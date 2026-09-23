// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;
using System.Net.Sockets;
using PhysicalAi.Course;
using PhysicalAi.Sensors;
using PhysicalAi.VehicleDynamics;
using PhysicalAi.Randomization;
using UnityEngine;

namespace PhysicalAi.Planning
{
    [Serializable]
    public class RlEnvironmentContract
    {
        public int schemaVersion = 1;
        public string observationSchema = "planning-vector-v1:4+5-per-waypoint;RGB160x90-policy-input;depth-and-BEV-are-separate-ground-truth-targets";
        public string actionSchema = "planning-desired-speed-mps-and-normalized-lateral-offset";
        public string mapMode, mapSourceHash, courseSettings, vehicleModel, vehicleSpecification, scenario, randomizationSettings;
        public int baseSeed, cameraWidth, cameraHeight, cameraHz, bevSize;
        public float fixedStepSeconds, maximumEpisodeSeconds, depthMaxMeters, bevMetersPerPixel, frictionScale;
        public bool depth, bev;
        public Vector3 cameraLocalPosition;
        public Quaternion cameraLocalRotation;
    }

    /// <summary>Single-environment JSON-lines TCP bridge for a Python RL policy process.</summary>
    public sealed class PythonRlBridge : MonoBehaviour
    {
        [SerializeField] PhysicalAiEpisodeOrchestrator _orchestrator;
        [SerializeField] string _host = "127.0.0.1";
        [SerializeField, Min(1)] int _port = 5015;
        [SerializeField] bool _connectOnStart;
        [SerializeField, Min(0.01f)] float _decisionPeriodSeconds = 0.1f;

        TcpClient _client;
        StreamReader _reader;
        StreamWriter _writer;
        PlanningCommand _lastCommand;
        float _pendingReward;
        bool _connected;
        Texture2D _cameraReadback;
        string _latestCameraJpeg;
        bool _imageBlackout;
        bool _loggedFirstCameraFrame;
        bool _loggedFirstRequest;
        bool _loggedFirstWrite;
        float _nextDecisionTime;
        bool _managed;
        int _cameraFrameIndex;
        float _cameraSimulationTime;
        bool _requested;
        public bool IsConnected => _connected;
        public bool IsManaged => _managed;
        public string LastError { get; private set; }
        public int CompletedEpisodes { get; private set; }
        public event Action TrainingRequested;
        public event Action TrainingCancelled;
        public event Action TrainingCompleted;
        /// <summary>Number of rendered camera frames converted to JPEG for this bridge lifetime.</summary>
        public int CameraFramesCaptured { get; private set; }
        /// <summary>Number of camera frames intentionally withheld by the image ablation.</summary>
        public int CameraFramesWithheld { get; private set; }

        [Serializable] class Request { public string type; public float[] observation; public string cameraJpegBase64; public float reward; public bool terminal; public string reason; public RlEnvironmentContract environment;
            public int episodeId, cameraFrameIndex; public float simulationTime, cameraSimulationTime; }
        [Serializable] class Response { public float desiredSpeed; public float desiredLateralOffsetNormalized; public bool ack; public int completedEpisodes; public string error; }

        void Awake()
        {
            if (_orchestrator == null) _orchestrator = GetComponent<PhysicalAiEpisodeOrchestrator>();
            _managed = GetCommandLineArg("-physicalAiRlManaged") == "true";
            _requested = _managed || HasCommandLineArgument("-physicalAiRlPort") || GetCommandLineArg("-physicalAiMode") == "rl";
            _port = PhysicalAiLaunchArguments.GetInt("-physicalAiRlPort", _port);
        }

        void Start()
        {
            // Keep the authored scene disconnected by default.  A player build can opt in
            // without changing scene YAML: -physicalAiRlPort 5015.
            _imageBlackout = HasCommandLineArgument("-physicalAiImageBlackout");
            if (_managed)
            {
                Time.timeScale = 0;
                gameObject.AddComponent<OperatorRlSession>();
                return;
            }
            var port = GetCommandLineArg("-physicalAiRlPort");
            if (int.TryParse(port, out var requestedPort) && requestedPort > 0)
            {
                _port = requestedPort;
                Connect();
            }
            else if (_connectOnStart) Connect();
        }

