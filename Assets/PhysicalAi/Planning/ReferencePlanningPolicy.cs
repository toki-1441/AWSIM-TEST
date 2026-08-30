// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using PhysicalAi.Course;
using UnityEngine;

namespace PhysicalAi.Planning
{
    /// <summary>Conservative local-look-ahead policy used as a deterministic fallback and smoke-test policy.</summary>
    public static class ReferencePlanningPolicy
    {
        /// <summary>0.4 m with the generated course's 0.1 m sample spacing.</summary>
        public const int LookAheadWaypointIndex = 4;

        public static PlanningCommand Choose(RelativeCourseWaypoint[] waypoints, float cameraSafeSpeed, float trackHalfWidth)
        {
            if (waypoints == null || waypoints.Length == 0)
                return new PlanningCommand { DesiredSpeed = cameraSafeSpeed, DesiredLateralOffsetNormalized = 0f };

            var targetIndex = Mathf.Min(LookAheadWaypointIndex, waypoints.Length - 1);
            var target = waypoints[targetIndex];
            var safeSpeed = cameraSafeSpeed;
            for (var index = 0; index <= targetIndex; index++)
                safeSpeed = Mathf.Min(safeSpeed, waypoints[index].MaximumSafeSpeed);
            return new PlanningCommand
            {
                DesiredSpeed = safeSpeed,
                DesiredLateralOffsetNormalized = Mathf.Clamp(target.LocalPosition.x / Mathf.Max(0.01f, trackHalfWidth), -1f, 1f)
            };
        }
    }
}
