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
                features[offset + 4] = i == 0 ? opponent.RelativeLongitudinalSpeed : 0f;
            }
            return new PlanningObservation(features);
        }
    }

    public static class PlanningReward
    {
        public static float Calculate(float progressMeters, bool collision, bool offCourse, float speed, float safeSpeed)
        {
            var reward = progressMeters;
            if (collision)
                reward -= 20f;
            if (offCourse)
                reward -= 10f;
            if (speed > safeSpeed + 0.1f)
                reward -= (speed - safeSpeed) * 2f;
            return reward;
        }
    }
}
