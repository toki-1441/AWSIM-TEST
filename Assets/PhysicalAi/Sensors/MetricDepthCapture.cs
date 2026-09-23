// Copyright 2026
// SPDX-License-Identifier: Apache-2.0
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace PhysicalAi.Sensors
{
    /// <summary>Optical-axis depth from the SAME HDRP camera render, not collider ray distances.</summary>
    public sealed class MetricDepthCapture : IDisposable
    {
        [Serializable]
        sealed class DepthPass : CustomPass
        {
            public Camera Camera;
            public RenderTexture Output;
            public Material Material;
            public bool Executed;
            protected override void Execute(CustomPassContext ctx)
            {
                if (ctx.hdCamera.camera != Camera) return;
                ctx.cmd.SetRenderTarget(Output);
                ctx.cmd.SetViewport(new Rect(0, 0, Output.width, Output.height));
                CoreUtils.DrawFullScreen(ctx.cmd, Material, shaderPassId: 0);
                Executed = true;
            }
        }
        readonly RenderTexture _target;
        readonly Texture2D _readback;
        readonly Material _material;
        readonly CustomPassVolume _volume;
        readonly DepthPass _pass;
        readonly float _maxMeters;
        public Texture2D Preview { get; }
        public int Width => _target.width;
        public int Height => _target.height;
        // Stored arrays have top-left origin, row-major x-right/y-down.
        public float[] Metres { get; }
        public byte[] Valid { get; }
        public string LastError { get; private set; }

        public MetricDepthCapture(Camera camera, int width, int height, float maxMeters)
        {
            if (!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RFloat)) throw new NotSupportedException("GPU does not support RFloat depth capture.");
            _maxMeters = maxMeters;
            var shader = Resources.Load<Shader>("PhysicalAiMetricDepth");
            if (shader == null) throw new InvalidOperationException("Metric depth shader is missing from the player.");
            _material = new Material(shader);
            _material.SetFloat("_PhysicalAiDepthMax", maxMeters);
            _target = new RenderTexture(width, height, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear) { name = "PhysicalAiMetricDepth" };
            _target.Create();
            _readback = new Texture2D(width, height, TextureFormat.RFloat, false, true);
            Preview = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Metres = new float[width * height]; Valid = new byte[Metres.Length];
            var host = new GameObject("PhysicalAiDepthPass");
            host.transform.SetParent(camera.transform, false);
            _volume = host.AddComponent<CustomPassVolume>();
            _volume.targetCamera = camera;
            _volume.injectionPoint = CustomPassInjectionPoint.BeforePostProcess;
            _pass = new DepthPass { name = "PhysicalAi metric depth", Camera = camera, Output = _target, Material = _material,
                targetColorBuffer = CustomPass.TargetBuffer.None, targetDepthBuffer = CustomPass.TargetBuffer.None };
            _volume.customPasses.Add(_pass);
            var hd = camera.GetComponent<HDAdditionalCameraData>() ?? camera.gameObject.AddComponent<HDAdditionalCameraData>();
            hd.customRenderingSettings = true;
            hd.renderingPathCustomFrameSettings.SetEnabled(FrameSettingsField.CustomPass, true);
            hd.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)FrameSettingsField.CustomPass] = true;
            hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.None;
            camera.allowDynamicResolution = false;
        }
        public void Prepare() { _pass.Executed = false; LastError = null; }
        public bool Read()
        {
            if (!_pass.Executed) { LastError = "HDRP did not execute the metric-depth pass."; return false; }
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = _target;
                _readback.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                _readback.Apply(false);
                var pixels = _readback.GetPixels();
                var preview = new Color32[pixels.Length];
                for (var y = 0; y < Height; y++)
                    for (var x = 0; x < Width; x++)
                    {
                        var source = y * Width + x;
                        var destination = (Height - 1 - y) * Width + x;
                        var value = pixels[source].r;
                        var valid = value > 0 && float.IsFinite(value) && value <= _maxMeters;
                        Metres[destination] = valid ? value : 0;
                        Valid[destination] = valid ? (byte)1 : (byte)0;
                        // Log visualization keeps sub-metre road detail visible on an RC-scale camera.
                        // This affects only the PNG/HUD, never the metric measurement above.
                        var fraction = valid ? Mathf.Log(1 + value / 0.02f) / Mathf.Log(1 + _maxMeters / 0.02f) : 1;
                        var shade = (byte)(255 * (1 - Mathf.Clamp01(fraction)));
                        preview[source] = valid ? new Color32(shade, shade, shade, 255) : new Color32(160, 0, 160, 255);
                    }
                Preview.SetPixels32(preview); Preview.Apply(false);
                return true;
            }
            finally { RenderTexture.active = previous; }
        }
        public void Dispose()
        {
            if (_volume != null) { _volume.customPasses.Clear(); UnityEngine.Object.Destroy(_volume.gameObject); }
            _target.Release();
            UnityEngine.Object.Destroy(_target); UnityEngine.Object.Destroy(_readback);
            UnityEngine.Object.Destroy(_material); UnityEngine.Object.Destroy(Preview);
        }
    }
}