        void Update()
        {
            // A teleoperation session may temporarily own the command source. Reclaim it only
            // after that source explicitly releases the slot, never while another source owns it.
            if (_connected && _orchestrator != null && _orchestrator.CommandProvider == null)
                _orchestrator.CommandProvider = RequestCommand;
        }

        void OnEnable()
        {
            if (_orchestrator == null) return;
            _orchestrator.EpisodeFinished += OnEpisodeFinished;
            _orchestrator.StepRewardReady += OnStepReward;
            _orchestrator.CameraObservationReady += OnCameraObservation;
            _orchestrator.CourseGenerated += OnCourseGenerated;
        }

        void OnDisable()
        {
            if (_orchestrator != null)
            {
                _orchestrator.EpisodeFinished -= OnEpisodeFinished;
                _orchestrator.StepRewardReady -= OnStepReward;
                _orchestrator.CameraObservationReady -= OnCameraObservation;
                _orchestrator.CourseGenerated -= OnCourseGenerated;
                if (_orchestrator.CommandProvider == RequestCommand) _orchestrator.CommandProvider = null;
            }
            Disconnect();
        }

        public bool Connect()
        {
            Disconnect();
            LastError = null;
            try
            {
                _client = new TcpClient();
                _client.SendTimeout = 1000;
                // CUDA/PyTorch lazily initialises on the first inference. A one-second
                // socket timeout disconnects the rollout before its first action even
                // reaches the policy, while subsequent decisions are normally much
                // faster. Keep the synchronous planning contract but permit that
                // bounded startup cost.
                _client.ReceiveTimeout = 10000;
                if (!_client.ConnectAsync(_host, _port).Wait(2000)) throw new IOException("Connection timed out.");
                var stream = _client.GetStream();
                _reader = new StreamReader(stream);
                _writer = new StreamWriter(stream) { AutoFlush = true };
                if (_managed)
                {
                    _writer.WriteLine(JsonUtility.ToJson(new Request { type = "hello", environment = EnvironmentContract() }));
                    ReadAcknowledgement();
                }
                _connected = true;
                if (_orchestrator != null) _orchestrator.CommandProvider = RequestCommand;
                return true;
            }
            catch (Exception error) when (error is SocketException || error is IOException || error is AggregateException || error is ArgumentException)
            {
                PauseWithError(error.GetBaseException().Message);
                return false;
            }
        }

        public RlEnvironmentContract EnvironmentContract()
        {
            var sensor = GetComponentInChildren<CameraObservationSensor>();
            return new RlEnvironmentContract
            {
                mapMode = _orchestrator.MapMode, mapSourceHash = _orchestrator.MapSourceHash ?? "seed-generated",
                courseSettings = _orchestrator.CourseSettingsJson, vehicleModel = _orchestrator.VehicleModelName,
                vehicleSpecification = JsonUtility.ToJson(GetComponent<CombinedSlipVehicle>().Specification),
                randomizationSettings = JsonUtility.ToJson(GetComponent<DomainRandomizer>().Settings),
                frictionScale = GetComponent<DomainRandomizer>().FrictionScale,
                scenario = GetCommandLineArg("-physicalAiDemonstrationScenario") ?? "nominal",
                baseSeed = PhysicalAiLaunchArguments.GetInt("-physicalAiSeed", _orchestrator.EpisodeSeed),
                cameraWidth = sensor.LatestImage.width, cameraHeight = sensor.LatestImage.height,
                cameraLocalPosition = sensor.transform.localPosition, cameraLocalRotation = sensor.transform.localRotation,
                cameraHz = PhysicalAiLaunchArguments.GetInt("-physicalAiCameraHz", 15), fixedStepSeconds = Time.fixedDeltaTime,
                maximumEpisodeSeconds = PhysicalAiLaunchArguments.GetFloat("-physicalAiMaximumEpisodeSeconds", 120),
                depth = GetCommandLineArg("-physicalAiDepth") == "true", bev = GetCommandLineArg("-physicalAiBev") == "true",
                depthMaxMeters = PhysicalAiLaunchArguments.GetFloat("-physicalAiDepthMaxMeters", 30),
                bevSize = PhysicalAiLaunchArguments.GetInt("-physicalAiBevSize", 128),
                bevMetersPerPixel = PhysicalAiLaunchArguments.GetFloat("-physicalAiBevMetersPerPixel", 0.1f)
            };
        }

