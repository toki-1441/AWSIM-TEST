// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using System;
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

        public void Configure(SensorModality modality, string frameId, Vector3 localPosition, Quaternion localRotation)
        {
            _modality = modality;
            _frameId = frameId;
            transform.localPosition = localPosition;
            transform.localRotation = localRotation;
        }
    }
}
