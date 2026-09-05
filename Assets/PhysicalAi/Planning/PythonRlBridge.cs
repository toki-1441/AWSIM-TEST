// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;
using System.Net.Sockets;
using PhysicalAi.Sensors;
using UnityEngine;

namespace PhysicalAi.Planning
{
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
        /// <summary>Number of rendered camera frames converted to JPEG for this bridge lifetime.</summary>
        public int CameraFramesCaptured { get; private set; }
        /// <summary>Number of camera frames intentionally withheld by the image ablation.</summary>
        public int CameraFramesWithheld { get; private set; }

        [Serializable] class Request { public string type; public float[] observation; public string cameraJpegBase64; public float reward; public bool terminal; }
        [Serializable] class Response { public float desiredSpeed; public float desiredLateralOffsetNormalized; }

        void Start()
        {
            // Keep the authored scene disconnected by default.  A player build can opt in
            // without changing scene YAML: -physicalAiRlPort 5015.
            _imageBlackout = HasCommandLineArgument("-physicalAiImageBlackout");
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
        }

        void OnDisable()
        {
            if (_orchestrator != null)
            {
                _orchestrator.EpisodeFinished -= OnEpisodeFinished;
                _orchestrator.StepRewardReady -= OnStepReward;
                _orchestrator.CameraObservationReady -= OnCameraObservation;
                if (_orchestrator.CommandProvider == RequestCommand) _orchestrator.CommandProvider = null;
            }
            Disconnect();
        }

        public bool Connect()
        {
            Disconnect();
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
                _client.Connect(_host, _port);
                var stream = _client.GetStream();
                _reader = new StreamReader(stream);
                _writer = new StreamWriter(stream) { AutoFlush = true };
                _connected = true;
                if (_orchestrator != null) _orchestrator.CommandProvider = RequestCommand;
                return true;
            }
            catch (SocketException)
            {
                Disconnect();
                return false;
            }
        }

        PlanningCommand RequestCommand(PlanningObservation observation)
        {
            if (!_connected) return _lastCommand;
            if (Time.fixedTime + 0.0001f < _nextDecisionTime) return _lastCommand;
            if (!_loggedFirstRequest)
            {
                Debug.Log("Physical AI RL bridge: first planning request.");
                _loggedFirstRequest = true;
            }
            try
            {
                _writer.WriteLine(JsonUtility.ToJson(new Request { type = "step", observation = observation.Features, cameraJpegBase64 = _latestCameraJpeg, reward = _pendingReward, terminal = false }));
                if (!_loggedFirstWrite)
                {
                    Debug.Log("Physical AI RL bridge: first request written; waiting for action.");
                    _loggedFirstWrite = true;
                }
                var line = _reader.ReadLine();
                var response = string.IsNullOrEmpty(line) ? null : JsonUtility.FromJson<Response>(line);
                if (response == null)
                {
                    Disconnect();
                    return _lastCommand;
                }
                _pendingReward = 0f;
                _nextDecisionTime = Time.fixedTime + _decisionPeriodSeconds;
                _lastCommand = new PlanningCommand
                {
                    DesiredSpeed = Mathf.Max(0f, response.desiredSpeed),
                    DesiredLateralOffsetNormalized = Mathf.Clamp(response.desiredLateralOffsetNormalized, -1f, 1f)
                };
            }
            catch (IOException) { Disconnect(); }
            return _lastCommand;
        }

        void OnEpisodeFinished(bool completed, bool collision, bool offCourse)
        {
            if (!_connected) return;
            try
            {
                Debug.Log("Physical AI RL bridge: sending terminal transition.");
                _writer.WriteLine(JsonUtility.ToJson(new Request { type = "terminal", reward = _pendingReward, terminal = true }));
                // The evaluation runner may quit the player immediately after this
                // callback. Wait for the trainer to persist its terminal update so a
                // valid rollout cannot lose its checkpoint or learning-curve point.
                _reader.ReadLine();
            }
            catch (IOException) { Disconnect(); }
        }

        void OnStepReward(float reward) => _pendingReward += reward;

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
            _reader?.Dispose(); _writer?.Dispose(); _client?.Dispose();
            _reader = null; _writer = null; _client = null;
        }

        void OnDestroy()
        {
            if (_cameraReadback != null) Destroy(_cameraReadback);
        }

        static string GetCommandLineArg(string parameter)
        {
            var arguments = Environment.GetCommandLineArgs();
            for (var index = 0; index + 1 < arguments.Length; index++)
                if (arguments[index] == parameter) return arguments[index + 1];
            return null;
        }

        static bool HasCommandLineArgument(string parameter)
        {
            foreach (var argument in Environment.GetCommandLineArgs())
                if (argument == parameter) return true;
            return false;
        }
    }
}