        public void StartTrainingEpisodes()
        {
            if (!_connected || TrainingRequested == null) throw new InvalidOperationException("Connected bridge and evaluation runner are required.");
            TrainingRequested.Invoke();
        }
        public void NotifyTrainingCompleted() => TrainingCompleted?.Invoke();
        public void StopTraining()
        {
            Time.timeScale = 0;
            TrainingCancelled?.Invoke();
            if (_connected)
            {
                try
                {
                    _writer.WriteLine(JsonUtility.ToJson(new Request { type = "stop", reward = _pendingReward, reason = "operator-stop" }));
                    ReadAcknowledgement();
                    _pendingReward = 0;
                }
                catch (Exception error) when (error is IOException || error is ArgumentException) { PauseWithError(error.Message); }
            }
            Disconnect();
        }

        void OnCourseGenerated(GeneratedCourse course)
        {
            _pendingReward = 0;
            _lastCommand = default;
            _latestCameraJpeg = null;
            _nextDecisionTime = 0;
        }

        Response ReadResponse()
        {
            var line = _reader.ReadLine();
            if (string.IsNullOrEmpty(line)) throw new IOException("Trainer closed the connection.");
            var response = JsonUtility.FromJson<Response>(line);
            if (response == null) throw new IOException("Invalid trainer response.");
            if (!string.IsNullOrEmpty(response.error)) throw new IOException(response.error);
            if (!response.ack && (!line.Contains("\"desiredSpeed\"") || !line.Contains("\"desiredLateralOffsetNormalized\"")))
                throw new IOException("Trainer response is missing action fields.");
            return response;
        }
        void ReadAcknowledgement()
        {
            var response = ReadResponse();
            if (!response.ack) throw new IOException("Trainer did not acknowledge saved state.");
            CompletedEpisodes = response.completedEpisodes;
        }

        void PauseWithError(string message)
        {
            LastError = "RL stopped: " + message;
            Disconnect();
            _lastCommand = default;
            if (_requested) Time.timeScale = 0;
            Debug.LogWarning(LastError);
            if (PhysicalAiLaunchArguments.HasFlag("-physicalAiQuitAfterEvaluation")) Application.Quit(2);
        }

        PlanningCommand RequestCommand(PlanningObservation observation)
        {
            if (!_connected) return default;
            if (_managed && string.IsNullOrEmpty(_latestCameraJpeg)) return default;
            if (Time.fixedTime + 0.0001f < _nextDecisionTime) return _lastCommand;
            if (!_loggedFirstRequest)
            {
                Debug.Log("Physical AI RL bridge: first planning request.");
                _loggedFirstRequest = true;
            }
            try
            {
                _writer.WriteLine(JsonUtility.ToJson(new Request { type = "step", observation = observation.Features, cameraJpegBase64 = _latestCameraJpeg, reward = _pendingReward, terminal = false,
                    episodeId = _orchestrator.EpisodeSeed, simulationTime = Time.time, cameraFrameIndex = _cameraFrameIndex, cameraSimulationTime = _cameraSimulationTime }));
                if (!_loggedFirstWrite)
                {
                    Debug.Log("Physical AI RL bridge: first request written; waiting for action.");
                    _loggedFirstWrite = true;
                }
                var response = ReadResponse();
                if (response.ack) throw new IOException("Trainer sent an acknowledgement instead of an action.");
                if (!float.IsFinite(response.desiredSpeed) || !float.IsFinite(response.desiredLateralOffsetNormalized))
                    throw new IOException("Trainer returned a nonfinite action.");
                _pendingReward = 0f;
                _nextDecisionTime = Time.fixedTime + _decisionPeriodSeconds;
                _lastCommand = new PlanningCommand
                {
                    DesiredSpeed = Mathf.Max(0f, response.desiredSpeed),
                    DesiredLateralOffsetNormalized = Mathf.Clamp(response.desiredLateralOffsetNormalized, -1f, 1f)
                };
            }
            catch (Exception error) when (error is IOException || error is ArgumentException) { PauseWithError(error.Message); }
            return _lastCommand;
        }

