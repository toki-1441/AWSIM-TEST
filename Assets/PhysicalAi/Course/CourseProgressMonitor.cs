// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using UnityEngine;

namespace PhysicalAi.Course
{
    /// <summary>Measures progress and lateral departure from a generated centerline.</summary>
    public sealed class CourseProgressMonitor : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] float _maximumDistanceFromCenterline = 2f;
        [SerializeField, Min(0.01f)] float _completionDistance = 1f;

        GeneratedCourse _course;
        float[] _progressAtWaypoint;
        public int NearestWaypointIndex { get; private set; }
        public float DistanceFromCenterline { get; private set; }
        public float ProgressMeters { get; private set; }
        public float DeltaProgressMeters { get; private set; }
        public bool IsOffCourse => DistanceFromCenterline > _maximumDistanceFromCenterline;
        public bool IsCompleted => _course != null && _course.Centerline.Count > 0 &&
                                   NearestWaypointIndex == _course.Centerline.Count - 1 && DistanceFromCenterline <= _completionDistance;

        public void SetCourse(GeneratedCourse course)
        {
            _course = course;
            NearestWaypointIndex = 0;
            DistanceFromCenterline = 0f;
            ProgressMeters = 0f;
            DeltaProgressMeters = 0f;
            if (course == null)
            {
                _progressAtWaypoint = null;
                return;
            }

            _progressAtWaypoint = new float[course.Centerline.Count];
            for (var i = 1; i < _progressAtWaypoint.Length; i++)
                _progressAtWaypoint[i] = _progressAtWaypoint[i - 1] + Vector3.Distance(course.Centerline[i - 1], course.Centerline[i]);
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
            var previousProgress = ProgressMeters;
            ProgressMeters = _progressAtWaypoint == null ? 0f : _progressAtWaypoint[NearestWaypointIndex];
            DeltaProgressMeters = ProgressMeters - previousProgress;
        }
    }
}
