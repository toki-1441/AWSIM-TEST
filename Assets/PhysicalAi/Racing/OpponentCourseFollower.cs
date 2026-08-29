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
        [SerializeField, Min(0f)] float _initialProgressMeters = 10f;

        GeneratedCourse _course;
        Rigidbody _body;
        int _segment;
        float _distanceOnSegment;
        float _progress;
        public Vector3 Velocity { get; private set; }

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
            Velocity = Vector3.zero;
            Advance(_initialProgressMeters);
            PlaceOnCourse();
        }

        public void RebaseCourse(int removedWaypointCount) => _segment = Mathf.Max(0, _segment - removedWaypointCount);

        void FixedUpdate()
        {
            EnsureBody();
            if (_course == null || _course.Centerline.Count < 2 || _segment >= _course.Centerline.Count - 1)
                return;
            var previousPosition = _body.position;
            Advance(_targetSpeed * Time.fixedDeltaTime);
            PlaceOnCourse();
            Velocity = (_body.position - previousPosition) / Time.fixedDeltaTime;
        }

        void Advance(float distance)
        {
            while (distance > 0f && _course != null && _segment < _course.Centerline.Count - 1)
            {
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
            var lateral = Vector3.Cross(Vector3.up, direction) * _lateralOffset;
            _body.MovePosition(position + lateral + Vector3.up * VehicleSpecification.Default.BodyCenterHeight);
            _body.MoveRotation(Quaternion.LookRotation(direction, Vector3.up));
        }

        void EnsureBody()
        {
            if (_body == null)
                _body = GetComponent<Rigidbody>();
            if (_body != null)
                _body.isKinematic = true;
        }
    }
}
