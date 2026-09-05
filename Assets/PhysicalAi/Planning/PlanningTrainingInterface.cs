// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using System;
using PhysicalAi.Course;
using PhysicalAi.Racing;
using PhysicalAi.Safety;
using UnityEngine;

namespace PhysicalAi.Planning
{
    /// <summary>Framework-neutral planning observation for BC, RL, and ROS2 replay.</summary>
    public readonly struct PlanningObservation
    {
        public readonly float[] Features;

        public PlanningObservation(float[] features) => Features = features;
    }

    /// <summary>Planning output; a classical controller owns throttle, brake, and steering actuation.</summary>
    [Serializable]
    public struct PlanningCommand
    {
        [Min(0f)] public float DesiredSpeed;
        [Range(-1f, 1f)] public float DesiredLateralOffsetNormalized;
    }

    public static class PlanningObservationEncoder
    {
        public static PlanningObservation Encode(
            RelativeCourseWaypoint[] waypoints,
            OpponentKinematics opponent,
            float currentSpeed,
            float visibleDistance,
            float reactionTime,
            float maximumDeceleration)
        {
            var waypointCount = waypoints?.Length ?? 0;
            var features = new float[4 + waypointCount * 5];
            features[0] = currentSpeed;
            features[1] = BlindCornerSafety.MaximumSafeSpeed(visibleDistance, reactionTime, maximumDeceleration);
            features[2] = opponent.LongitudinalDistance;
            features[3] = opponent.LateralOffset;
            for (var i = 0; i < waypointCount; i++)
            {
                var offset = 4 + i * 5;
                features[offset] = waypoints[i].LocalPosition.x;
                features[offset + 1] = waypoints[i].LocalPosition.z;
                features[offset + 2] = waypoints[i].Curvature;
                features[offset + 3] = waypoints[i].MaximumSafeSpeed;
                // Preserve the established five-values-per-waypoint layout. The
                // first two auxiliary slots carry opponent motion, allowing a
                // policy to distinguish closing speed from a left/right move
                // without invalidating existing dataset widths or checkpoints.
                features[offset + 4] = i == 0
                    ? opponent.RelativeLongitudinalSpeed
                    : i == 1 ? opponent.RelativeLateralSpeed : 0f;
            }
            return new PlanningObservation(features);
        }
    }

    public static class PlanningReward
    {
        public static float Calculate(float progressMeters, bool collision, bool offCourse, float speed, float safeSpeed,
            int egoOvertakeCount = 0, int opponentOvertakeCount = 0)
        {
            var reward = progressMeters;
            if (collision)
                reward -= 20f;
            if (offCourse)
                reward -= 10f;
            if (speed > safeSpeed + 0.1f)
                reward -= (speed - safeSpeed) * 2f;
            // Progress already rewards speed, but it cannot distinguish a safe
            // pass from following the slower opponent indefinitely. Emit these
            // event rewards once, when the arc-length ordering actually changes.
            reward += Mathf.Max(0, egoOvertakeCount) * 5f;
            reward -= Mathf.Max(0, opponentOvertakeCount) * 3f;
            return reward;
        }

        /// <summary>Terminal reward delivered exactly once at an episode boundary.</summary>
        public static float CalculateTerminal(bool completed, bool collision, bool offCourse, bool timedOut, float speed, float safeSpeed)
        {
            var reward = Calculate(0f, collision, offCourse, speed, safeSpeed);
            if (completed)
                reward += 10f;
            // A zero-action policy must not prefer timing out to making safe progress.
            if (timedOut)
                reward -= 5f;
            return reward;
        }
    }
}
