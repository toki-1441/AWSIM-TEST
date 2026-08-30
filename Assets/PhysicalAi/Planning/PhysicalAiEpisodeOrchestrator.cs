// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using System;
using PhysicalAi.Course;
using PhysicalAi.Calibration;
using PhysicalAi.Racing;
using PhysicalAi.Randomization;
using PhysicalAi.Safety;
using PhysicalAi.VehicleDynamics;
using PhysicalAi.Sensors;
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
        [SerializeField] BehaviorCloningPolicy _behaviorCloningPolicy;
        [SerializeField] BlindCornerHazardSpawner _hazardSpawner;
        [SerializeField] DomainRandomizer _domainRandomizer;
        [SerializeField] TelemetryCalibrationApplier _telemetryCalibrationApplier;
        [SerializeField] ProceduralCourseTrackBuilder _trackBuilder;
        [SerializeField] CourseProgressMonitor _progressMonitor;
        [SerializeField] HeadToHeadScenario _headToHeadScenario;
        [SerializeField] Camera _egoCamera;
        [SerializeField] CameraObservationSensor _cameraObservationSensor;
        [SerializeField] bool _useStreamingTraining;
        [SerializeField, Min(2)] int _streamingExtensionWaypointMargin = 40;
        [SerializeField, Min(0)] int _streamingRetainedWaypointsBehind = 80;
        [SerializeField, Min(16)] int _maximumStreamingWaypointHistory = 400;
        [SerializeField, Min(1f)] float _floatingOriginThreshold = 50f;
        [SerializeField] CourseGenerationSettings _courseSettings = default;
        [SerializeField] int _episodeSeed = 1;
        [SerializeField, Min(1)] int _observationWaypointCount = 6;
        [SerializeField, Min(0.01f)] float _trackHalfWidth = 0.5f;
        [SerializeField, Min(0.01f)] float _reactionTime = 0.5f;
        [SerializeField, Min(0.01f)] float _maximumDeceleration = 6f;
        [SerializeField, Min(0.1f)] float _maximumVisibleDistance = 6f;

        GeneratedCourse _course;
        StreamingCourseState _streamingCourse;
        Rigidbody _rigidbody;
        Vector3 _episodeStartPosition;
        Quaternion _episodeStartRotation;
        bool _episodeEnded;
        bool _visibilitySafetyDisabled;
        public bool LastCollisionWasBlindCornerHazard { get; private set; }
        public bool LastCollisionWasOpponent { get; private set; }

        public event Action<GeneratedCourse> CourseGenerated;
        public event Action<GeneratedCourse> CourseExtended;
        public event Action<PlanningObservation> ObservationReady;
        public event Action<CameraObservationFrame> CameraObservationReady;
        public event Action<float> StepRewardReady;
        public event Action<bool, bool, bool> EpisodeFinished;
        public Func<PlanningObservation, PlanningCommand> CommandProvider;
        public GeneratedCourse Course => _course;
        public int EpisodeSeed => _episodeSeed;

        void Awake()
        {
            if (_courseSettings.SampleSpacing <= 0f)
                _courseSettings = CourseGenerationSettings.Default;
            _rigidbody = _vehicle != null ? _vehicle.GetComponent<Rigidbody>() : null;
            _episodeStartPosition = transform.position;
            _episodeStartRotation = transform.rotation;
            _visibilitySafetyDisabled = HasCommandLineArgument("-physicalAiDisableVisibilitySafety");
        }

        void Start() => ResetEpisode(_episodeSeed);

        public void ResetEpisode(int seed)
        {
            ResetEpisode(seed, false);
        }

        public void ResetHoldoutEpisode(int seed)
        {
            ResetEpisode(seed, true);
        }

        /// <summary>Uses the training generator as a finite course for comparable evaluation.</summary>
        public void ResetFiniteTrainingEpisode(int seed)
        {
            var wasStreaming = _useStreamingTraining;
            _useStreamingTraining = false;
            ResetEpisode(seed, false);
            _useStreamingTraining = wasStreaming;
        }

        void ResetEpisode(int seed, bool useHoldoutCourse)
        {
            _episodeSeed = seed;
            ResetVehicleState();
            // ResetVehicleState may be called from an episode-finished callback in
            // the same physics tick. At that point Transform can still expose the
            // previous episode's off-course pose even though the Rigidbody has been
            // reset. Course generation must use the authoritative reset position.
            var courseOrigin = new Vector3(_episodeStartPosition.x, 0f, _episodeStartPosition.z);
            if (_useStreamingTraining && !useHoldoutCourse)
            {
                _streamingCourse = new StreamingCourseState();
                _streamingCourse.Reset(seed, _courseSettings, courseOrigin, _episodeStartRotation.eulerAngles.y);
                _course = _streamingCourse.Course;
            }
            else
            {
                _streamingCourse = null;
                _course = useHoldoutCourse
                    ? ClosedHoldoutCourseGenerator.Generate(seed, _courseSettings, courseOrigin, _episodeStartRotation.eulerAngles.y)
                    : PhysicsConstrainedCourseGenerator.Generate(seed, _courseSettings, courseOrigin, _episodeStartRotation.eulerAngles.y);
            }
            _domainRandomizer?.ApplyEpisodeSeed(seed);
            _telemetryCalibrationApplier?.ApplyCalibration();
            _trackBuilder?.Build(_course);
            _progressMonitor?.SetCourse(_course);
            _headToHeadScenario?.SetCourse(_course);
            _episodeEnded = false;
            LastCollisionWasBlindCornerHazard = false;
            LastCollisionWasOpponent = false;
            _hazardSpawner?.SpawnCourseHazard(_course, seed);
            _demonstrationRecorder?.BeginEpisode(seed);
            CourseGenerated?.Invoke(_course);
        }

        void ResetVehicleState()
        {
            _vehicle?.ResetState();
            if (_rigidbody == null)
                return;
            _rigidbody.position = _episodeStartPosition;
            _rigidbody.rotation = _episodeStartRotation;
            _rigidbody.linearVelocity = Vector3.zero;
            _rigidbody.angularVelocity = Vector3.zero;
            _rigidbody.Sleep();
        }

        void FixedUpdate()
        {
            if (_episodeEnded || _course == null || _vehicle == null || _lowLevelController == null || _rigidbody == null)
                return;

            ApplyFloatingOriginIfNeeded();
            _progressMonitor?.UpdatePosition(_rigidbody.position);
            ExtendStreamingCourseIfNeeded();
            if (_progressMonitor != null && _progressMonitor.IsOffCourse)
            {
                EndEpisode(false, true);
                return;
            }
            if (_progressMonitor != null && _progressMonitor.IsCompleted)
            {
                FinishEpisode(true, false, false);
                return;
            }

            var waypointIndex = FindNearestWaypoint(_rigidbody.position);
            var waypoints = RelativeCourseObservation.Encode(_course, _rigidbody.position, _rigidbody.rotation, waypointIndex, _observationWaypointCount);
            var visibleDistance = MeasureVisibleDistance();
            var opponent = _headToHeadScenario == null ? default : _headToHeadScenario.ObserveClosestOpponent();
            var observation = PlanningObservationEncoder.Encode(
                waypoints,
                opponent,
                _rigidbody.linearVelocity.magnitude,
                visibleDistance,
                _reactionTime,
                _maximumDeceleration);
            ObservationReady?.Invoke(observation);
            if (_cameraObservationSensor != null && _cameraObservationSensor.CaptureIfDue())
            {
                var cameraFrame = _cameraObservationSensor.CurrentFrame();
                _demonstrationRecorder?.QueueCameraFrame(cameraFrame);
                CameraObservationReady?.Invoke(cameraFrame);
            }

            var command = ChooseCommand(observation, waypoints);
            var target = waypoints.Length == 0 ? default : waypoints[Mathf.Min(1, waypoints.Length - 1)];
            var headingError = waypoints.Length == 0 ? 0f : Mathf.Atan2(target.LocalPosition.x, Mathf.Max(0.01f, target.LocalPosition.z));
            _lowLevelController.Apply(command, _rigidbody.linearVelocity.magnitude, headingError);
            var safeSpeed = waypoints.Length == 0 ? observation.Features[1] : waypoints[0].MaximumSafeSpeed;
            var progressMeters = _progressMonitor == null ? 0f : _progressMonitor.DeltaProgressMeters;
            var reward = PlanningReward.Calculate(progressMeters, false, false, _rigidbody.linearVelocity.magnitude, safeSpeed);
            _demonstrationRecorder?.Record(observation, command, reward);
            StepRewardReady?.Invoke(reward);
        }

        public void EndEpisode(bool collision, bool offCourse)
        {
            FinishEpisode(false, collision, offCourse, false);
        }

        /// <summary>Ends an externally managed episode, optionally recording an evaluation timeout.</summary>
        public void EndEpisode(bool collision, bool offCourse, bool timedOut)
        {
            FinishEpisode(false, collision, offCourse, timedOut);
        }

        void FinishEpisode(bool completed, bool collision, bool offCourse, bool timedOut = false)
        {
            if (_episodeEnded)
                return;
            _episodeEnded = true;
            var speed = _rigidbody == null ? 0f : _rigidbody.linearVelocity.magnitude;
            var safeSpeed = _course == null || _rigidbody == null
                ? 0f
                : _course.MaximumSafeSpeed[Mathf.Clamp(FindNearestWaypoint(_rigidbody.position), 0, _course.MaximumSafeSpeed.Count - 1)];
            var reward = PlanningReward.CalculateTerminal(completed, collision, offCourse, timedOut, speed, safeSpeed);
            _demonstrationRecorder?.EndEpisode(new PlanningObservation(Array.Empty<float>()), default, reward);
            StepRewardReady?.Invoke(reward);
            EpisodeFinished?.Invoke(completed, collision, offCourse);
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
            if (_visibilitySafetyDisabled || _egoCamera == null)
                return _maximumVisibleDistance;
            return Physics.Raycast(_egoCamera.transform.position, _egoCamera.transform.forward, out var hit, _maximumVisibleDistance, ~0, QueryTriggerInteraction.Ignore)
                ? hit.distance
                : _maximumVisibleDistance;
        }

        void ExtendStreamingCourseIfNeeded()
        {
            if (_streamingCourse == null || _progressMonitor == null || _course == null ||
                _progressMonitor.NearestWaypointIndex < _course.Centerline.Count - _streamingExtensionWaypointMargin)
                return;
            var firstNewIndex = _streamingCourse.AppendNextChunk();
            _course = _streamingCourse.Course;
            _trackBuilder?.Append(_course, firstNewIndex);
            _trackBuilder?.DiscardBefore(_progressMonitor.NearestWaypointIndex, _streamingRetainedWaypointsBehind);
            _progressMonitor.ExtendCourse(_course);
            _headToHeadScenario?.SetCourse(_course);
            TrimStreamingHistoryIfNeeded();
            CourseExtended?.Invoke(_course);
        }

        void TrimStreamingHistoryIfNeeded()
        {
            if (_streamingCourse == null || _course == null || _progressMonitor == null || _course.Centerline.Count <= _maximumStreamingWaypointHistory)
                return;
            var firstRetained = Mathf.Max(0, _progressMonitor.NearestWaypointIndex - _streamingRetainedWaypointsBehind);
            var removed = _streamingCourse.TrimBefore(firstRetained);
            if (removed == 0) return;
            _course = _streamingCourse.Course;
            _trackBuilder?.RebaseWaypoints(removed, _course);
            _progressMonitor.TrimCourse(_course, removed);
            _headToHeadScenario?.RebaseCourse(removed);
        }

        void ApplyFloatingOriginIfNeeded()
        {
            if (_streamingCourse == null)
                return;
            var planarPosition = new Vector3(_rigidbody.position.x, 0f, _rigidbody.position.z);
            if (planarPosition.sqrMagnitude < _floatingOriginThreshold * _floatingOriginThreshold)
                return;
            var offset = -planarPosition;
            _course.Translate(offset);
            _trackBuilder?.Translate(offset);
            _rigidbody.position += offset;
            _headToHeadScenario?.Translate(offset);
            _hazardSpawner?.Translate(offset);
        }

        void OnCollisionEnter(Collision collision)
        {
            if (_episodeEnded || collision.collider == null)
                return;
            LastCollisionWasBlindCornerHazard = collision.collider.GetComponentInParent<EpisodeHazard>() != null;
            LastCollisionWasOpponent = collision.collider.GetComponentInParent<OpponentCourseFollower>() != null;
            if (LastCollisionWasBlindCornerHazard || LastCollisionWasOpponent || collision.collider.GetComponentInParent<CourseBoundary>() != null)
                EndEpisode(true, false);
        }

        PlanningCommand ChooseCommand(PlanningObservation observation, RelativeCourseWaypoint[] waypoints)
        {
            if (CommandProvider != null)
                return CommandProvider(observation);
            if (_behaviorCloningPolicy != null && _behaviorCloningPolicy.TryPredict(observation, out var bcCommand))
                return bcCommand;
            return ReferencePlanningPolicy.Choose(waypoints, observation.Features[1], _trackHalfWidth);
        }

        static bool HasCommandLineArgument(string parameter)
        {
            foreach (var argument in Environment.GetCommandLineArgs())
                if (argument == parameter) return true;
            return false;
        }

    }
}
