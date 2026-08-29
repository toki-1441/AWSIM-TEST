// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using UnityEngine;

namespace PhysicalAi.Course
{
    [Serializable]
    public struct CourseGenerationSettings
    {
        [Min(0.01f)] public float SampleSpacing;
        [Min(1)] public int SegmentCount;
        [Min(0.01f)] public float MinimumStraightLength;
        [Min(0.01f)] public float MaximumStraightLength;
        [Min(0.01f)] public float MinimumCornerRadius;
        [Min(0.01f)] public float MaximumCornerRadius;
        [Min(1f)] public float MaximumCornerAngleDegrees;
        [Min(0f)] public float MaximumBankDegrees;
        [Min(0.01f)] public float FrictionCoefficient;
        [Min(0f)] public float DesignSpeed;

        public static CourseGenerationSettings Default => new CourseGenerationSettings
        {
            SampleSpacing = 0.1f,
            SegmentCount = 12,
            MinimumStraightLength = 2f,
            MaximumStraightLength = 8f,
            MinimumCornerRadius = 1.4f,
            MaximumCornerRadius = 4f,
            MaximumCornerAngleDegrees = 110f,
            MaximumBankDegrees = 0f,
            FrictionCoefficient = 0.9f,
            DesignSpeed = 3.5f
        };
    }

    public sealed class GeneratedCourse
    {
        readonly List<Vector3> _centerline;
        public IReadOnlyList<Vector3> Centerline { get; }
        public IReadOnlyList<float> Curvature { get; }
        public IReadOnlyList<float> MaximumSafeSpeed { get; }
        public bool IsClosedLoop { get; }

        public GeneratedCourse(List<Vector3> centerline, List<float> curvature, List<float> maximumSafeSpeed, bool isClosedLoop = false)
        {
            _centerline = centerline ?? throw new ArgumentNullException(nameof(centerline));
            Centerline = centerline;
            Curvature = curvature;
            MaximumSafeSpeed = maximumSafeSpeed;
            IsClosedLoop = isClosedLoop;
        }

        /// <summary>Moves the world-space course representation without changing its geometry.</summary>
        public void Translate(Vector3 offset)
        {
            for (var index = 0; index < _centerline.Count; index++)
                _centerline[index] += offset;
        }
    }

    /// <summary>Physical feasibility checks shared by course generation and reward design.</summary>
    public static class CoursePhysicsConstraints
    {
        const float Gravity = 9.80665f;

        public static float MaximumSafeSpeed(float curvature, float frictionCoefficient, float bankDegrees)
        {
            if (Mathf.Abs(curvature) < 0.00001f)
                return float.PositiveInfinity;

            var bankRadians = bankDegrees * Mathf.Deg2Rad;
            var lateralAcceleration = Gravity * (Mathf.Max(0f, frictionCoefficient) * Mathf.Cos(bankRadians) + Mathf.Abs(Mathf.Sin(bankRadians)));
            return Mathf.Sqrt(Mathf.Max(0f, lateralAcceleration / Mathf.Abs(curvature)));
        }

        public static float MinimumTurnRadius(float designSpeed, float frictionCoefficient, float maximumBankDegrees)
        {
            var bankRadians = maximumBankDegrees * Mathf.Deg2Rad;
            var lateralAcceleration = Gravity * (Mathf.Max(0f, frictionCoefficient) * Mathf.Cos(bankRadians) + Mathf.Abs(Mathf.Sin(bankRadians)));
            return lateralAcceleration <= 0f ? float.PositiveInfinity : designSpeed * designSpeed / lateralAcceleration;
        }

        public static bool IsTraversable(float speed, float curvature, float frictionCoefficient, float bankDegrees)
        {
            return speed <= MaximumSafeSpeed(curvature, frictionCoefficient, bankDegrees) + 0.001f;
        }
    }

    /// <summary>
    /// Deterministic centerline generator. Every generated arc respects the
    /// design-speed constraint, so impossible curvature is never sampled into
    /// the training distribution.
    /// </summary>
    public static class PhysicsConstrainedCourseGenerator
    {
        public static GeneratedCourse Generate(int seed, CourseGenerationSettings settings, Vector3 startPosition, float startHeadingDegrees)
        {
            Validate(settings);
            var random = new System.Random(seed);
            var points = new List<Vector3>();
            var curvatures = new List<float>();
            var safeSpeeds = new List<float>();
            var position = startPosition;
            var heading = startHeadingDegrees * Mathf.Deg2Rad;
            var minimumRadius = Mathf.Max(settings.MinimumCornerRadius, CoursePhysicsConstraints.MinimumTurnRadius(settings.DesignSpeed, settings.FrictionCoefficient, settings.MaximumBankDegrees));

            AddSample(position, 0f, 0f);
            for (var segment = 0; segment < settings.SegmentCount; segment++)
            {
                var isCorner = segment % 2 == 1;
                var length = isCorner
                    ? minimumRadius * Mathf.Deg2Rad * Mathf.Lerp(20f, settings.MaximumCornerAngleDegrees, (float)random.NextDouble())
                    : Mathf.Lerp(settings.MinimumStraightLength, settings.MaximumStraightLength, (float)random.NextDouble());
                var curvature = 0f;
                var bank = 0f;
                if (isCorner)
                {
                    var radius = Mathf.Lerp(minimumRadius, Mathf.Max(minimumRadius, settings.MaximumCornerRadius), (float)random.NextDouble());
                    curvature = ((random.Next() & 1) == 0 ? -1f : 1f) / radius;
                    bank = Mathf.Sign(curvature) * Mathf.Lerp(0f, settings.MaximumBankDegrees, (float)random.NextDouble());
                    length = radius * Mathf.Deg2Rad * Mathf.Lerp(20f, settings.MaximumCornerAngleDegrees, (float)random.NextDouble());
                }

                var travelled = 0f;
                while (travelled < length)
                {
                    var step = Mathf.Min(settings.SampleSpacing, length - travelled);
                    heading += curvature * step;
                    position += new Vector3(Mathf.Sin(heading), 0f, Mathf.Cos(heading)) * step;
                    travelled += step;
                    AddSample(position, curvature, bank);
                }
            }

            return new GeneratedCourse(points, curvatures, safeSpeeds);

            void AddSample(Vector3 point, float curvature, float bank)
            {
                points.Add(point);
                curvatures.Add(curvature);
                safeSpeeds.Add(CoursePhysicsConstraints.MaximumSafeSpeed(curvature, settings.FrictionCoefficient, bank));
            }
        }

        static void Validate(CourseGenerationSettings settings)
        {
            if (settings.SampleSpacing <= 0f || settings.SegmentCount < 1 || settings.MaximumStraightLength < settings.MinimumStraightLength || settings.MaximumCornerRadius < settings.MinimumCornerRadius)
                throw new ArgumentException("Invalid course generation settings.");
        }
    }
}
