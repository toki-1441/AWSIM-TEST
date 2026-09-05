// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using System;
using PhysicalAi.Randomization;
using PhysicalAi.VehicleDynamics;
using UnityEngine;

namespace PhysicalAi.Calibration
{
    [Serializable]
    public class TelemetryCalibrationData
    {
        public string RunId;
        public int RawSampleCount;
        public CorneringTelemetrySample[] Samples;
    }

    /// <summary>Loads real-vehicle cornering telemetry and centers simulation friction randomization on its identified grip.</summary>
    public sealed class TelemetryCalibrationApplier : MonoBehaviour
    {
        [SerializeField] TextAsset _telemetryAsset;
        [SerializeField] CombinedSlipVehicle _vehicle;
        [SerializeField] DomainRandomizer _domainRandomizer;
        [SerializeField, Min(0.01f)] float _uncalibratedDomainMeanFriction = 0.9f;

        public FrictionCalibrationResult LastResult { get; private set; }
        public string LastRunId { get; private set; }
        public int LastRawSampleCount { get; private set; }
        public event Action<FrictionCalibrationResult> CalibrationApplied;

        public bool ApplyCalibration()
        {
            if (_telemetryAsset == null)
                return false;
            var data = JsonUtility.FromJson<TelemetryCalibrationData>(_telemetryAsset.text);
            if (data?.Samples == null || data.Samples.Length == 0)
                return false;
            LastResult = TelemetryFrictionCalibrator.Estimate(data.Samples);
            LastRunId = data.RunId;
            LastRawSampleCount = data.RawSampleCount > 0 ? data.RawSampleCount : data.Samples.Length;
            TelemetryFrictionCalibrator.Apply(_vehicle, LastResult);
            _domainRandomizer?.SetFrictionScale(LastResult.FrictionCoefficient / _uncalibratedDomainMeanFriction);
            CalibrationApplied?.Invoke(LastResult);
            return true;
        }
    }
}
