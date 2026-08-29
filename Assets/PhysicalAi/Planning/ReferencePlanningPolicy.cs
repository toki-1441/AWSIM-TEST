// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using PhysicalAi.Course;
using UnityEngine;

namespace PhysicalAi.Planning
{
    /// <summary>Conservative local-look-ahead policy used as a deterministic fallback and smoke-test policy.</summary>
    public static class ReferencePlanningPolicy
    {
        public static PlanningCommand Choose(RelativeCourseWaypoint[] waypoints, float cameraSafeSpeed, float trackHalfWidth)
        {
            if (waypoints == null || waypoints.Length == 0)
                return new PlanningCommand { DesiredSpeed = cameraSafeSpeed, DesiredLateralOffsetNormalized = 0f };

            var target = waypoints[Mathf.Min(1, waypoints.Length - 1)];
            return new PlanningCommand
            {
                DesiredSpeed = Mathf.Min(cameraSafeSpeed, target.MaximumSafeSpeed),
                DesiredLateralOffsetNormalized = Mathf.Clamp(target.LocalPosition.x / Mathf.Max(0.01f, trackHalfWidth), -1f, 1f)
            };
        }
    }
}
