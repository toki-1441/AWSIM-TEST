// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using PhysicalAi.Course;
using PhysicalAi.VehicleDynamics;
using UnityEngine;

namespace PhysicalAi.Racing
{
    /// <summary>Kinematic opponent that follows the generated centerline while retaining collider interaction with ego.</summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class OpponentCourseFollower : MonoBehaviour
    {
        [SerializeField, Min(0f)] float _targetSpeed = 3f;
        [SerializeField] float _lateralOffset;
        [SerializeField, Min(0f)] float _lateralOscillationAmplitude;
        [SerializeField, Min(0f)] float _lateralOscillationFrequencyHz;
        [SerializeField, Min(0f)] float _initialProgressMeters = 10f;

        GeneratedCourse _course;
        Rigidbody _body;
        int _segment;
        float _distanceOnSegment;
        float _progress;
        float _elapsedSeconds;
        public Vector3 Velocity { get; private set; }
        /// <summary>Distance travelled along the course since the scenario was reset.</summary>
        public float ProgressMeters => _progress;
        public float TargetSpeed => _targetSpeed;
        public float InitialProgressMeters => _initialProgressMeters;
        public float LateralOffset => _lateralOffset;
        public float LateralOscillationAmplitude => _lateralOscillationAmplitude;
        public float LateralOscillationFrequencyHz => _lateralOscillationFrequencyHz;

        /// <summary>Applies an evaluation-only opponent setup before its next course reset.</summary>
        public void Configure(float? targetSpeed = null, float? initialProgressMeters = null, float? lateralOffset = null,
            float? lateralOscillationAmplitude = null, float? lateralOscillationFrequencyHz = null)
        {
            if (targetSpeed.HasValue)
                _targetSpeed = Mathf.Max(0f, targetSpeed.Value);
            if (initialProgressMeters.HasValue)
                _initialProgressMeters = Mathf.Max(0f, initialProgressMeters.Value);
            if (lateralOffset.HasValue)
                _lateralOffset = lateralOffset.Value;
            if (lateralOscillationAmplitude.HasValue)
                _lateralOscillationAmplitude = Mathf.Max(0f, lateralOscillationAmplitude.Value);
            if (lateralOscillationFrequencyHz.HasValue)
                _lateralOscillationFrequencyHz = Mathf.Max(0f, lateralOscillationFrequencyHz.Value);
        }

        void Awake()
        {
            EnsureBody();
        }

        public void SetCourse(GeneratedCourse course)
        {
            EnsureBody();
            _course = course;
            _segment = 0;
            _distanceOnSegment = 0f;
            _progress = 0f;
            _elapsedSeconds = 0f;
            Velocity = Vector3.zero;
            Advance(_initialProgressMeters);
            PlaceOnCourse();
        }

        /// <summary>Replaces an append-only course view without teleporting or resetting race progress.</summary>
        public void ExtendCourse(GeneratedCourse course)
        {
            if (course == null || course.Centerline.Count < 2)
                return;
            _course = course;
            _segment = Mathf.Clamp(_segment, 0, course.Centerline.Count - 2);
        }

        public void RebaseCourse(int removedWaypointCount) => _segment = Mathf.Max(0, _segment - removedWaypointCount);

        void FixedUpdate()
        {
            EnsureBody();
            if (_course == null || _course.Centerline.Count < 2 || _segment >= _course.Centerline.Count - 1)
                return;
            var previousPosition = _body.position;
            Advance(_targetSpeed * Time.fixedDeltaTime);
            _elapsedSeconds += Time.fixedDeltaTime;
            PlaceOnCourse();
            Velocity = (_body.position - previousPosition) / Time.fixedDeltaTime;
        }

        void Advance(float distance)
        {
            while (distance > 0f && _course != null)
            {
                if (_segment >= _course.Centerline.Count - 1)
                {
                    // Fixed holdout circuits are closed. Keep the kinematic opponent
                    // circulating so a long evaluation continues to contain genuine
                    // head-to-head interaction instead of becoming solo driving.
                    if (!_course.IsClosedLoop)
                        return;
                    _segment = 0;
                    _distanceOnSegment = 0f;
                }
                var segmentLength = Vector3.Distance(_course.Centerline[_segment], _course.Centerline[_segment + 1]);
                var remaining = Mathf.Max(0f, segmentLength - _distanceOnSegment);
                var step = Mathf.Min(distance, remaining);
                _distanceOnSegment += step;
                _progress += step;
                distance -= step;
                if (_distanceOnSegment >= segmentLength - 0.0001f)
                {
                    _segment++;
                    _distanceOnSegment = 0f;
                }
            }
        }

        void PlaceOnCourse()
        {
            if (_course == null || _segment >= _course.Centerline.Count - 1)
                return;
            var start = _course.Centerline[_segment];
            var end = _course.Centerline[_segment + 1];
            var direction = (end - start).normalized;
            var length = Vector3.Distance(start, end);
            var position = Vector3.Lerp(start, end, length <= 0.0001f ? 0f : _distanceOnSegment / length);
            var lateralOffset = LateralOffsetAtTime(_lateralOffset, _lateralOscillationAmplitude,
                _lateralOscillationFrequencyHz, _elapsedSeconds);
            var lateral = Vector3.Cross(Vector3.up, direction) * lateralOffset;
            _body.MovePosition(position + lateral + Vector3.up * VehicleSpecification.Default.BodyCenterHeight);
            _body.MoveRotation(Quaternion.LookRotation(direction, Vector3.up));
        }

        public static float LateralOffsetAtTime(float centerOffset, float amplitude, float frequencyHz, float elapsedSeconds) =>
            centerOffset + Mathf.Max(0f, amplitude) * Mathf.Sin(2f * Mathf.PI * Mathf.Max(0f, frequencyHz) * Mathf.Max(0f, elapsedSeconds));

        void EnsureBody()
        {
            if (_body == null)
                _body = GetComponent<Rigidbody>();
            if (_body != null)
                _body.isKinematic = true;
        }
    }
}
