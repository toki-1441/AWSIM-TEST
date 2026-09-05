// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;
using PhysicalAi.Course;
using PhysicalAi.Planning;
using PhysicalAi.VehicleDynamics;
using UnityEngine;

namespace PhysicalAi.Evaluation
{
    [Serializable]
    public sealed class WheelDynamicsTelemetry
    {
        public bool grounded;
        public float angularVelocityRadiansPerSecond;
        public float normalForceNewtons;
        public float longitudinalSlip;
        public float slipAngleRadians;
        public float longitudinalVelocityMetersPerSecond;
        public float suspensionCompressionMeters;
        public float suspensionVelocityMetersPerSecond;
        public float longitudinalForceNewtons;
        public float lateralForceNewtons;
        public float tireUtilization;
    }

    [Serializable]
    public sealed class VehicleDynamicsTelemetryFrame
    {
        public int episodeSeed;
        public int step;
        public float simulationTimeSeconds;
        public Vector3 worldVelocityMetersPerSecond;
        public Vector3 localVelocityMetersPerSecond;
        public Vector3 angularVelocityRadiansPerSecond;
        public float throttle;
        public float brake;
        public float steering;
        public float surfaceFriction;
        public int groundedWheelCount;
        public float peakTireUtilization;
        public WheelDynamicsTelemetry[] wheels;
    }

    /// <summary>
    /// Opt-in, per-physics-step diagnostic log for validating contact dynamics.
    /// It is deliberately separate from BC demonstrations so diagnostic signals
    /// cannot silently alter the observation/data-set contract.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public sealed class VehicleDynamicsTelemetryRecorder : MonoBehaviour
    {
        [SerializeField] CombinedSlipVehicle _vehicle;
        [SerializeField] PhysicalAiEpisodeOrchestrator _orchestrator;
        [SerializeField] HoldoutEvaluationRunner _evaluationRunner;
        [SerializeField] string _filePrefix = "physical-ai-dynamics";

        StreamWriter _writer;
        int _episodeSeed;
        int _step;

        void OnEnable()
        {
            if (_orchestrator == null) return;
            _orchestrator.CourseGenerated += BeginEpisode;
            if (_evaluationRunner != null) _evaluationRunner.EpisodeMeasured += EndEvaluationEpisode;
        }

        void OnDisable()
        {
            if (_orchestrator != null)
            {
                _orchestrator.CourseGenerated -= BeginEpisode;
            }
            if (_evaluationRunner != null)
            {
                _evaluationRunner.EpisodeMeasured -= EndEvaluationEpisode;
            }
            Close();
        }

        void FixedUpdate()
        {
            if (_writer == null || _vehicle == null) return;
            var body = _vehicle.GetComponent<Rigidbody>();
            if (body == null) return;
            var wheels = _vehicle.Wheels;
            var wheelSamples = new WheelDynamicsTelemetry[wheels.Count];
            for (var index = 0; index < wheels.Count; index++)
            {
                var wheel = wheels[index];
                wheelSamples[index] = wheel == null ? new WheelDynamicsTelemetry() : new WheelDynamicsTelemetry
                {
                    grounded = wheel.IsGrounded,
                    angularVelocityRadiansPerSecond = wheel.AngularVelocity,
                    normalForceNewtons = wheel.LastNormalForce,
                    longitudinalSlip = wheel.LastLongitudinalSlip,
                    slipAngleRadians = wheel.LastSlipAngleRadians,
                    longitudinalVelocityMetersPerSecond = wheel.LastLongitudinalVelocity,
                    suspensionCompressionMeters = wheel.LastSuspensionCompression,
                    suspensionVelocityMetersPerSecond = wheel.LastSuspensionVelocity,
                    longitudinalForceNewtons = wheel.LastTireForce.Longitudinal,
                    lateralForceNewtons = wheel.LastTireForce.Lateral,
                    tireUtilization = wheel.LastTireForce.Utilization
                };
            }
            var worldVelocity = body.linearVelocity;
            Write(new VehicleDynamicsTelemetryFrame
            {
                episodeSeed = _episodeSeed,
                step = _step++,
                simulationTimeSeconds = Time.time,
                worldVelocityMetersPerSecond = worldVelocity,
                localVelocityMetersPerSecond = transform.InverseTransformDirection(worldVelocity),
                angularVelocityRadiansPerSecond = body.angularVelocity,
                throttle = _vehicle.Throttle,
                brake = _vehicle.Brake,
                steering = _vehicle.Steering,
                surfaceFriction = _vehicle.SurfaceFriction,
                groundedWheelCount = _vehicle.GroundedWheelCount,
                peakTireUtilization = _vehicle.PeakTireUtilization,
                wheels = wheelSamples
            });
        }

        void BeginEpisode(GeneratedCourse ignored)
        {
            Close();
            if (!HasCommandLineArgument("-physicalAiRecordDynamicsTelemetry") || _orchestrator == null) return;
            _episodeSeed = _orchestrator.EpisodeSeed;
            _step = 0;
            var directory = GetCommandLineArg("-physicalAiReportDirectory");
            if (string.IsNullOrEmpty(directory)) directory = Path.Combine(Application.persistentDataPath, "PhysicalAi");
            Directory.CreateDirectory(directory);
            _writer = new StreamWriter(Path.Combine(directory, $"{_filePrefix}-seed-{_episodeSeed:D6}.jsonl"), false);
        }

        void EndEvaluationEpisode(CourseEpisodeResult ignored) => Close();

        void Write(VehicleDynamicsTelemetryFrame frame)
        {
            _writer.WriteLine(JsonUtility.ToJson(frame));
            _writer.Flush();
        }

        void Close()
        {
            _writer?.Dispose();
            _writer = null;
        }

        static bool HasCommandLineArgument(string parameter)
        {
            foreach (var argument in Environment.GetCommandLineArgs()) if (argument == parameter) return true;
            return false;
        }

        static string GetCommandLineArg(string parameter)
        {
            var arguments = Environment.GetCommandLineArgs();
            for (var index = 0; index + 1 < arguments.Length; index++) if (arguments[index] == parameter) return arguments[index + 1];
            return null;
        }
    }
}
