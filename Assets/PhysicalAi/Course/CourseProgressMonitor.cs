// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using UnityEngine;

namespace PhysicalAi.Course
{
    /// <summary>Measures progress and lateral departure from a generated centerline.</summary>
    public sealed class CourseProgressMonitor : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] float _maximumDistanceFromCenterline = 2f;

        GeneratedCourse _course;
        public int NearestWaypointIndex { get; private set; }
        public float DistanceFromCenterline { get; private set; }
        public bool IsOffCourse => DistanceFromCenterline > _maximumDistanceFromCenterline;

        public void SetCourse(GeneratedCourse course)
        {
            _course = course;
            NearestWaypointIndex = 0;
            DistanceFromCenterline = 0f;
        }

        public void UpdatePosition(Vector3 position)
        {
            if (_course == null || _course.Centerline.Count == 0)
                return;
            var nearestDistanceSquared = float.PositiveInfinity;
            for (var index = 0; index < _course.Centerline.Count; index++)
            {
                var distanceSquared = (position - _course.Centerline[index]).sqrMagnitude;
                if (distanceSquared < nearestDistanceSquared)
                {
                    nearestDistanceSquared = distanceSquared;
                    NearestWaypointIndex = index;
                }
            }
            DistanceFromCenterline = Mathf.Sqrt(nearestDistanceSquared);
        }
    }
}
