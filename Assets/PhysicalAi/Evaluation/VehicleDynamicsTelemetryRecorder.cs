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
    public sealed class AxleDynamicsTelemetry
    {
        public string axle;
        public float normalForceNewtons;
        public float longitudinalForceNewtons;
        public float lateralForceNewtons;
        public float tireUtilization;
    }

    [Serializable]
    public sealed class VehicleDynamicsTelemetryFrame
    {
        public int schemaVersion = 2;
        public string vehicleModel = "planar-single-track";
        public string samplingPhase = "pre-physics-after-tire-force-queue";
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
        public float actualSteeringAngleRadians;
        public float dynamicWeight;
        public float peakTireUtilization;
        // Axle force coordinates: longitudinal forward, lateral left. Normal loads
        // are analytical support on a flat plane, not measured suspension contacts.
        public AxleDynamicsTelemetry[] axles;
    }

    /// <summary>
    /// Opt-in, per-physics-step diagnostic log for planar dynamics (schema 2).
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
            Write(CaptureFrame(_vehicle, _episodeSeed, _step++, Time.fixedTime));
        }

        public static VehicleDynamicsTelemetryFrame CaptureFrame(CombinedSlipVehicle vehicle, int seed, int step, float time)
        {
            var body = vehicle.GetComponent<Rigidbody>();
            var worldVelocity = body.linearVelocity;
            return new VehicleDynamicsTelemetryFrame
            {
                vehicleModel = vehicle.VehicleModelName,
                samplingPhase = vehicle.NoSlipAssistance ? "pre-physics-after-no-slip-velocity-constraint" : "pre-physics-after-tire-force-queue",
                episodeSeed = seed,
                step = step,
                simulationTimeSeconds = time,
                worldVelocityMetersPerSecond = worldVelocity,
                localVelocityMetersPerSecond = Quaternion.Inverse(body.rotation) * worldVelocity,
                angularVelocityRadiansPerSecond = body.angularVelocity,
                throttle = vehicle.Throttle,
                brake = vehicle.Brake,
                steering = vehicle.Steering,
                surfaceFriction = vehicle.SurfaceFriction,
                actualSteeringAngleRadians = vehicle.ActualSteeringAngleRadians,
                dynamicWeight = vehicle.DynamicWeight,
                peakTireUtilization = vehicle.PeakTireUtilization,
                axles = vehicle.NoSlipAssistance ? Array.Empty<AxleDynamicsTelemetry>() : new[] { AxleSample("front", vehicle.FrontAxleForce, vehicle.SurfaceFriction),
                    AxleSample("rear", vehicle.RearAxleForce, vehicle.SurfaceFriction) }
            };
        }

        static AxleDynamicsTelemetry AxleSample(string axle, SingleTrackAxleForce force, float mu) =>
            new AxleDynamicsTelemetry
            {
                axle = axle,
                normalForceNewtons = (float)force.NormalLoad,
                longitudinalForceNewtons = (float)force.Longitudinal,
                lateralForceNewtons = (float)force.Lateral,
                tireUtilization = mu * force.NormalLoad <= 0 ? 0 : (float)(Math.Sqrt(
                    force.Longitudinal * force.Longitudinal + force.Lateral * force.Lateral) / (mu * force.NormalLoad))
            };

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
            foreach (var argument in PhysicalAiLaunchArguments.Current) if (argument == parameter) return true;
            return false;
        }

        static string GetCommandLineArg(string parameter) => PhysicalAiLaunchArguments.GetValue(parameter);
    }
}
