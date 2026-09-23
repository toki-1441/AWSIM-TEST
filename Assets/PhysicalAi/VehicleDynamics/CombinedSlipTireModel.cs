// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using System;
using UnityEngine;

namespace PhysicalAi.VehicleDynamics
{
    /// <summary>
    /// Parameters for one Pacejka magic-formula force curve. D is expressed as
    /// a fraction of the available friction force (normal load times mu).
    /// </summary>
    [Serializable]
    public struct PacejkaCoefficients
    {
        [Min(0.001f)] public float B;
        [Min(0.001f)] public float C;
        [Min(0.001f)] public float D;
        public float E;

        public PacejkaCoefficients(float b, float c, float d, float e)
        {
            B = b;
            C = c;
            D = d;
            E = e;
        }
    }

    /// <summary>Longitudinal and lateral tire force at the wheel contact patch.</summary>
    public readonly struct TireForce
    {
        public readonly float Longitudinal;
        public readonly float Lateral;
        public readonly float Utilization;

        public TireForce(float longitudinal, float lateral, float utilization)
        {
            Longitudinal = longitudinal;
            Lateral = lateral;
            Utilization = utilization;
        }
    }

    /// <summary>
    /// Combined-slip Pacejka tire model.  It first evaluates the independent
    /// longitudinal and lateral curves, then enforces a friction circle so the
    /// resultant force can never exceed the available road friction.
    /// </summary>
    public static class CombinedSlipTireModel
    {
        public static readonly PacejkaCoefficients DefaultLongitudinal = new PacejkaCoefficients(10f, 1.9f, 1f, 0.97f);
        /// <summary>
        /// Lateral curve for each axle. B is applied to a slip angle in RADIANS, so it
        /// sets where the force peaks: alpha_peak = 1.657 / B for this C and E. The
        /// previous shared B of 5.2 put the peak at 18.1 degrees, so the car had to crab
        /// more than twice as far as a real tire to reach the grip the friction circle
        /// already assumed, and every corner entry felt like a slide. Rubber peaks
        /// between roughly 4 and 12 degrees at any scale; these put the front at 8.0 and
        /// the rear at 6.9, the front softer so the vehicle understeers rather than
        /// spinning. Both are estimates pending a skidpad identification -- see
        /// docs/physical-ai-vehicle-model.md.
        /// </summary>
        public static readonly PacejkaCoefficients DefaultLateralFront = new PacejkaCoefficients(11.9f, 1.3f, 1f, -1.6f);
        public static readonly PacejkaCoefficients DefaultLateralRear = new PacejkaCoefficients(13.8f, 1.3f, 1f, -1.6f);

        public static TireForce Evaluate(
            float longitudinalSlip,
            float slipAngleRadians,
            float normalLoad,
            float frictionCoefficient,
            PacejkaCoefficients longitudinalCoefficients,
            PacejkaCoefficients lateralCoefficients)
        {
            var availableForce = Mathf.Max(0f, normalLoad) * Mathf.Max(0f, frictionCoefficient);
            if (availableForce <= 0f)
                return new TireForce(0f, 0f, 0f);

            var longitudinalPeak = availableForce * Mathf.Max(0.001f, longitudinalCoefficients.D);
            var lateralPeak = availableForce * Mathf.Max(0.001f, lateralCoefficients.D);
            var longitudinal = MagicFormula(longitudinalSlip, longitudinalPeak, longitudinalCoefficients);
            // Positive lateral velocity means the contact patch is moving right;
            // tire force must therefore point left.
            var lateral = -MagicFormula(slipAngleRadians, lateralPeak, lateralCoefficients);

            var utilization = Mathf.Sqrt(
                Mathf.Pow(longitudinal / availableForce, 2f) +
                Mathf.Pow(lateral / availableForce, 2f));

            if (utilization > 1f)
            {
                longitudinal /= utilization;
                lateral /= utilization;
                utilization = 1f;
            }

            return new TireForce(longitudinal, lateral, utilization);
        }

        static float MagicFormula(float slip, float peakForce, PacejkaCoefficients coefficients)
        {
            var bx = coefficients.B * slip;
            return peakForce * Mathf.Sin(coefficients.C * Mathf.Atan(bx - coefficients.E * (bx - Mathf.Atan(bx))));
        }
    }
}
