// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using UnityEngine;
using UnityEngine.UI;

namespace PhysicalAi.Sensors
{
    /// <summary>Keeps a small on-screen view of the exact camera image used by perception.</summary>
    public sealed class SensorCameraInset : MonoBehaviour
    {
        [SerializeField] CameraObservationSensor _sensor;
        [SerializeField] RawImage _image;

        void LateUpdate()
        {
            if (_sensor == null || _image == null)
                return;
            var frame = _sensor.LatestImage;
            if (frame != null && _image.texture != frame)
                _image.texture = frame;
        }
    }
}
