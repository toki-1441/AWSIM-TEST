// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using PhysicalAi.VehicleDynamics;
using UnityEngine;

namespace PhysicalAi.Calibration
{
    /// <summary>Minimal real-vehicle telemetry needed to identify lateral tire grip.</summary>
    [Serializable]
    public struct CorneringTelemetrySample
    {
        public float Speed;
        public float YawRateRadiansPerSecond;
        public float BankDegrees;
    }

    public readonly struct FrictionCalibrationResult
    {
        public readonly float FrictionCoefficient;
        public readonly int SampleCount;
        public readonly float LateralAccelerationRmse;

        public FrictionCalibrationResult(float frictionCoefficient, int sampleCount, float lateralAccelerationRmse)
        {
            FrictionCoefficient = frictionCoefficient;
            SampleCount = sampleCount;
            LateralAccelerationRmse = lateralAccelerationRmse;
        }
    }

    /// <summary>
    /// Robustly estimates friction from steady-state cornering telemetry. The
    /// result can be applied to <see cref="CombinedSlipVehicle.SurfaceFriction"/>.
    /// </summary>
    public static class TelemetryFrictionCalibrator
    {
        const float Gravity = 9.80665f;

        public static FrictionCalibrationResult Estimate(IReadOnlyList<CorneringTelemetrySample> samples, float minimumLateralAcceleration = 0.5f)
        {
            if (samples == null)
                throw new ArgumentNullException(nameof(samples));

            var estimates = new List<float>();
            foreach (var sample in samples)
            {
                var lateralAcceleration = Mathf.Abs(sample.Speed * sample.YawRateRadiansPerSecond);
                if (lateralAcceleration < minimumLateralAcceleration)
                    continue;
                var bank = sample.BankDegrees * Mathf.Deg2Rad;
                var denominator = Gravity * Mathf.Max(0.01f, Mathf.Cos(bank));
                estimates.Add(Mathf.Max(0f, (lateralAcceleration - Gravity * Mathf.Abs(Mathf.Sin(bank))) / denominator));
            }
            if (estimates.Count == 0)
                throw new ArgumentException("Telemetry contains no usable cornering samples.", nameof(samples));

            estimates.Sort();
            var friction = estimates[estimates.Count / 2];
            var squaredError = 0f;
            foreach (var sample in samples)
            {
                var bank = sample.BankDegrees * Mathf.Deg2Rad;
                var predicted = Gravity * (friction * Mathf.Cos(bank) + Mathf.Abs(Mathf.Sin(bank)));
                var measured = Mathf.Abs(sample.Speed * sample.YawRateRadiansPerSecond);
                squaredError += (predicted - measured) * (predicted - measured);
            }
            return new FrictionCalibrationResult(friction, estimates.Count, Mathf.Sqrt(squaredError / samples.Count));
        }

        public static void Apply(CombinedSlipVehicle vehicle, FrictionCalibrationResult result)
        {
            if (vehicle == null)
                throw new ArgumentNullException(nameof(vehicle));
            vehicle.SurfaceFriction = result.FrictionCoefficient;
        }
    }
}
