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

        public CameraObservationFrame(RenderTexture image, int frameIndex, float simulationTime)
        {
            Image = image;
            FrameIndex = frameIndex;
            SimulationTime = simulationTime;
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
        public int FrameIndex { get; private set; }
        public RenderTexture LatestImage => _target;

        void Awake()
        {
            if (_camera == null)
                _camera = GetComponent<Camera>();
            AllocateTarget();
        }

        void OnDisable() => ReleaseTarget();

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
            _camera.Render();
            FrameIndex++;
            return true;
        }

        public CameraObservationFrame CurrentFrame()
        {
            if (_target == null)
                AllocateTarget();
            return new CameraObservationFrame(_target, FrameIndex, Time.time);
        }

        void AllocateTarget()
        {
            ReleaseTarget();
            _target = new RenderTexture(_width, _height, 24, RenderTextureFormat.ARGB32)
            {
                name = $"{name}-CameraObservation",
                useMipMap = false,
                autoGenerateMips = false
            };
            _target.Create();
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
