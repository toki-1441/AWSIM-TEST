// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using PhysicalAi.Course;
using UnityEngine;

namespace PhysicalAi.Planning
{
    /// <summary>Conservative local-look-ahead policy used as a deterministic fallback and smoke-test policy.</summary>
    public static class ReferencePlanningPolicy
    {
        /// <summary>
        /// 1.0 m with the runtime observation's five-sample (0.5 m) stride.
        /// The index is into the observation array, not raw 0.1 m course samples.
        /// A farther target looks through a tight turn and turns too late.
        /// </summary>
        public const int LookAheadWaypointIndex = 2;

        public static PlanningCommand Choose(RelativeCourseWaypoint[] waypoints, float cameraSafeSpeed, float trackHalfWidth, float maximumDesiredSpeed = float.PositiveInfinity)
        {
            if (waypoints == null || waypoints.Length == 0)
                return new PlanningCommand { DesiredSpeed = Mathf.Min(cameraSafeSpeed, maximumDesiredSpeed), DesiredLateralOffsetNormalized = 0f };

            var targetIndex = Mathf.Min(LookAheadWaypointIndex, waypoints.Length - 1);
            var target = waypoints[targetIndex];
            var safeSpeed = Mathf.Min(cameraSafeSpeed, maximumDesiredSpeed);
            for (var index = 0; index <= targetIndex; index++)
                safeSpeed = Mathf.Min(safeSpeed, waypoints[index].MaximumSafeSpeed);
            return new PlanningCommand
            {
                DesiredSpeed = safeSpeed,
                // PlanningLowLevelController already applies the target heading.
                // Feeding the full lane-normalised lateral displacement as well
                // double-counts a tight bend and drives the 1/10 car into the
                // inside duct. Retain a small centring correction for drift.
                DesiredLateralOffsetNormalized = Mathf.Clamp(
                    target.LocalPosition.x / Mathf.Max(0.01f, trackHalfWidth) * 0.35f,
                    -1f, 1f)
            };
        }
    }
}
