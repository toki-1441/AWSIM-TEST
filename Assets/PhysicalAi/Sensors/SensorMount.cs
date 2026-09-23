// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Globalization;
using PhysicalAi.Configuration;
using UnityEngine;

namespace PhysicalAi.Sensors
{
    /// <summary>Sensor modalities which can share a vehicle-fixed mount.</summary>
    public enum SensorModality { Camera, PlanarLidar, SolidStateLidar, TransformFrame }

    /// <summary>Stable sensor-frame identity and mounting pose, independent of the sensing implementation.</summary>
    public sealed class SensorMount : MonoBehaviour
    {
        [SerializeField] SensorModality _modality;
        [SerializeField] string _frameId = "sensor";

        public SensorModality Modality => _modality;
        public string FrameId => _frameId;

        void Awake() => ApplyLaunchConfiguration();

        /// <summary>Fixed camera extrinsics. Height is above the planar course; x/z are vehicle-local offsets.</summary>
        public void ApplyLaunchConfiguration()
        {
            if (_modality != SensorModality.Camera || transform.parent == null) return;
            float Read(string name, float fallback)
            {
                var text = RuntimeArguments.GetValue(name);
                if (text == null) return fallback;
                if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !float.IsFinite(value))
                    throw new ArgumentException("Invalid camera mount value: " + name);
                return value;
            }
            var position = transform.localPosition;
            var angles = transform.localEulerAngles;
            var height = Read("-physicalAiCameraHeightMeters", position.y + transform.parent.position.y);
            if (height <= 0) throw new ArgumentException("Camera height above the planar course must be positive.");
            position.y = height - transform.parent.position.y;
            position.x = Read("-physicalAiCameraLateralMeters", position.x);
            position.z = Read("-physicalAiCameraForwardMeters", position.z);
            angles.x = Read("-physicalAiCameraPitchDegrees", angles.x);
            angles.y = Read("-physicalAiCameraYawDegrees", angles.y);
            angles.z = Read("-physicalAiCameraRollDegrees", angles.z);
            transform.localPosition = position;
            transform.localRotation = Quaternion.Euler(angles);
        }

        public void Configure(SensorModality modality, string frameId, Vector3 localPosition, Quaternion localRotation)
        {
            _modality = modality;
            _frameId = frameId;
            transform.localPosition = localPosition;
            transform.localRotation = localRotation;
        }
    }
}
