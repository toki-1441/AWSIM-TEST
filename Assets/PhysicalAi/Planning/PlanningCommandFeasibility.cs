// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using PhysicalAi.Course;
using UnityEngine;

namespace PhysicalAi.Planning
{
    /// <summary>Projects a learned high-level command into the local physical and visibility envelope.</summary>
    public static class PlanningCommandFeasibility
    {
        public static PlanningCommand Project(PlanningCommand command, RelativeCourseWaypoint[] waypoints, float cameraSafeSpeed)
        {
            var maximumSpeed = Mathf.Max(0f, cameraSafeSpeed);
            if (waypoints != null)
            {
                var count = Mathf.Min(ReferencePlanningPolicy.LookAheadWaypointIndex + 1, waypoints.Length);
                for (var index = 0; index < count; index++)
                    maximumSpeed = Mathf.Min(maximumSpeed, Mathf.Max(0f, waypoints[index].MaximumSafeSpeed));
            }
            return new PlanningCommand
            {
                DesiredSpeed = Mathf.Clamp(command.DesiredSpeed, 0f, maximumSpeed),
                DesiredLateralOffsetNormalized = Mathf.Clamp(command.DesiredLateralOffsetNormalized, -1f, 1f)
            };
        }
    }
}