        void OnEpisodeFinished(bool completed, bool collision, bool offCourse)
        {
            if (!_connected) return;
            try
            {
                Debug.Log("Physical AI RL bridge: sending terminal transition.");
                _writer.WriteLine(JsonUtility.ToJson(new Request { type = "terminal", reward = _pendingReward, terminal = true,
                    reason = completed ? "completed" : collision ? "collision" : offCourse ? "off-course" : "timeout" }));
                // The evaluation runner may quit the player immediately after this
                // callback. Wait for the trainer to persist its terminal update so a
                // valid rollout cannot lose its checkpoint or learning-curve point.
                ReadAcknowledgement();
                _pendingReward = 0;
                _lastCommand = default;
                _latestCameraJpeg = null;
                _nextDecisionTime = 0;
            }
            catch (Exception error) when (error is IOException || error is ArgumentException) { PauseWithError(error.Message); }
        }

        void OnStepReward(float reward) { if (_connected) _pendingReward += reward; }

        void OnCameraObservation(CameraObservationFrame frame)
        {
            // Evaluation ablation: preserve the bridge cadence and vector
            // observation, but withhold every camera JPEG. The visual servers
            // map an absent image to their zero image tensor, so normal and
            // blackout runs differ only in the primary visual observation.
            if (_imageBlackout)
            {
                if (!_loggedFirstCameraFrame)
                {
                    Debug.Log("Physical AI RL bridge: camera blackout enabled; JPEG frames are withheld.");
                    _loggedFirstCameraFrame = true;
                }
                CameraFramesWithheld++;
                _latestCameraJpeg = null;
                return;
            }
            if (!_connected || frame.Image == null || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                return;
            var previous = RenderTexture.active;
            RenderTexture.active = frame.Image;
            if (_cameraReadback == null || _cameraReadback.width != frame.Image.width || _cameraReadback.height != frame.Image.height)
            {
                if (_cameraReadback != null) Destroy(_cameraReadback);
                _cameraReadback = new Texture2D(frame.Image.width, frame.Image.height, TextureFormat.RGB24, false);
            }
            _cameraReadback.ReadPixels(new Rect(0, 0, frame.Image.width, frame.Image.height), 0, 0);
            _cameraReadback.Apply(false);
            RenderTexture.active = previous;
            _latestCameraJpeg = Convert.ToBase64String(_cameraReadback.EncodeToJPG(75));
            _cameraFrameIndex = frame.FrameIndex;
            _cameraSimulationTime = frame.SimulationTime;
            CameraFramesCaptured++;
            if (!_loggedFirstCameraFrame)
            {
                Debug.Log($"Physical AI RL bridge: first camera JPEG ({frame.Image.width}x{frame.Image.height}) captured.");
                _loggedFirstCameraFrame = true;
            }
        }

        void Disconnect()
        {
            _connected = false;
            _loggedFirstRequest = false;
            _loggedFirstWrite = false;
            _loggedFirstCameraFrame = false;
            _nextDecisionTime = 0f;
            if (_orchestrator != null && _orchestrator.CommandProvider == RequestCommand)
                _orchestrator.CommandProvider = null;
            try { _writer?.Dispose(); } catch (IOException) { }
            try { _reader?.Dispose(); } catch (IOException) { }
            _client?.Dispose();
            _reader = null; _writer = null; _client = null;
        }

        void OnDestroy()
        {
            if (_cameraReadback != null) Destroy(_cameraReadback);
            if (_requested) Time.timeScale = 1;
        }

        void OnGUI()
        {
            if (!_managed && _requested && LastError != null)
                GUI.Box(new Rect(12, 12, Mathf.Min(700, Screen.width - 24), 80), LastError + "\nSimulation paused; no fallback policy is driving.");
        }

        static string GetCommandLineArg(string parameter) => PhysicalAiLaunchArguments.GetValue(parameter);

        static bool HasCommandLineArgument(string parameter)
        {
            foreach (var argument in PhysicalAiLaunchArguments.Current)
                if (argument == parameter) return true;
            return false;
        }
    }
}
