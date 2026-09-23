// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;
using PhysicalAi.Course;
using PhysicalAi.Sensors;
using PhysicalAi.VehicleDynamics;
using UnityEngine;

namespace PhysicalAi.Planning
{
    [Serializable]
    public class PlanningDemonstrationFrame
    {
        public int SchemaVersion = 3;
        public int EpisodeId;
        public int Step;
        public float SimulationTime;
        public float[] Observation;
        public PlanningCommand Command;
        public string CommandSpace;
        public Vector3 DirectActuation; // throttle, brake, steering
        public string VehicleModel;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Velocity;
        public Vector3 FloatingOriginOffset;
        public float Reward;
        public bool Terminal;
        public string EndReason;
        public string ScenarioLabel;
        public int CameraFrameIndex = -1;
        public float CameraSimulationTime;
        public Vector3 CameraPosition;
        public Quaternion CameraRotation;
        public string CameraJpegBase64;
        public string MetricDepthFile;
        public string DepthValidityFile;
        public string DepthPreviewFile;
        public string BevLabelsFile;
        public string BevPreviewFile;
        public string AnnotationsFile;
    }

    [Serializable]
    public class DemonstrationSessionManifest
    {
        public int schemaVersion = 3;
        public string createdUtc;
        public string vehicleModel;
        public VehicleSpecification vehicleSpecification;
        public string mapMode;
        public string mapHash;
        public int seed;
        public string scenario;
        public string[] launchArguments;
        public string coordinateConvention = "Unity: x right, y up, z forward; metres, seconds; global position = Position + FloatingOriginOffset";
        public string commandConvention;
        public bool rgb;
        public bool depth;
        public bool bev;
        public float depthMaxMeters;
        public string depthConvention = "optical-axis z in metres; little-endian float32; top-left row-major; 0 invalid; validity u8 0/1; opaque/depth-writing surfaces only";
        public int bevSize;
        public float bevMetersPerPixel;
        public string bevConvention = "ground truth, not visible-only; row-major u8; top-left; ego +x right, +z up; cell center x=(column+0.5-size/2)*resolution,z=(size/2-row-0.5)*resolution";
        public string[] semanticClasses = BevAnnotationCapture.Classes;
        public float[] cameraIntrinsics;
        public string cameraCalibrationConvention = "K row-major: fx,0,cx;0,fy,cy;0,0,1; pixel edges at integers, centres +0.5; optical x right, y down, z forward; CameraPosition/Rotation are Unity world camera transform at render time; add FloatingOriginOffset for global position";
        public float cameraVerticalFovDegrees;
        public float cameraNearMeters;
        public float cameraFarMeters;
        public Vector3 cameraLocalPosition;
        public Quaternion cameraLocalRotation;
        public int cameraWidth;
        public int cameraHeight;
        public string frames = "frames.jsonl";
        public string events = "events.jsonl";
    }

    [Serializable]
    public class DemonstrationEvent
    {
        public string kind;
        public string label;
        public int episodeId;
        public int step;
        public float simulationTime;
        public Vector3 position;
        public Vector3 floatingOriginOffset;
        public CourseMapDocument map;
    }

    /// <summary>One self-describing, unique directory per episode/recording segment.</summary>
    public sealed class PlanningDemonstrationRecorder : MonoBehaviour
    {
        [SerializeField] string _datasetDirectory = "PhysicalAiDemonstrations";
        [SerializeField] string _filePrefix = "teleop";
        [SerializeField] string _scenarioLabel = "human-teleoperation";
        [SerializeField] bool _recordingEnabled;
        StreamWriter _writer;
        StreamWriter _events;
        int _episodeId;
        int _step;
        bool _hasEpisode;
        bool _rgb = true;
        bool _depthRequested;
        bool _bevRequested;
        string _depthFile, _validFile, _depthPreviewFile, _bevFile, _bevPreviewFile, _annotationsFile;
        Texture2D _cameraReadback;
        int _pendingCameraFrameIndex = -1;
        float _pendingCameraTime;
        Vector3 _pendingCameraPosition;
        Quaternion _pendingCameraRotation;
        string _pendingCameraJpeg;
        CombinedSlipVehicle _vehicle;
        PhysicalAiEpisodeOrchestrator _orchestrator;
        public string CurrentPath { get; private set; }
        public bool RecordingEnabled => _recordingEnabled;
        public string ScenarioLabel => _scenarioLabel;
        public int FramesWritten => _step;
        public string LastError { get; private set; }
        public string OutputDirectory => Path.GetFullPath(Path.IsPathRooted(_datasetDirectory) ||
            PhysicalAiLaunchArguments.GetValue("-physicalAiDemonstrationDirectory") != null ?
            _datasetDirectory : Path.Combine(Application.persistentDataPath, _datasetDirectory));

