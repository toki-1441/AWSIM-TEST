// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;
using UnityEngine;

namespace PhysicalAi.Course
{
    public readonly struct RelativeCourseWaypoint
    {
        public readonly Vector3 LocalPosition;
        public readonly float Curvature;
        public readonly float MaximumSafeSpeed;

        public RelativeCourseWaypoint(Vector3 localPosition, float curvature, float maximumSafeSpeed)
        {
            LocalPosition = localPosition;
            Curvature = curvature;
            MaximumSafeSpeed = maximumSafeSpeed;
        }
    }

    /// <summary>Produces translation- and rotation-invariant planning features.</summary>
    public static class RelativeCourseObservation
    {
        public static RelativeCourseWaypoint[] Encode(GeneratedCourse course, Vector3 egoPosition, Quaternion egoRotation, int firstWaypointIndex, int count, int stride = 1)
        {
            if (course == null || count < 1 || stride < 1)
                return System.Array.Empty<RelativeCourseWaypoint>();

            var output = new RelativeCourseWaypoint[count];
            var inverseRotation = Quaternion.Inverse(egoRotation);
            var finalIndex = course.Centerline.Count - 1;
            for (var i = 0; i < count; i++)
            {
                var index = Mathf.Clamp(firstWaypointIndex + i * stride, 0, finalIndex);
                output[i] = new RelativeCourseWaypoint(
                    inverseRotation * (course.Centerline[index] - egoPosition),
                    course.Curvature[index],
                    course.MaximumSafeSpeed[index]);
            }
            return output;
        }
    }
}
