// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using UnityEngine;

namespace PhysicalAi.Course
{
    /// <summary>Measures progress and lateral departure from a generated centerline.</summary>
    public sealed class CourseProgressMonitor : MonoBehaviour
    {
        const float MinimumCompletedLapFraction = 0.8f;
        [SerializeField, Min(0.01f)] float _maximumDistanceFromCenterline = 0.45f;
        [SerializeField, Min(0.01f)] float _completionDistance = 0.1f;

        GeneratedCourse _course;
        float[] _progressAtWaypoint;
        float _progressOffsetMeters;
        Vector3 _previousPosition;
        float _travelledDistanceMeters;
        bool _hasPreviousPosition;
        bool _completed;
        public int NearestWaypointIndex { get; private set; }
        public float DistanceFromCenterline { get; private set; }
        public float ProgressMeters { get; private set; }
        public float DeltaProgressMeters { get; private set; }
        public bool IsOffCourse => DistanceFromCenterline > _maximumDistanceFromCenterline;
        public bool IsCompleted => _completed;

        public void SetCourse(GeneratedCourse course)
        {
            _course = course;
            NearestWaypointIndex = 0;
            DistanceFromCenterline = 0f;
            ProgressMeters = 0f;
            DeltaProgressMeters = 0f;
            _progressOffsetMeters = 0f;
            _previousPosition = Vector3.zero;
            _travelledDistanceMeters = 0f;
            _hasPreviousPosition = false;
            _completed = false;
            if (course == null)
            {
                _progressAtWaypoint = null;
                return;
            }

            _progressAtWaypoint = new float[course.Centerline.Count];
            for (var i = 1; i < _progressAtWaypoint.Length; i++)
                _progressAtWaypoint[i] = _progressAtWaypoint[i - 1] + Vector3.Distance(course.Centerline[i - 1], course.Centerline[i]);
        }

        /// <summary>Replaces an append-only streaming course without converting the extension itself into artificial progress reward.</summary>
        public void ExtendCourse(GeneratedCourse course)
        {
            if (_course == null || course == null || course.Centerline.Count < _course.Centerline.Count)
            {
                SetCourse(course);
                return;
            }
            _course = course;
            var preservedProgress = ProgressMeters;
            _progressAtWaypoint = new float[course.Centerline.Count];
            for (var i = 1; i < _progressAtWaypoint.Length; i++)
                _progressAtWaypoint[i] = _progressAtWaypoint[i - 1] + Vector3.Distance(course.Centerline[i - 1], course.Centerline[i]);
            ProgressMeters = preservedProgress;
            DeltaProgressMeters = 0f;
        }

        public void TrimCourse(GeneratedCourse course, int removedWaypointCount)
        {
            if (_course == null || course == null || _progressAtWaypoint == null || removedWaypointCount <= 0 || removedWaypointCount >= _progressAtWaypoint.Length)
            {
                SetCourse(course);
                return;
            }
            _progressOffsetMeters += _progressAtWaypoint[removedWaypointCount];
            _course = course;
            _progressAtWaypoint = BuildProgress(course);
            NearestWaypointIndex = Mathf.Max(0, NearestWaypointIndex - removedWaypointCount);
            ProgressMeters = _progressOffsetMeters + _progressAtWaypoint[Mathf.Clamp(NearestWaypointIndex, 0, _progressAtWaypoint.Length - 1)];
            DeltaProgressMeters = 0f;
        }

        public void UpdatePosition(Vector3 position)
        {
            if (_course == null || _course.Centerline.Count == 0)
                return;
            if (_hasPreviousPosition)
                _travelledDistanceMeters += Vector3.Distance(position, _previousPosition);
            _previousPosition = position;
            _hasPreviousPosition = true;
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
            ProgressMeters = _progressAtWaypoint == null ? 0f : _progressOffsetMeters + _progressAtWaypoint[NearestWaypointIndex];
            DeltaProgressMeters = ProgressMeters - previousProgress;
            UpdateCompletionState(position);
        }

        void UpdateCompletionState(Vector3 position)
        {
            var count = _course.Centerline.Count;
            if (count == 0 || DistanceFromCenterline > _completionDistance)
                return;

            if (!_course.IsClosedLoop)
            {
                _completed = NearestWaypointIndex == count - 1;
                return;
            }

            // A closed centreline can repeat point zero at its final index, and tiny
            // floating-point closure errors can instead make its final approach the
            // nearest point at the episode start. Neither case is proof of a lap.
            // Completion consequently requires both meaningful travelled distance and
            // a return to the geometric start/finish line.
            var lapLength = _progressAtWaypoint == null ? 0f : _progressAtWaypoint[count - 1];
            var atFinishLine = Vector3.Distance(position, _course.Centerline[0]) <= _completionDistance;
            if (lapLength > 0f && _travelledDistanceMeters >= lapLength * MinimumCompletedLapFraction && atFinishLine)
                _completed = true;
        }

        static float[] BuildProgress(GeneratedCourse course)
        {
            var progress = new float[course.Centerline.Count];
            for (var index = 1; index < progress.Length; index++)
                progress[index] = progress[index - 1] + Vector3.Distance(course.Centerline[index - 1], course.Centerline[index]);
            return progress;
        }
    }
}