        void Awake()
        {
            _vehicle = GetComponent<CombinedSlipVehicle>();
            _orchestrator = GetComponent<PhysicalAiEpisodeOrchestrator>();
            _datasetDirectory = PhysicalAiLaunchArguments.GetValue("-physicalAiDemonstrationDirectory") ?? _datasetDirectory;
            _scenarioLabel = PhysicalAiLaunchArguments.GetValue("-physicalAiDemonstrationScenario") ?? _scenarioLabel;
            _rgb = PhysicalAiLaunchArguments.GetValue("-physicalAiRecordRgb") != "false";
            _depthRequested = PhysicalAiLaunchArguments.GetValue("-physicalAiDepth") == "true";
            _bevRequested = PhysicalAiLaunchArguments.GetValue("-physicalAiBev") == "true";
            if (PhysicalAiLaunchArguments.HasFlag("-physicalAiRecordReferenceDemonstrations"))
            {
                _filePrefix = "reference";
                _recordingEnabled = true;
            }
            _recordingEnabled |= PhysicalAiLaunchArguments.HasFlag("-physicalAiRecordOnStart");
        }

        void OnEnable()
        {
            if (_orchestrator != null) _orchestrator.StreamingTransitioned += OnWorldTransition;
        }

        public void SetRecordingEnabled(bool enabled)
        {
            if (_recordingEnabled == enabled && (!enabled || _writer != null || !_hasEpisode)) return;
            _recordingEnabled = enabled;
            if (enabled && _hasEpisode) OpenSegment();
            else if (!enabled) CloseSegment("operator-stop", false);
        }

        public void BeginEpisode(int episodeId)
        {
            CloseSegment("reset", false);
            _episodeId = episodeId;
            _hasEpisode = true;
            if (_recordingEnabled && (!PhysicalAiLaunchArguments.HasFlag("-physicalAiRlPort") || GetComponent<PythonRlBridge>()?.IsConnected == true)) OpenSegment();
        }

        /// <summary>Flush a paused trainer segment while retaining the requested recording selection for its next reset.</summary>
        public void SuspendTrainingSegment() => CloseSegment("trainer-stop", false);

