// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using UnityEngine;

namespace PhysicalAi.Sensors
{
    /// <summary>Transform sample consumed by the ROS2 TF bridge.</summary>
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
        // For the base-link edge this is the vehicle root and provides its dynamic
        // odometry pose.  Fixed sensor mounts leave it unset and use local pose.
        [SerializeField] Transform _poseSource;
        Vector3 _floatingOriginWorldShift;

        Vector3 DynamicPosition()
        {
            // Rigidbody.position is authoritative during FixedUpdate.  Unity can
            // defer mirroring a direct Rigidbody move to Transform.position until
            // the next simulation step, precisely when a floating-origin rebase
            // must publish a continuous odometry sample.
            var poseBody = _poseSource == null ? null : _poseSource.GetComponent<Rigidbody>();
            return poseBody == null ? _poseSource.position : poseBody.position;
        }

        /// <summary>
        /// Records a world-space floating-origin move.  The source transform moves
        /// with the local simulation world, while odometry must remain continuous.
        /// </summary>
        public void ApplyFloatingOriginOffset(Vector3 offset)
        {
            _floatingOriginWorldShift += offset;
        }

        /// <summary>Starts a new episode in a fresh odometry frame.</summary>
        public void ResetFloatingOrigin() => _floatingOriginWorldShift = Vector3.zero;

        public TransformFrameSample Sample()
        {
            var childFrame = _mount == null ? transform.name : _mount.FrameId;
            return _poseSource == null
                ? new TransformFrameSample(_parentFrame, childFrame, transform.localPosition, transform.localRotation)
                : new TransformFrameSample(_parentFrame, childFrame, DynamicPosition() - _floatingOriginWorldShift, _poseSource.rotation);
        }
    }
}
