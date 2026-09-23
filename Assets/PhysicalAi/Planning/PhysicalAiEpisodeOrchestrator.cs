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
    /// <summary>Identifies an operation that changes streamed-world bookkeeping.</summary>
    public enum StreamingCourseTransitionKind
    {
        AppendAndRetire,
        HistoryTrim,
        FloatingOriginRebase
    }

    /// <summary>
    /// Immediate vehicle-state sample around a streamed-course operation.  A
    /// floating-origin rebase is allowed to translate position by
    /// <see cref="ExpectedPositionDelta"/>; all other state deltas must remain
    /// continuous.  This exposes a measurable contract for manual-drive logs
    /// without confusing an intentional coordinate shift for an impulse.
    /// </summary>
    public readonly struct StreamingCourseTransition
    {
        public readonly StreamingCourseTransitionKind Kind;
        public readonly Vector3 PositionDelta;
        public readonly Vector3 ExpectedPositionDelta;
        public readonly Quaternion RotationBefore;
        public readonly Quaternion RotationAfter;
        public readonly Vector3 LinearVelocityDelta;
        public readonly Vector3 AngularVelocityDelta;
        public readonly Vector3 ControlInputBefore;
        public readonly Vector3 ControlInputAfter;
        public readonly string CommandSource;
        public readonly float ActualSteeringAngleBefore;
        public readonly float ActualSteeringAngleAfter;
        public readonly float MaximumAxleForceDelta;
        public readonly float DynamicWeightDelta;
        public readonly int RetainedWaypointCount;
        public readonly int ForwardWaypointCount;
        public readonly int ChunkCount;
        public readonly int AppendedCornerDirection;

        public StreamingCourseTransition(StreamingCourseTransitionKind kind,
            Vector3 positionDelta, Vector3 expectedPositionDelta,
            Quaternion rotationBefore, Quaternion rotationAfter,
            Vector3 linearVelocityDelta, Vector3 angularVelocityDelta,
            Vector3 controlInputBefore, Vector3 controlInputAfter, string commandSource,
            float actualSteeringAngleBefore, float actualSteeringAngleAfter,
            float maximumAxleForceDelta, float dynamicWeightDelta,
            int retainedWaypointCount, int forwardWaypointCount, int chunkCount,
            int appendedCornerDirection)
        {
            Kind = kind;
            PositionDelta = positionDelta;
            ExpectedPositionDelta = expectedPositionDelta;
            RotationBefore = rotationBefore;
            RotationAfter = rotationAfter;
            LinearVelocityDelta = linearVelocityDelta;
            AngularVelocityDelta = angularVelocityDelta;
            ControlInputBefore = controlInputBefore;
            ControlInputAfter = controlInputAfter;
            CommandSource = commandSource;
            ActualSteeringAngleBefore = actualSteeringAngleBefore;
            ActualSteeringAngleAfter = actualSteeringAngleAfter;
            MaximumAxleForceDelta = maximumAxleForceDelta;
            DynamicWeightDelta = dynamicWeightDelta;
            RetainedWaypointCount = retainedWaypointCount;
            ForwardWaypointCount = forwardWaypointCount;
            ChunkCount = chunkCount;
            AppendedCornerDirection = appendedCornerDirection;
        }

        public Vector3 UnexpectedPositionDelta => PositionDelta - ExpectedPositionDelta;
        public float RotationDeltaDegrees => Quaternion.Angle(RotationBefore, RotationAfter);
        public Vector3 ControlInputDelta => ControlInputAfter - ControlInputBefore;
        public float ActualSteeringAngleDelta => Mathf.Abs(ActualSteeringAngleAfter - ActualSteeringAngleBefore);
    }

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
        /// <summary>
        /// The venue floor. It is what the wheels actually rest on: the generated road
        /// is visual only, so the floor must move with the world during a rebase or the
        /// car is left standing on ground that no longer matches the course around it.
        /// </summary>
        [SerializeField] Transform _venueFloor;
        [SerializeField] Camera _egoCamera;
        [SerializeField] CameraObservationSensor _cameraObservationSensor;
        [SerializeField] bool _useStreamingTraining;
        // Three chunks guarantee at least 13.5 m from the minimum primitive
        // lengths, beyond the 6 m camera horizon, without constructing an
        // unbounded initial cache before the vehicle starts moving.
        [SerializeField, Min(1)] int _initialStreamingChunkCount = 3;
        // Extend while roughly 12 m of the generated road remains, instead of
        // waiting until the vehicle is already at the visible end.
        [SerializeField, Min(2)] int _streamingExtensionWaypointMargin = 120;
        [SerializeField, Min(0)] int _streamingRetainedWaypointsBehind = 80;
        [SerializeField, Min(16)] int _maximumStreamingWaypointHistory = 400;
        [SerializeField, Min(1f)] float _floatingOriginThreshold = 50f;
        [SerializeField] CourseGenerationSettings _courseSettings = default;
        [SerializeField] int _episodeSeed = 1;
        [SerializeField, Min(1)] int _observationWaypointCount = 6;
        // A 0.1 m course sample is appropriate for track construction but too
        // near for planning at racing speed. Six features at this stride span
        // 2.5 m without changing the observation layout contract.
        [SerializeField, Min(1)] int _observationWaypointStride = 5;
        [SerializeField, Min(0.01f)] float _trackHalfWidth = 0.5f;
        [SerializeField, Min(0.01f)] float _reactionTime = 0.5f;
        [SerializeField, Min(0.01f)] float _maximumDeceleration = 6f;
        [SerializeField, Min(0.1f)] float _maximumVisibleDistance = 6f;
        // The deterministic reference policy is a regression/smoke driver, not
        // the learned high-speed planner. Keep enough lateral reserve to verify
        // generated corridor integrity independently of policy training.
        [SerializeField, Range(0.1f, 1f)] float _referencePlanningSpeedFraction = 0.6f;

        GeneratedCourse _course;
        StreamingCourseState _streamingCourse;
        Rigidbody _rigidbody;
        Vector3 _episodeStartPosition;
        Quaternion _episodeStartRotation;
        bool _episodeEnded;
        string _configuredMapMode;
        int _configuredSeed;
        bool _requiresRl;
        CourseMapDocument _loadedMap;
        BevAnnotationCapture _bevCapture;
        public BevObservation CurrentBev { get; private set; }
        public bool EpisodeEnded => _episodeEnded;
        public string MapMode => _streamingCourse == null ? "fixed" : "endless";
        public string MapHash { get; private set; }
        public string MapSourceHash => _loadedMap?.ContentHash;
        public string CourseSettingsJson => JsonUtility.ToJson(_courseSettings);
        public Vector3 FloatingOriginOffset { get; private set; }
        public CourseMapDocument CaptureMap() => CourseMapDocument.Capture(_course, Vector3.zero, 0, _trackHalfWidth);
        int _rewardedEgoOvertakeCount;
        int _rewardedOpponentOvertakeCount;
        bool _visibilitySafetyDisabled;
        public bool LastCollisionWasBlindCornerHazard { get; private set; }
        public bool LastCollisionWasOpponent { get; private set; }

        public event Action<GeneratedCourse> CourseGenerated;
        public event Action<GeneratedCourse> CourseExtended;
        public event Action<PlanningObservation> ObservationReady;
        public event Action<CameraObservationFrame> CameraObservationReady;
        public event Action<float> StepRewardReady;
        public event Action<bool, bool, bool> EpisodeFinished;
        /// <summary>Raised immediately after every append/retire, trim, or world rebase.</summary>
        public event Action<StreamingCourseTransition> StreamingTransitioned;
        public string VehicleModelName => _vehicle == null ? "unknown" : _vehicle.VehicleModelName;
        public Func<PlanningObservation, PlanningCommand> CommandProvider;
        /// <summary>
        /// Interactive driving applies throttle, brake, and steering directly.
        /// Keep the planner observation/recording loop alive, but do not overwrite
        /// those commands with its high-level controller on the following physics
        /// step.
        /// </summary>
        public bool ManualActuationOverride { get; set; }
        public GeneratedCourse Course => _course;
        public int EpisodeSeed => _episodeSeed;
        public float ProgressMeters => _progressMonitor == null ? 0f : _progressMonitor.ProgressMeters;
        public float DistanceFromCenterline => _progressMonitor == null ? 0f : _progressMonitor.DistanceFromCenterline;
        public int EgoOvertakeCount => _headToHeadScenario == null ? 0 : _headToHeadScenario.EgoOvertakeCount;
        public int OpponentOvertakeCount => _headToHeadScenario == null ? 0 : _headToHeadScenario.OpponentOvertakeCount;
        public bool HazardWasSpawned => _hazardSpawner != null && _hazardSpawner.HazardSpawnedThisEpisode;

        /// <summary>Separates solo generalisation rollouts from explicit racing interaction runs.</summary>
        public void SetHeadToHeadEnabled(bool enabled) => _headToHeadScenario?.SetInteractionEnabled(enabled);

        /// <summary>Sets an opt-in opponent probe before the evaluated course is reset.</summary>
        public void ConfigureHeadToHead(float? targetSpeed = null, float? initialProgressMeters = null, float? lateralOffset = null,
            float? lateralOscillationAmplitude = null, float? lateralOscillationFrequencyHz = null) =>
            _headToHeadScenario?.ConfigureOpponents(targetSpeed, initialProgressMeters, lateralOffset,
                lateralOscillationAmplitude, lateralOscillationFrequencyHz);

        /// <summary>
        /// Identifies the command source for an evaluation artifact.  This is not a
        /// performance claim: it prevents a reference-planner result from being
        /// mistaken for BC or RL generalisation evidence.
        /// </summary>
        public string ActivePolicyLabel
        {
            get
            {
                if (ManualActuationOverride) return "manual-teleoperation";
                if (_requiresRl && (GetComponent<PythonRlBridge>() == null || !GetComponent<PythonRlBridge>().IsConnected))
                    return "rl-disconnected";
                if (CommandProvider != null)
                {
                    var declaringType = CommandProvider.Method.DeclaringType;
                    return declaringType == null ? "external-command-provider" : declaringType.Name;
                }
                return _behaviorCloningPolicy != null && _behaviorCloningPolicy.IsReady
                    ? "behavior-cloning"
                    : "reference-planner";
            }
        }

        void Awake()
        {
            if (_courseSettings.SampleSpacing <= 0f)
                _courseSettings = CourseGenerationSettings.Default;
            _rigidbody = _vehicle != null ? _vehicle.GetComponent<Rigidbody>() : null;
            _episodeStartPosition = transform.position;
            _episodeStartRotation = transform.rotation;
            _configuredMapMode = PhysicalAiLaunchArguments.GetValue("-physicalAiMapMode");
            if (_configuredMapMode != null && _configuredMapMode != "endless" && _configuredMapMode != "fixed")
                throw new ArgumentException("Map mode must be fixed or endless.");
            _episodeSeed = PhysicalAiLaunchArguments.GetInt("-physicalAiSeed", _episodeSeed);
            _configuredSeed = _episodeSeed;
            _requiresRl = PhysicalAiLaunchArguments.HasFlag("-physicalAiRlPort") || PhysicalAiLaunchArguments.GetValue("-physicalAiMode") == "rl";
            if (_configuredMapMode != null) _useStreamingTraining = _configuredMapMode == "endless";
            var mapPath = PhysicalAiLaunchArguments.GetValue("-physicalAiMapPath");
            if (!string.IsNullOrEmpty(mapPath))
            {
                if (_configuredMapMode != "fixed") throw new ArgumentException("A map file requires fixed-map mode.");
                _loadedMap = CourseMapDocument.Load(mapPath);
                _trackHalfWidth = _loadedMap.halfWidth;
                if (_trackBuilder != null) _trackBuilder.RoadWidth = _trackHalfWidth * 2;
            }
            var model = PhysicalAiLaunchArguments.GetValue("-physicalAiVehicleModel");
            if (_vehicle != null && model != null) _vehicle.NoSlipAssistance = model == "assisted";
            _visibilitySafetyDisabled = HasCommandLineArgument("-physicalAiDisableVisibilitySafety");
            var headToHead = HasCommandLineArgument("-physicalAiHeadToHead");
            SetHeadToHeadEnabled(headToHead);
            if (headToHead)
                ConfigureHeadToHead(
                    GetCommandLineFloat("-physicalAiOpponentTargetSpeed"),
                    GetCommandLineFloat("-physicalAiOpponentInitialProgressMeters"),
                    GetCommandLineFloat("-physicalAiOpponentLateralOffset"),
                    GetCommandLineFloat("-physicalAiOpponentLateralOscillationAmplitude"),
                    GetCommandLineFloat("-physicalAiOpponentLateralOscillationFrequencyHz"));
        }

        void Start() { if (_course == null) ResetEpisode(_episodeSeed); }

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
            var loadedFixedMap = _configuredMapMode == "fixed" && _loadedMap != null && !useHoldoutCourse;
            var startPose = loadedFixedMap ? _loadedMap.StartPose(_episodeStartPosition.y) :
                new Pose(_episodeStartPosition, _episodeStartRotation);
            ResetVehicleState(startPose);
            ResetTransformFrameOrigins();
            FloatingOriginOffset = Vector3.zero;
            // ResetVehicleState may be called from an episode-finished callback in
            // the same physics tick. At that point Transform can still expose the
            // previous episode's off-course pose even though the Rigidbody has been
            // reset. Course generation must use the authoritative reset position.
            var courseOrigin = new Vector3(_episodeStartPosition.x, 0f, _episodeStartPosition.z);
            if (_configuredMapMode == "fixed" && !useHoldoutCourse)
            {
                _streamingCourse = null;
                _course = _loadedMap != null ? _loadedMap.Instantiate(Vector3.zero, 0) :
                    ClosedHoldoutCourseGenerator.Generate(_configuredSeed, _courseSettings, courseOrigin, _episodeStartRotation.eulerAngles.y);
            }
            else if (_useStreamingTraining && !useHoldoutCourse)
            {
                _streamingCourse = new StreamingCourseState();
                _streamingCourse.Reset(seed, _courseSettings, courseOrigin, _episodeStartRotation.eulerAngles.y);
                for (var chunk = 1; chunk < _initialStreamingChunkCount; chunk++)
                    _streamingCourse.AppendNextChunk();
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
            _rewardedEgoOvertakeCount = 0;
            _rewardedOpponentOvertakeCount = 0;
            _episodeEnded = false;
            LastCollisionWasBlindCornerHazard = false;
            LastCollisionWasOpponent = false;
            _hazardSpawner?.SpawnCourseHazard(_course, seed);
            MapHash = CaptureMap().ContentHash;
            _demonstrationRecorder?.BeginEpisode(seed);
            CourseGenerated?.Invoke(_course);
            Debug.Log($"Physical AI episode ready: seed {seed}, map {MapMode}, samples {_course.Centerline.Count}, model {VehicleModelName}.");
        }

        void ResetVehicleState(Pose pose)
        {
            _vehicle?.ResetState();
            if (_rigidbody == null)
                return;
            _rigidbody.position = pose.position;
            _rigidbody.rotation = pose.rotation;
            _rigidbody.linearVelocity = Vector3.zero;
            _rigidbody.angularVelocity = Vector3.zero;
            // Synchronise the reset before the tire adapter and sensors sample this tick.
            Physics.SyncTransforms();
            _rigidbody.WakeUp();
        }

        void FixedUpdate()
        {
            if (_requiresRl && (GetComponent<PythonRlBridge>() == null || !GetComponent<PythonRlBridge>().IsConnected)) return;
            if (_episodeEnded || _course == null || _vehicle == null || _lowLevelController == null || _rigidbody == null)
                return;

            RefreshStreamingCourseForCurrentPosition();
            _headToHeadScenario?.UpdateEgoProgress(ProgressMeters);
            var egoOvertakeDelta = Mathf.Max(0, EgoOvertakeCount - _rewardedEgoOvertakeCount);
            var opponentOvertakeDelta = Mathf.Max(0, OpponentOvertakeCount - _rewardedOpponentOvertakeCount);
            _rewardedEgoOvertakeCount = EgoOvertakeCount;
            _rewardedOpponentOvertakeCount = OpponentOvertakeCount;
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
            var waypoints = RelativeCourseObservation.Encode(_course, _rigidbody.position, _rigidbody.rotation, waypointIndex, _observationWaypointCount, _observationWaypointStride);
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
                if (PhysicalAiLaunchArguments.GetValue("-physicalAiBev") == "true")
                {
                    _bevCapture ??= new BevAnnotationCapture(PhysicalAiLaunchArguments.GetInt("-physicalAiBevSize", 128),
                        PhysicalAiLaunchArguments.GetFloat("-physicalAiBevMetersPerPixel", 0.1f));
                    CurrentBev = _bevCapture.Capture(_trackBuilder, _vehicle.transform, cameraFrame.FrameIndex, cameraFrame.SimulationTime);
                }
                _demonstrationRecorder?.QueueCameraFrame(cameraFrame);
                CameraObservationReady?.Invoke(cameraFrame);
            }

            var command = PlanningCommandFeasibility.Project(ChooseCommand(observation, waypoints), waypoints, observation.Features[1]);
            var target = waypoints.Length == 0 ? default : waypoints[Mathf.Min(ReferencePlanningPolicy.LookAheadWaypointIndex, waypoints.Length - 1)];
            var headingError = waypoints.Length == 0 ? 0f : Mathf.Atan2(target.LocalPosition.x, Mathf.Max(0.01f, target.LocalPosition.z));
            if (!ManualActuationOverride)
                _lowLevelController.Apply(command, _rigidbody.linearVelocity.magnitude, headingError);
            var safeSpeed = waypoints.Length == 0 ? observation.Features[1] : waypoints[0].MaximumSafeSpeed;
            var progressMeters = _progressMonitor == null ? 0f : _progressMonitor.DeltaProgressMeters;
            var reward = PlanningReward.Calculate(progressMeters, false, false, _rigidbody.linearVelocity.magnitude,
                safeSpeed, egoOvertakeDelta, opponentOvertakeDelta);
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
            Debug.Log($"Physical AI episode ended: completed={completed}, collision={collision}, offCourse={offCourse}, timedOut={timedOut}, progress={ProgressMeters:F2} m.");
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

            // The on-car camera deliberately looks slightly down at the road.  A
            // ray against every layer therefore sees its own drivable surface a few
            // centimetres ahead and caps the planner to crawl speed.  Visibility is
            // about occluding walls/hazards, not the supporting road or ego body.
            var groundLayer = LayerMask.NameToLayer(ProceduralCourseTrackBuilder.DrivableSurfaceLayerName);
            var mask = groundLayer < 0 ? ~0 : ~(1 << groundLayer);
            var closestDistance = _maximumVisibleDistance;
            // A centre-only ray misses a small hazard once it has emerged to one
            // side of the lane. Sample the optical centre and two rays on each
            // side inside the authored camera FOV; this is still visibility, not
            // privileged course geometry.
            const int rayCount = 7;
            var halfHorizontalFov = _egoCamera.fieldOfView * 0.5f;
            for (var ray = 0; ray < rayCount; ray++)
            {
                var normalized = rayCount == 1 ? 0f : ray / (float)(rayCount - 1) * 2f - 1f;
                var direction = Quaternion.AngleAxis(normalized * halfHorizontalFov, _egoCamera.transform.up) * _egoCamera.transform.forward;
                var hits = Physics.RaycastAll(_egoCamera.transform.position, direction,
                    _maximumVisibleDistance, mask, QueryTriggerInteraction.Ignore);
                System.Array.Sort(hits, (first, second) => first.distance.CompareTo(second.distance));
                foreach (var hit in hits)
                {
                    if (_vehicle != null && hit.collider.transform.IsChildOf(_vehicle.transform))
                        continue;
                    closestDistance = Mathf.Min(closestDistance, hit.distance);
                    break;
                }
            }
            return closestDistance;
        }

        /// <summary>
        /// Updates progress and maintains the forward streaming horizon.  This is
        /// public so the batch smoke test can exercise the same path without
        /// pretending to run Unity's private physics callback in EditMode.
        /// </summary>
        public bool RefreshStreamingCourseForCurrentPosition()
        {
            if (_progressMonitor == null || _course == null || _rigidbody == null)
                return false;
            // Holdout evaluation uses a finite closed course, so it has no
            // streaming state to refresh. Its progress still drives completion,
            // off-course termination and the report metrics every physics tick.
            if (_streamingCourse == null)
            {
                _progressMonitor.UpdatePosition(_rigidbody.position);
                return false;
            }
            ApplyFloatingOriginIfNeeded();
            _progressMonitor.UpdatePosition(_rigidbody.position);
            var extended = false;
            // A chunk can be shorter than the required forward horizon.  Keep
            // appending until the contract is restored instead of exposing a road
            // end for one or more physics frames.
            for (var attempt = 0; attempt < 8 && ExtendStreamingCourseIfNeeded(); attempt++)
                extended = true;
            return extended;
        }

        bool ExtendStreamingCourseIfNeeded()
        {
            if (_streamingCourse == null || _progressMonitor == null || _course == null ||
                _progressMonitor.NearestWaypointIndex < _course.Centerline.Count - _streamingExtensionWaypointMargin)
                return false;
            var stateBefore = CaptureVehicleState();
            var firstNewIndex = _streamingCourse.AppendNextChunk();
            _course = _streamingCourse.Course;
            _trackBuilder?.Append(_course, firstNewIndex);
            _trackBuilder?.DiscardBefore(_progressMonitor.NearestWaypointIndex, _streamingRetainedWaypointsBehind);
            _progressMonitor.ExtendCourse(_course);
            _headToHeadScenario?.ExtendCourse(_course);
            TrimStreamingHistoryIfNeeded();
            PublishStreamingTransition(StreamingCourseTransitionKind.AppendAndRetire, stateBefore, Vector3.zero,
                _streamingCourse.LastAppendedCornerDirection);
            CourseExtended?.Invoke(_course);
            return true;
        }

        void TrimStreamingHistoryIfNeeded()
        {
            if (_streamingCourse == null || _course == null || _progressMonitor == null || _course.Centerline.Count <= _maximumStreamingWaypointHistory)
                return;
            var firstRetained = Mathf.Max(0, _progressMonitor.NearestWaypointIndex - _streamingRetainedWaypointsBehind);
            var stateBefore = CaptureVehicleState();
            var removed = _streamingCourse.TrimBefore(firstRetained);
            if (removed == 0) return;
            _course = _streamingCourse.Course;
            _trackBuilder?.RebaseWaypoints(removed, _course);
            _progressMonitor.TrimCourse(_course, removed);
            _headToHeadScenario?.RebaseCourse(removed);
            PublishStreamingTransition(StreamingCourseTransitionKind.HistoryTrim, stateBefore, Vector3.zero);
        }

        void ApplyFloatingOriginIfNeeded()
        {
            if (_streamingCourse == null)
                return;
            var planarPosition = new Vector3(_rigidbody.position.x, 0f, _rigidbody.position.z);
            if (planarPosition.sqrMagnitude < _floatingOriginThreshold * _floatingOriginThreshold)
                return;
            var stateBefore = CaptureVehicleState();
            var offset = -planarPosition;
            FloatingOriginOffset -= offset;
            _streamingCourse.Translate(offset);
            _trackBuilder?.Translate(offset);
            if (_venueFloor != null)
                _venueFloor.position += offset;
            _rigidbody.position += offset;
            // TF is published in the same physics frame.  Synchronise the
            // Rigidbody move before sampling its source Transform, otherwise one
            // stale frame leaks the pre-rebase local coordinate to odometry.
            Physics.SyncTransforms();
            foreach (var transformFrame in _vehicle.GetComponentsInChildren<TransformFrameSensor>())
                transformFrame.ApplyFloatingOriginOffset(offset);
            _headToHeadScenario?.Translate(offset);
            _hazardSpawner?.Translate(offset);
            PublishStreamingTransition(StreamingCourseTransitionKind.FloatingOriginRebase, stateBefore, offset);
        }

        readonly struct VehicleState
        {
            public readonly Vector3 Position;
            public readonly Quaternion Rotation;
            public readonly Vector3 LinearVelocity;
            public readonly Vector3 AngularVelocity;
            public readonly Vector3 ControlInput;
            public readonly float ActualSteeringAngle;
            public readonly float DynamicWeight;
            public readonly Vector3 FrontAxleForce;
            public readonly Vector3 RearAxleForce;

            public VehicleState(Rigidbody body, CombinedSlipVehicle vehicle)
            {
                Position = body == null ? Vector3.zero : body.position;
                Rotation = body == null ? Quaternion.identity : body.rotation;
                LinearVelocity = body == null ? Vector3.zero : body.linearVelocity;
                AngularVelocity = body == null ? Vector3.zero : body.angularVelocity;
                ControlInput = vehicle == null
                    ? Vector3.zero
                    : new Vector3(vehicle.Throttle, vehicle.Brake, vehicle.Steering);
                ActualSteeringAngle = vehicle == null ? 0 : vehicle.ActualSteeringAngleRadians;
                DynamicWeight = vehicle == null ? 0 : vehicle.DynamicWeight;
                FrontAxleForce = vehicle == null ? Vector3.zero : ForceVector(vehicle.FrontAxleForce);
                RearAxleForce = vehicle == null ? Vector3.zero : ForceVector(vehicle.RearAxleForce);
            }

            static Vector3 ForceVector(SingleTrackAxleForce force) =>
                new Vector3((float)force.Longitudinal, (float)force.Lateral, (float)force.NormalLoad);
        }

        VehicleState CaptureVehicleState() => new VehicleState(_rigidbody, _vehicle);

        void PublishStreamingTransition(StreamingCourseTransitionKind kind, VehicleState before,
            Vector3 expectedPositionDelta, int appendedCornerDirection = 0)
        {
            if (_rigidbody == null)
                return;
            var after = CaptureVehicleState();
            StreamingTransitioned?.Invoke(new StreamingCourseTransition(kind,
                after.Position - before.Position, expectedPositionDelta,
                before.Rotation, after.Rotation,
                after.LinearVelocity - before.LinearVelocity,
                after.AngularVelocity - before.AngularVelocity,
                before.ControlInput, after.ControlInput, ActivePolicyLabel,
                before.ActualSteeringAngle, after.ActualSteeringAngle,
                Mathf.Max((after.FrontAxleForce - before.FrontAxleForce).magnitude,
                    (after.RearAxleForce - before.RearAxleForce).magnitude),
                Mathf.Abs(after.DynamicWeight - before.DynamicWeight),
                _course == null ? 0 : _course.Centerline.Count,
                _course == null || _progressMonitor == null ? 0 :
                    Mathf.Max(0, _course.Centerline.Count - 1 - _progressMonitor.NearestWaypointIndex),
                _streamingCourse == null ? 0 : _streamingCourse.ChunkCount,
                appendedCornerDirection));
        }

        void ResetTransformFrameOrigins()
        {
            if (_vehicle == null)
                return;
            foreach (var transformFrame in _vehicle.GetComponentsInChildren<TransformFrameSensor>())
                transformFrame.ResetFloatingOrigin();
        }

        void OnDestroy() => _bevCapture?.Dispose();

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
            if (_requiresRl) return default; // Never silently turn an RL run into reference/BC driving.
            if (_behaviorCloningPolicy != null && _behaviorCloningPolicy.TryPredict(observation, out var bcCommand))
                return bcCommand;
            return ReferencePlanningPolicy.Choose(waypoints, observation.Features[1], _trackHalfWidth,
                _courseSettings.DesignSpeed * _referencePlanningSpeedFraction);
        }

        static bool HasCommandLineArgument(string parameter)
        {
            foreach (var argument in PhysicalAiLaunchArguments.Current)
                if (argument == parameter) return true;
            return false;
        }

        static float? GetCommandLineFloat(string parameter)
        {
            return float.TryParse(PhysicalAiLaunchArguments.GetValue(parameter), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed) && !float.IsNaN(parsed) && !float.IsInfinity(parsed)
                ? parsed : (float?)null;
        }

    }
}