        void OpenSegment()
        {
            CloseSegment("recording-restart", false);
            LastError = null;
            ClearCamera();
            _step = 0;
            try
            {
                var directory = Path.Combine(OutputDirectory,
                    Sanitize(_filePrefix) + "-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                Directory.CreateDirectory(directory);
                CurrentPath = Path.Combine(directory, "frames.jsonl");
                _writer = new StreamWriter(new FileStream(CurrentPath, FileMode.CreateNew, FileAccess.Write));
                _events = new StreamWriter(new FileStream(Path.Combine(directory, "events.jsonl"), FileMode.CreateNew, FileAccess.Write));
                var sensor = GetComponentInChildren<CameraObservationSensor>();
                var camera = sensor == null ? null : sensor.GetComponent<Camera>();
                var manifest = new DemonstrationSessionManifest
                {
                    createdUtc = DateTime.UtcNow.ToString("O"), vehicleModel = _vehicle == null ? "unknown" : _vehicle.VehicleModelName,
                    vehicleSpecification = _vehicle == null ? default : _vehicle.Specification,
                    mapMode = _orchestrator == null ? "unknown" : _orchestrator.MapMode,
                    mapHash = _orchestrator?.MapHash, seed = _episodeId, scenario = _scenarioLabel,
                    launchArguments = PhysicalAiLaunchArguments.Current, commandConvention = CommandSpace(), rgb = _rgb,
                    depth = _depthRequested, bev = _bevRequested,
                    depthMaxMeters = PhysicalAiLaunchArguments.GetFloat("-physicalAiDepthMaxMeters", 30),
                    bevSize = PhysicalAiLaunchArguments.GetInt("-physicalAiBevSize", 128),
                    bevMetersPerPixel = PhysicalAiLaunchArguments.GetFloat("-physicalAiBevMetersPerPixel", 0.1f),
                    cameraVerticalFovDegrees = camera == null ? 0 : camera.fieldOfView,
                    cameraNearMeters = camera == null ? 0 : camera.nearClipPlane,
                    cameraFarMeters = camera == null ? 0 : camera.farClipPlane,
                    cameraLocalPosition = camera == null ? default : camera.transform.localPosition,
                    cameraLocalRotation = camera == null ? Quaternion.identity : camera.transform.localRotation,
                    cameraWidth = sensor?.LatestImage == null ? 0 : sensor.LatestImage.width,
                    cameraHeight = sensor?.LatestImage == null ? 0 : sensor.LatestImage.height
                };
                if (camera != null)
                {
                    var p = camera.projectionMatrix;
                    manifest.cameraIntrinsics = new[] { p.m00 * manifest.cameraWidth / 2, 0, (1 - p.m02) * manifest.cameraWidth / 2,
                        0, p.m11 * manifest.cameraHeight / 2, (1 + p.m12) * manifest.cameraHeight / 2, 0, 0, 1 };
                }
                File.WriteAllText(Path.Combine(directory, "manifest.json"), JsonUtility.ToJson(manifest, true));
                WriteEvent("recording-start", "", true);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException)
            { Fail(error); }
        }

        string CommandSpace() => _orchestrator != null && _orchestrator.ManualActuationOverride ?
            "manual-direct-actuation; Command is a legacy proxy, not a measured planning target" : "planning-speed-lateral-offset";

        public void Record(PlanningObservation observation, PlanningCommand command, float reward)
        {
            if (_writer == null) return;
            var frame = NewFrame();
            frame.Observation = observation.Features;
            frame.Command = command;
            frame.Reward = reward;
            frame.CameraFrameIndex = _pendingCameraFrameIndex;
            frame.CameraSimulationTime = _pendingCameraTime;
            frame.CameraPosition = _pendingCameraPosition;
            frame.CameraRotation = _pendingCameraRotation;
            frame.CameraJpegBase64 = _pendingCameraJpeg;
            frame.MetricDepthFile = _depthFile; frame.DepthValidityFile = _validFile; frame.DepthPreviewFile = _depthPreviewFile;
            frame.BevLabelsFile = _bevFile; frame.BevPreviewFile = _bevPreviewFile; frame.AnnotationsFile = _annotationsFile;
            WriteFrame(frame);
            ClearCamera();
        }

        PlanningDemonstrationFrame NewFrame()
        {
            var body = _vehicle == null ? null : _vehicle.GetComponent<Rigidbody>();
            return new PlanningDemonstrationFrame
            {
                EpisodeId = _episodeId, Step = _step, SimulationTime = Time.time, ScenarioLabel = _scenarioLabel,
                VehicleModel = _vehicle == null ? "unknown" : _vehicle.VehicleModelName, CommandSpace = CommandSpace(),
                DirectActuation = _vehicle == null ? default : new Vector3(_vehicle.Throttle, _vehicle.Brake, _vehicle.Steering),
                Position = body == null ? transform.position : body.position,
                Rotation = body == null ? transform.rotation : body.rotation,
                Velocity = body == null ? default : body.linearVelocity,
                FloatingOriginOffset = _orchestrator == null ? default : _orchestrator.FloatingOriginOffset
            };
        }

        public void QueueCameraFrame(CameraObservationFrame frame)
        {
            if (_writer == null || frame.Image == null || frame.FrameIndex == _pendingCameraFrameIndex ||
                SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            var previous = RenderTexture.active;
            try
            {
                if (_rgb)
                {
                    RenderTexture.active = frame.Image;
                    if (_cameraReadback == null || _cameraReadback.width != frame.Image.width || _cameraReadback.height != frame.Image.height)
                    {
                        if (_cameraReadback != null) Destroy(_cameraReadback);
                        _cameraReadback = new Texture2D(frame.Image.width, frame.Image.height, TextureFormat.RGB24, false);
                    }
                    _cameraReadback.ReadPixels(new Rect(0, 0, frame.Image.width, frame.Image.height), 0, 0);
                    _cameraReadback.Apply(false);
                    _pendingCameraJpeg = Convert.ToBase64String(_cameraReadback.EncodeToJPG(75));
                }
                _pendingCameraFrameIndex = frame.FrameIndex;
                _pendingCameraTime = frame.SimulationTime;
                _pendingCameraPosition = frame.Position;
                _pendingCameraRotation = frame.Rotation;
                if (_depthRequested)
                {
                    if (frame.Depth == null) { Fail(new InvalidOperationException("Requested metric depth was not captured. Check HDRP custom-pass support.")); return; }
                    var bytes = new byte[frame.Depth.Metres.Length * sizeof(float)];
                    Buffer.BlockCopy(frame.Depth.Metres, 0, bytes, 0, bytes.Length);
                    if (!BitConverter.IsLittleEndian) for (var i = 0; i < bytes.Length; i += 4) Array.Reverse(bytes, i, 4);
                    _depthFile = SaveSensor(frame.FrameIndex, ".depth.f32", bytes);
                    _validFile = SaveSensor(frame.FrameIndex, ".depth-valid.u8", frame.Depth.Valid);
                    _depthPreviewFile = SaveSensor(frame.FrameIndex, ".depth.png", frame.Depth.Preview.EncodeToPNG());
                }
                if (_bevRequested)
                {
                    var bev = _orchestrator?.CurrentBev;
                    if (bev == null || bev.Annotations.frameIndex != frame.FrameIndex)
                    { Fail(new InvalidOperationException("BEV and camera frame indices do not match.")); return; }
                    _bevFile = SaveSensor(frame.FrameIndex, ".bev.u8", bev.Labels);
                    _bevPreviewFile = SaveSensor(frame.FrameIndex, ".bev.png", bev.Preview.EncodeToPNG());
                    _annotationsFile = SaveSensor(frame.FrameIndex, ".annotations.json", System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(bev.Annotations)));
                }
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { Fail(error); }
            finally { RenderTexture.active = previous; }
        }

        string SaveSensor(int frame, string suffix, byte[] bytes)
        {
            var relative = "sensors/" + frame.ToString("D8") + suffix;
            var path = Path.Combine(Path.GetDirectoryName(CurrentPath), relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            output.Write(bytes, 0, bytes.Length);
            return relative;
        }

        public void AddAnnotation(string label)
        {
            if (_events != null && !string.IsNullOrWhiteSpace(label)) WriteEvent("operator-tag", label.Trim(), false);
        }
        void OnWorldTransition(StreamingCourseTransition transition) => WriteEvent(transition.Kind.ToString(), "", true);
        void WriteEvent(string kind, string label, bool includeMap)
        {
            if (_events == null) return;
            try
            {
                _events.WriteLine(JsonUtility.ToJson(new DemonstrationEvent
                {
                    kind = kind, label = label, episodeId = _episodeId, step = _step, simulationTime = Time.time,
                    position = _vehicle == null ? transform.position : _vehicle.GetComponent<Rigidbody>().position,
                    floatingOriginOffset = _orchestrator == null ? default : _orchestrator.FloatingOriginOffset,
                    map = includeMap && _orchestrator?.Course != null ? _orchestrator.CaptureMap() : null
                }));
                _events.Flush();
            }
            catch (IOException error) { Fail(error); }
        }
        public void EndEpisode(bool terminal = true)
        {
            CloseSegment(terminal ? "episode-ended" : "closed", terminal);
            _hasEpisode = false;
        }
        public void EndEpisode(PlanningObservation observation, PlanningCommand command, float terminalReward)
        {
            if (_writer != null)
            {
                var frame = NewFrame();
                frame.Terminal = true; frame.EndReason = "episode-ended";
                frame.Observation = observation.Features; frame.Command = command; frame.Reward = terminalReward;
                WriteFrame(frame);
            }
            DisposeWriters();
            _hasEpisode = false;
        }
        void CloseSegment(string reason, bool terminal)
        {
            if (_writer != null)
            {
                var frame = NewFrame();
                frame.Terminal = terminal; frame.EndReason = reason;
                WriteFrame(frame);
            }
            DisposeWriters();
        }
        void DisposeWriters()
        {
            _writer?.Dispose(); _writer = null;
            _events?.Dispose(); _events = null;
            ClearCamera();
        }
        void Fail(Exception error)
        {
            LastError = error.Message;
            _recordingEnabled = false;
            try { _writer?.Dispose(); } catch (IOException) { }
            try { _events?.Dispose(); } catch (IOException) { }
            _writer = null; _events = null;
            Debug.LogWarning("Physical AI recording stopped: " + LastError);
        }
        void WriteFrame(PlanningDemonstrationFrame frame)
        {
            try { _writer.WriteLine(JsonUtility.ToJson(frame)); _writer.Flush(); _step++; }
            catch (IOException error) { Fail(error); }
        }
        void ClearCamera()
        {
            _pendingCameraFrameIndex = -1; _pendingCameraJpeg = null; _pendingCameraTime = 0;
            _pendingCameraPosition = default; _pendingCameraRotation = default;
            _depthFile = _validFile = _depthPreviewFile = _bevFile = _bevPreviewFile = _annotationsFile = null;
        }
        void OnDisable()
        {
            if (_orchestrator != null) _orchestrator.StreamingTransitioned -= OnWorldTransition;
            CloseSegment("disabled", false);
        }
        void OnDestroy() { if (_cameraReadback != null) Destroy(_cameraReadback); }
        static string Sanitize(string value)
        {
            foreach (var invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
            return string.IsNullOrWhiteSpace(value) ? "teleop" : value;
        }
    }
}
