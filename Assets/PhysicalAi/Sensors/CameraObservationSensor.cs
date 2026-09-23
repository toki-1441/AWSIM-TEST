// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using UnityEngine;
using UnityEngine.Rendering;

namespace PhysicalAi.Sensors
{
    /// <summary>Rendered camera frame made available to external learning bridges without changing vector-observation compatibility.</summary>
    public readonly struct CameraObservationFrame
    {
        public readonly RenderTexture Image;
        public readonly int FrameIndex;
        public readonly float SimulationTime;
        public readonly MetricDepthCapture Depth;
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;

        public CameraObservationFrame(RenderTexture image, int frameIndex, float simulationTime, MetricDepthCapture depth = null,
            Vector3 position = default, Quaternion rotation = default)
        {
            Image = image;
            FrameIndex = frameIndex;
            SimulationTime = simulationTime;
            Depth = depth;
            Position = position;
            Rotation = rotation;
        }
    }

    [RequireComponent(typeof(Camera))]
    public sealed class CameraObservationSensor : MonoBehaviour
    {
        [SerializeField] Camera _camera;
        [SerializeField, Min(16)] int _width = 160;
        [SerializeField, Min(16)] int _height = 90;
        [SerializeField, Min(1)] int _outputHz = 15;

        RenderTexture _target;
        float _nextCaptureTime;
        MetricDepthCapture _depth;
        bool _depthReady;
        float _captureTime;
        Vector3 _capturePosition;
        Quaternion _captureRotation;
        public Texture2D DepthPreview => _depth?.Preview;
        public string DepthError => _depth?.LastError;
        public int FrameIndex { get; private set; }
        public RenderTexture LatestImage => _target;

        void Awake()
        {
            if (_camera == null)
                _camera = GetComponent<Camera>();
            _width = Mathf.Clamp(PhysicalAi.Configuration.RuntimeArguments.GetInt("-physicalAiCameraWidth", _width), 16, 1920);
            _height = Mathf.Clamp(PhysicalAi.Configuration.RuntimeArguments.GetInt("-physicalAiCameraHeight", _height), 16, 1080);
            _outputHz = Mathf.Clamp(PhysicalAi.Configuration.RuntimeArguments.GetInt("-physicalAiCameraHz", _outputHz), 1, 60);
            AllocateTarget();
        }

        void OnDisable() { _depth?.Dispose(); _depth = null; ReleaseTarget(); }

        public bool CaptureIfDue()
        {
            if (Time.time + 0.0001f < _nextCaptureTime)
                return false;
            _nextCaptureTime = Time.time + 1f / _outputHz;
            return Capture();
        }

        public bool Capture()
        {
            if (_camera == null)
                return false;
            // HDRP cannot render through Unity's Null graphics device used by
            // headless Linux tests. Keep the episode running; a GPU player will
            // capture the same frame on the next policy step.
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                return false;
            if (_target == null)
                AllocateTarget();
            if (_target == null)
                return false;
            _camera.targetTexture = _target;
            if (PhysicalAi.Configuration.RuntimeArguments.GetValue("-physicalAiDepth") == "true" && _depth == null)
                _depth = new MetricDepthCapture(_camera, _width, _height,
                    PhysicalAi.Configuration.RuntimeArguments.GetFloat("-physicalAiDepthMaxMeters", 30));
            _depth?.Prepare();
            _captureTime = Time.time;
            _capturePosition = _camera.transform.position;
            _captureRotation = _camera.transform.rotation;
            _camera.Render();
            _depthReady = _depth != null && _depth.Read();
            FrameIndex++;
            return true;
        }

        public CameraObservationFrame CurrentFrame()
        {
            if (_target == null)
                AllocateTarget();
            return new CameraObservationFrame(_target, FrameIndex, _captureTime, _depthReady ? _depth : null,
                _capturePosition, _captureRotation);
        }

        void AllocateTarget()
        {
            // CurrentFrame is also used by editor tooling before MonoBehaviour.Awake runs.
            if (_camera == null) _camera = GetComponent<Camera>();
            ReleaseTarget();
            _target = new RenderTexture(_width, _height, 24, RenderTextureFormat.ARGB32)
            {
                name = $"{name}-CameraObservation",
                useMipMap = false,
                autoGenerateMips = false
            };
            _target.Create();
            // Establish calibration before the recorder opens its manifest, not only at first capture.
            _camera.targetTexture = _target;
            _camera.aspect = (float)_width / _height;
        }

        void ReleaseTarget()
        {
            if (_target == null)
                return;
            if (_camera != null && _camera.targetTexture == _target)
                _camera.targetTexture = null;
            _target.Release();
            Destroy(_target);
            _target = null;
        }
    }
}
