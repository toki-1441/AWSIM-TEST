// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using UnityEngine;

namespace PhysicalAi.Planning
{
    /// <summary>
    /// Leaves an inspectable Player.log trail for every streamed-world operation.
    /// It deliberately checks the immediate state only: normal driving can change
    /// speed on the next simulation tick, whereas a geometry-management operation
    /// must not change it in the tick that performs the operation.
    /// </summary>
    public sealed class StreamingCourseIntegrityLogger : MonoBehaviour
    {
        [SerializeField] PhysicalAiEpisodeOrchestrator _orchestrator;
        [SerializeField, Min(0f)] float _positionTolerance = 0.0001f;
        [SerializeField, Min(0f)] float _rotationToleranceDegrees = 0.01f;
        [SerializeField, Min(0f)] float _velocityTolerance = 0.001f;
        [SerializeField, Min(0f)] float _angularVelocityTolerance = 0.001f;
        [SerializeField, Min(0f)] float _controlInputTolerance = 0.000001f;
        [SerializeField, Min(0f)] float _modelStateTolerance = 0.000001f;

        public int TransitionCount { get; private set; }
        public int DiscontinuityCount { get; private set; }

        void OnEnable()
        {
            if (_orchestrator != null)
                _orchestrator.StreamingTransitioned += OnStreamingTransition;
        }

        void OnDisable()
        {
            if (_orchestrator != null)
                _orchestrator.StreamingTransitioned -= OnStreamingTransition;
        }

        void OnStreamingTransition(StreamingCourseTransition transition)
        {
            TransitionCount++;
            var positionError = transition.UnexpectedPositionDelta.magnitude;
            var velocityError = transition.LinearVelocityDelta.magnitude;
            var angularVelocityError = transition.AngularVelocityDelta.magnitude;
            var controlInputError = transition.ControlInputDelta.magnitude;
            var discontinuous = positionError > _positionTolerance ||
                               transition.RotationDeltaDegrees > _rotationToleranceDegrees ||
                               velocityError > _velocityTolerance ||
                               angularVelocityError > _angularVelocityTolerance ||
                               controlInputError > _controlInputTolerance ||
                               transition.ActualSteeringAngleDelta > 0.000001f ||
                               transition.MaximumAxleForceDelta > _modelStateTolerance ||
                               transition.DynamicWeightDelta > _modelStateTolerance;
            if (discontinuous)
            {
                DiscontinuityCount++;
                Debug.LogError($"Physical AI streaming discontinuity ({transition.Kind}): " +
                               $"position error {positionError:F6} m, rotation {transition.RotationDeltaDegrees:F4}°, " +
                               $"velocity {velocityError:F6} m/s, angular velocity {angularVelocityError:F6} rad/s; " +
                               $"source {transition.CommandSource}, " +
                               $"control input {FormatVector(transition.ControlInputBefore)}->{FormatVector(transition.ControlInputAfter)}, " +
                               $"control delta {controlInputError:F6}, " +
                               $"model {_orchestrator.VehicleModelName}, actual steering {transition.ActualSteeringAngleBefore:F6}->{transition.ActualSteeringAngleAfter:F6} rad, " +
                               $"axle force delta {transition.MaximumAxleForceDelta:F6} N, " +
                               $"dynamic weight delta {transition.DynamicWeightDelta:F6}; " +
                               $"retained {transition.RetainedWaypointCount}, forward {transition.ForwardWaypointCount}, " +
                               $"chunks {transition.ChunkCount}, appended corner {CornerLabel(transition.AppendedCornerDirection)}.");
                return;
            }

            Debug.Log($"Physical AI streaming transition ({transition.Kind}) is continuous: " +
                      $"position error {positionError:F6} m, rotation {transition.RotationDeltaDegrees:F4}°, " +
                      $"velocity {velocityError:F6} m/s, angular velocity {angularVelocityError:F6} rad/s; " +
                      $"source {transition.CommandSource}, " +
                      $"control input {FormatVector(transition.ControlInputBefore)}->{FormatVector(transition.ControlInputAfter)}, " +
                      $"control delta {controlInputError:F6}, " +
                      $"model {_orchestrator.VehicleModelName}, actual steering {transition.ActualSteeringAngleBefore:F6}->{transition.ActualSteeringAngleAfter:F6} rad, " +
                      $"axle force delta {transition.MaximumAxleForceDelta:F6} N, " +
                      $"dynamic weight delta {transition.DynamicWeightDelta:F6}; " +
                      $"retained {transition.RetainedWaypointCount}, forward {transition.ForwardWaypointCount}, " +
                      $"chunks {transition.ChunkCount}, appended corner {CornerLabel(transition.AppendedCornerDirection)}.");
        }

        static string CornerLabel(int direction) => direction < 0 ? "right" : direction > 0 ? "left" : "none";
        static string FormatVector(Vector3 value) => $"({value.x:F6},{value.y:F6},{value.z:F6})";
    }
}
