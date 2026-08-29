// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using UnityEngine;

namespace PhysicalAi.Course
{
    /// <summary>Fixed-seed, closed MAGP-envelope circuits kept separate from the open training distribution.</summary>
    public static class ClosedHoldoutCourseGenerator
    {
        static readonly float[] LongStraights = { 8f, 10f, 12f, 14f };
        static readonly float[] ShortStraights = { 2.5f, 3f, 3.5f, 4f };
        static readonly float[] CornerRadii = { 1.4f, 1.6f, 1.8f, 2f };

        public static GeneratedCourse Generate(int seed, CourseGenerationSettings settings, Vector3 startPosition, float startHeadingDegrees)
        {
            if (settings.SampleSpacing <= 0f)
                throw new ArgumentException("Sample spacing must be positive.", nameof(settings));
            var variant = Mathf.Abs(seed) % LongStraights.Length;
            var minimumPhysicsRadius = CoursePhysicsConstraints.MinimumTurnRadius(settings.DesignSpeed, settings.FrictionCoefficient, settings.MaximumBankDegrees);
            var radius = Mathf.Max(CornerRadii[variant], minimumPhysicsRadius);
            var points = new List<Vector3>();
            var curvatures = new List<float>();
            var safeSpeeds = new List<float>();
            var position = startPosition;
            var heading = startHeadingDegrees * Mathf.Deg2Rad;

            Add(position, 0f);
            for (var side = 0; side < 4; side++)
            {
                Append(LongStraights[variant % LongStraights.Length] * (side % 2 == 0 ? 1f : 0f) + ShortStraights[variant] * (side % 2 == 0 ? 0f : 1f), 0f);
                Append(radius * Mathf.PI * 0.5f, 1f / radius);
            }
            // The final sampled arc is within one sample step of the initial pose; explicitly close
            // it so lap completion and boundary construction share a watertight centerline.
            if (Vector3.Distance(position, points[0]) > 0.0001f)
            {
                points.Add(points[0]);
                curvatures.Add(0f);
                safeSpeeds.Add(float.PositiveInfinity);
            }
            return new GeneratedCourse(points, curvatures, safeSpeeds, true);

            void Append(float length, float curvature)
            {
                var travelled = 0f;
                while (travelled < length - 0.00001f)
                {
                    var step = Mathf.Min(settings.SampleSpacing, length - travelled);
                    heading += curvature * step;
                    position += new Vector3(Mathf.Sin(heading), 0f, Mathf.Cos(heading)) * step;
                    travelled += step;
                    Add(position, curvature);
                }
            }

            void Add(Vector3 point, float curvature)
            {
                points.Add(point);
                curvatures.Add(curvature);
                safeSpeeds.Add(CoursePhysicsConstraints.MaximumSafeSpeed(curvature, settings.FrictionCoefficient, 0f));
            }
        }
    }
}
