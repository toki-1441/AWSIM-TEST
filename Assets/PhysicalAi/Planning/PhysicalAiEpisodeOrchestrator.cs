// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using System;
using PhysicalAi.Course;
using PhysicalAi.Racing;
using PhysicalAi.Randomization;
using PhysicalAi.Safety;
using PhysicalAi.VehicleDynamics;
using UnityEngine;

namespace PhysicalAi.Planning
{
    /// <summary>
    /// Runtime episode boundary joining generated courses, camera risk, hazards,
    /// planning policies, classical actuation, and optional BC recording.
    /// Attach this to an episode root and subscribe an ML/Python or ROS2 bridge
    /// to <see cref="ObservationReady"/> and <see cref="CommandProvider"/>.
    /// </summary>
    public sealed class PhysicalAiEpisodeOrchestrator : MonoBehaviour
    {
        [SerializeField] CombinedSlipVehicle _vehicle;
        [SerializeField] PlanningLowLevelController _lowLevelController;
        [SerializeField] PlanningDemonstrationRecorder _demonstrationRecorder;
        [SerializeField] BlindCornerHazardSpawner _hazardSpawner;
        [SerializeField] DomainRandomizer _domainRandomizer;
        [SerializeField] ProceduralCourseTrackBuilder _trackBuilder;
        [SerializeField] Camera _egoCamera;
        [SerializeField] CourseGenerationSettings _courseSettings = default;
        [SerializeField] int _episodeSeed = 1;
        [SerializeField, Min(1)] int _observationWaypointCount = 6;
        [SerializeField, Min(0.01f)] float _reactionTime = 0.5f;
        [SerializeField, Min(0.01f)] float _maximumDeceleration = 6f;
        [SerializeField, Min(1f)] float _maximumVisibleDistance = 40f;

        GeneratedCourse _course;
        Rigidbody _rigidbody;

        public event Action<GeneratedCourse> CourseGenerated;
        public event Action<PlanningObservation> ObservationReady;
        public Func<PlanningObservation, PlanningCommand> CommandProvider;
        public GeneratedCourse Course => _course;

        void Awake()
        {
            if (_courseSettings.SampleSpacing <= 0f)
                _courseSettings = CourseGenerationSettings.Default;
            _rigidbody = _vehicle != null ? _vehicle.GetComponent<Rigidbody>() : null;
        }

        void Start() => ResetEpisode(_episodeSeed);

        public void ResetEpisode(int seed)
        {
            _episodeSeed = seed;
            var courseOrigin = new Vector3(transform.position.x, 0f, transform.position.z);
            _course = PhysicsConstrainedCourseGenerator.Generate(seed, _courseSettings, courseOrigin, transform.eulerAngles.y);
            _domainRandomizer?.ApplyEpisodeSeed(seed);
            _trackBuilder?.Build(_course);
            _hazardSpawner?.SpawnEpisodeHazard(seed);
            _demonstrationRecorder?.BeginEpisode(seed);
            CourseGenerated?.Invoke(_course);
        }

        void FixedUpdate()
        {
            if (_course == null || _vehicle == null || _lowLevelController == null || _rigidbody == null)
                return;

            var waypointIndex = FindNearestWaypoint(_rigidbody.position);
            var waypoints = RelativeCourseObservation.Encode(_course, _rigidbody.position, _rigidbody.rotation, waypointIndex, _observationWaypointCount);
            var visibleDistance = MeasureVisibleDistance();
            var observation = PlanningObservationEncoder.Encode(
                waypoints,
                default,
                _rigidbody.linearVelocity.magnitude,
                visibleDistance,
                _reactionTime,
                _maximumDeceleration);
            ObservationReady?.Invoke(observation);

            var command = CommandProvider != null
                ? CommandProvider(observation)
                : SafeFallbackCommand(waypoints, observation.Features[1]);
            var headingError = waypoints.Length == 0 ? 0f : Mathf.Atan2(waypoints[0].LocalPosition.x, Mathf.Max(0.01f, waypoints[0].LocalPosition.z));
            _lowLevelController.Apply(command, _rigidbody.linearVelocity.magnitude, headingError);
            _demonstrationRecorder?.Record(observation, command, 0f);
        }

        public void EndEpisode(bool collision, bool offCourse)
        {
            var speed = _rigidbody == null ? 0f : _rigidbody.linearVelocity.magnitude;
            var safeSpeed = _course == null || _rigidbody == null
                ? 0f
                : _course.MaximumSafeSpeed[Mathf.Clamp(FindNearestWaypoint(_rigidbody.position), 0, _course.MaximumSafeSpeed.Count - 1)];
            var reward = PlanningReward.Calculate(0f, collision, offCourse, speed, safeSpeed);
            _demonstrationRecorder?.Record(new PlanningObservation(Array.Empty<float>()), default, reward);
            _demonstrationRecorder?.EndEpisode();
        }

        int FindNearestWaypoint(Vector3 position)
        {
            var nearest = 0;
            var nearestDistance = float.PositiveInfinity;
            for (var i = 0; i < _course.Centerline.Count; i++)
            {
                var distance = (position - _course.Centerline[i]).sqrMagnitude;
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = i;
                }
            }
            return nearest;
        }

        float MeasureVisibleDistance()
        {
            if (_egoCamera == null)
                return _maximumVisibleDistance;
            return Physics.Raycast(_egoCamera.transform.position, _egoCamera.transform.forward, out var hit, _maximumVisibleDistance, ~0, QueryTriggerInteraction.Ignore)
                ? hit.distance
                : _maximumVisibleDistance;
        }

        static PlanningCommand SafeFallbackCommand(RelativeCourseWaypoint[] waypoints, float cameraSafeSpeed)
        {
            var courseSafeSpeed = waypoints.Length == 0 ? cameraSafeSpeed : waypoints[0].MaximumSafeSpeed;
            return new PlanningCommand { DesiredSpeed = Mathf.Min(cameraSafeSpeed, courseSafeSpeed), DesiredLateralOffsetNormalized = 0f };
        }
    }
}
