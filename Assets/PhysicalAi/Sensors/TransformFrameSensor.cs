// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using UnityEngine;

namespace PhysicalAi.Sensors
{
    /// <summary>Vehicle-to-sensor transform sample; the ROS2 bridge can publish this as the TF edge.</summary>
    public readonly struct TransformFrameSample
    {
        public readonly string ParentFrame;
        public readonly string ChildFrame;
        public readonly Vector3 LocalPosition;
        public readonly Quaternion LocalRotation;

        public TransformFrameSample(string parentFrame, string childFrame, Vector3 localPosition, Quaternion localRotation)
        {
            ParentFrame = parentFrame;
            ChildFrame = childFrame;
            LocalPosition = localPosition;
            LocalRotation = localRotation;
        }
    }

    public sealed class TransformFrameSensor : MonoBehaviour
    {
        [SerializeField] string _parentFrame = "base_link";
        [SerializeField] SensorMount _mount;

        public TransformFrameSample Sample()
        {
            var childFrame = _mount == null ? transform.name : _mount.FrameId;
            return new TransformFrameSample(_parentFrame, childFrame, transform.localPosition, transform.localRotation);
        }
    }
}
