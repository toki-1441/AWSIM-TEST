// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using UnityEngine;

namespace PhysicalAi.Course
{
    /// <summary>Measures progress and lateral departure from a generated centerline.</summary>
    public sealed class CourseProgressMonitor : MonoBehaviour
    {
        const float MinimumCompletionTravelFraction = 0.8f;
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
                _travelledDistanceMeters += PlanarDistance(position, _previousPosition);
            _previousPosition = position;
            _hasPreviousPosition = true;
            var nearestDistanceSquared = float.PositiveInfinity;
            for (var index = 0; index < _course.Centerline.Count; index++)
            {
                var distanceSquared = PlanarDistanceSquared(position, _course.Centerline[index]);
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
            if (count == 0)
                return;

            // A closed centreline can repeat point zero at its final index, while an
            // open generator may place its endpoint close to its start. A nearest
            // waypoint alone is therefore not proof of a completed route. Every
            // course requires meaningful travelled distance and geometric arrival at
            // its real finish before an episode can claim success.
            var courseLength = _progressAtWaypoint == null ? 0f : _progressAtWaypoint[count - 1];
            // A closed loop needs a precise start-line arrival; an open course
            // instead has a finish line at its final sample. Requiring a moving
            // vehicle to stop within 10 cm of that endpoint turns a valid finish
            // crossing into an off-course result.
            var atFinish = _course.IsClosedLoop
                ? PlanarDistance(position, _course.Centerline[0]) <= _completionDistance
                : NearestWaypointIndex == count - 1 && DistanceFromCenterline <= _maximumDistanceFromCenterline;
            if (courseLength > 0f && _travelledDistanceMeters >= courseLength * MinimumCompletionTravelFraction && atFinish)
                _completed = true;
        }

        // The generated racing course is planar. Vehicle suspension and contact
        // response legitimately change Y, none of which is lateral departure.
        static float PlanarDistance(Vector3 first, Vector3 second) => Mathf.Sqrt(PlanarDistanceSquared(first, second));

        static float PlanarDistanceSquared(Vector3 first, Vector3 second)
        {
            var delta = first - second;
            return delta.x * delta.x + delta.z * delta.z;
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
