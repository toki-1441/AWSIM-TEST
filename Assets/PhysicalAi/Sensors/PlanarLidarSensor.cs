// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using UnityEngine;

namespace PhysicalAi.Sensors
{
    /// <summary>Optional 2D LiDAR observation for comparison and sensor-ablation experiments.</summary>
    public sealed class PlanarLidarSensor : MonoBehaviour
    {
        [SerializeField, Min(1)] int _rayCount = 181;
        [SerializeField, Range(1f, 360f)] float _fieldOfViewDegrees = 180f;
        [SerializeField, Min(0.01f)] float _maximumRange = 30f;
        [SerializeField] LayerMask _layerMask = ~0;

        public float[] ScanRanges()
        {
            var ranges = new float[_rayCount];
            var denominator = Mathf.Max(1, _rayCount - 1);
            for (var i = 0; i < _rayCount; i++)
            {
                var angle = Mathf.Lerp(-_fieldOfViewDegrees * 0.5f, _fieldOfViewDegrees * 0.5f, i / (float)denominator);
                var direction = Quaternion.AngleAxis(angle, transform.up) * transform.forward;
                ranges[i] = Physics.Raycast(transform.position, direction, out var hit, _maximumRange, _layerMask, QueryTriggerInteraction.Ignore)
                    ? hit.distance : _maximumRange;
            }
            return ranges;
        }
    }
}
