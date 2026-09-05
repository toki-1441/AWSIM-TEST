// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using UnityEngine;

namespace PhysicalAi.Course
{
    /// <summary>
    /// Deterministically extends the open training road with parameterised straights
    /// and arcs.  A chunk is admitted only when its physical corridor clears the
    /// retained road; this class is deliberately the geometry authority used by both
    /// rendering and collision construction.
    /// </summary>
    public sealed class StreamingCourseState
    {
        // Two 1.2 m roads with 0.04 m walls require 1.24 m between centrelines;
        // retain another centimetre of visible air. This is a swept corridor
        // clearance, not merely a line-segment intersection test.
        const float MinimumNonAdjacentCenterlineSeparation = 1.25f;
        // Nearby cross-sections of one continuous 1.2 m road necessarily overlap.
        // Keep that local swept neighbourhood out of the clearance test, while
        // the separate topological-intersection test below still rejects a true
        // crossing anywhere except a directly shared endpoint.
        const float ConnectionExclusionDistance = 8f;
        const int MaximumChunkAttempts = 128;
        // The clearance test alone allows an open random walk to curl into a
        // cul-de-sac.  Retain an expanding free-space frontier so a later chunk
        // still has legal curved candidates.  This is a candidate-selection
        // constraint, never a straight fallback: every accepted primitive below
        // still contains its sampled arc.
        const float MinimumOutwardProgressFraction = 0.45f;
        const float MinimumOutwardHeadingAlignment = 0.5f;

        readonly List<Vector3> _centerline = new List<Vector3>();
        readonly List<float> _curvature = new List<float>();
        readonly List<float> _safeSpeed = new List<float>();
        CourseGenerationSettings _settings;
        int _seed;
        int _chunkIndex;
        Vector3 _streamOrigin;
        float _endHeadingDegrees;

        public GeneratedCourse Course { get; private set; }
        public int ChunkCount => _chunkIndex;
        /// <summary>-1 for right, +1 for left; every streamed chunk contains exactly one corner.</summary>
        public int LastAppendedCornerDirection { get; private set; }

        public void Reset(int seed, CourseGenerationSettings settings, Vector3 startPosition, float startHeadingDegrees)
        {
            Validate(settings);
            _centerline.Clear();
            _curvature.Clear();
            _safeSpeed.Clear();
            _seed = seed;
            _settings = settings;
            _chunkIndex = 0;
            _streamOrigin = startPosition;
            AppendChunk(startPosition, startHeadingDegrees);
        }

        public int AppendNextChunk()
        {
            if (Course == null || _centerline.Count < 2)
                throw new InvalidOperationException("Reset streaming state before extending it.");
            var end = _centerline[_centerline.Count - 1];
            var firstNewIndex = _centerline.Count;
            AppendChunk(end, _endHeadingDegrees);
            return firstNewIndex;
        }

        public int TrimBefore(int firstRetainedIndex)
        {
            var removed = Mathf.Clamp(firstRetainedIndex, 0, Mathf.Max(0, _centerline.Count - 2));
            if (removed == 0) return 0;
            _centerline.RemoveRange(0, removed);
            _curvature.RemoveRange(0, removed);
            _safeSpeed.RemoveRange(0, removed);
            return removed;
        }

        /// <summary>
        /// Moves every world-space value owned by the streaming generator.  The
        /// candidate frontier must use the same rebased frame as the rendered
        /// course or appending after a floating-origin shift is no longer
        /// translation invariant and can eventually exhaust its retry budget.
        /// </summary>
        public void Translate(Vector3 offset)
        {
            if (Course == null)
                throw new InvalidOperationException("Reset streaming state before translating it.");
            Course.Translate(offset);
            _streamOrigin += offset;
        }

        void AppendChunk(Vector3 startPosition, float startHeadingDegrees)
        {
            GeneratedCourse chunk = null;
            var outwardRejections = 0;
            var intersectionRejections = 0;
            var clearanceRejections = 0;
            var lastGeometryRejection = string.Empty;
            var acceptedEndHeadingDegrees = startHeadingDegrees;
            var acceptedCornerDirection = 0;
            for (var attempt = 0; attempt < MaximumChunkAttempts; attempt++)
            {
                var random = new System.Random(ChunkAttemptSeed(_seed, _chunkIndex, attempt));
                var candidate = CreateCandidateChunk(startPosition, startHeadingDegrees, random,
                    out var candidateEndHeadingDegrees, out var candidateCornerDirection);
                if (!MakesOutwardProgress(candidate, startPosition))
                {
                    outwardRejections++;
                    continue;
                }
                if (ViolatesClearance(candidate, out var intersects, out lastGeometryRejection))
                {
                    if (intersects) intersectionRejections++;
                    else clearanceRejections++;
                    continue;
                }
                chunk = candidate;
                acceptedEndHeadingDegrees = candidateEndHeadingDegrees;
                acceptedCornerDirection = candidateCornerDirection;
                break;
            }

            if (chunk == null)
                throw new InvalidOperationException(
                    $"Could not generate a non-intersecting curved streaming chunk within the retry budget " +
                    $"(outward {outwardRejections}, intersections {intersectionRejections}, clearance {clearanceRejections}; " +
                    $"last {lastGeometryRejection}).");

            var first = _centerline.Count == 0 ? 0 : 1;
            for (var index = first; index < chunk.Centerline.Count; index++)
            {
                _centerline.Add(chunk.Centerline[index]);
                _curvature.Add(chunk.Curvature[index]);
                _safeSpeed.Add(chunk.MaximumSafeSpeed[index]);
            }
            _endHeadingDegrees = acceptedEndHeadingDegrees;
            LastAppendedCornerDirection = acceptedCornerDirection;
            _chunkIndex++;
            Course = new GeneratedCourse(_centerline, _curvature, _safeSpeed);
        }

        GeneratedCourse CreateCandidateChunk(Vector3 startPosition, float headingDegrees, System.Random random,
            out float endHeadingDegrees, out int cornerDirection)
        {
            var points = new List<Vector3>();
            var curvature = new List<float>();
            var safeSpeed = new List<float>();
            var position = startPosition;
            var heading = headingDegrees * Mathf.Deg2Rad;
            var minimumRadius = Mathf.Max(
                _settings.MinimumCornerRadius,
                CoursePhysicsConstraints.MinimumTurnRadius(_settings.DesignSpeed, _settings.FrictionCoefficient, _settings.MaximumBankDegrees));
            var maximumRadius = Mathf.Max(minimumRadius, _settings.MaximumCornerRadius);
            var maximumAngle = Mathf.Clamp(_settings.MaximumCornerAngleDegrees, 20f, 75f);

            AddSample(position, 0f);
            AppendStraight(Mathf.Lerp(_settings.MinimumStraightLength, _settings.MaximumStraightLength, (float)random.NextDouble()));

            var radius = Mathf.Lerp(minimumRadius, maximumRadius, (float)random.NextDouble());
            var signedCurvature = ((random.Next() & 1) == 0 ? -1f : 1f) / radius;
            cornerDirection = signedCurvature < 0f ? -1 : 1;
            var angleDegrees = Mathf.Lerp(20f, maximumAngle, (float)random.NextDouble());
            AppendArc(radius * angleDegrees * Mathf.Deg2Rad, signedCurvature);

            AppendStraight(Mathf.Lerp(_settings.MinimumStraightLength, _settings.MaximumStraightLength, (float)random.NextDouble()));
            endHeadingDegrees = heading * Mathf.Rad2Deg;
            return new GeneratedCourse(points, curvature, safeSpeed);

            void AppendStraight(float length)
            {
                Append(length, 0f);
            }

            void AppendArc(float length, float signedCurvature)
            {
                Append(length, signedCurvature);
            }

            void Append(float length, float signedCurvature)
            {
                // Dividing the primitive evenly avoids a near-zero remainder
                // segment when its length is almost an exact spacing multiple.
                // Such slivers create coincident wall/mesh elements at joins and
                // make a topologically separate segment look like a crossing.
                var stepCount = Mathf.Max(1, Mathf.CeilToInt(length / _settings.SampleSpacing));
                var step = length / stepCount;
                for (var sample = 0; sample < stepCount; sample++)
                {
                    heading += signedCurvature * step;
                    position += new Vector3(Mathf.Sin(heading), 0f, Mathf.Cos(heading)) * step;
                    AddSample(position, signedCurvature);
                }
            }

            void AddSample(Vector3 point, float signedCurvature)
            {
                points.Add(point);
                curvature.Add(signedCurvature);
                safeSpeed.Add(CoursePhysicsConstraints.MaximumSafeSpeed(signedCurvature, _settings.FrictionCoefficient, 0f));
            }
        }

        bool ViolatesClearance(GeneratedCourse candidate, out bool intersects, out string rejection)
        {
            intersects = false;
            rejection = string.Empty;
            var adjacentSamples = Mathf.Max(2, Mathf.CeilToInt(ConnectionExclusionDistance / _settings.SampleSpacing));
            var candidateStart = _centerline.Count == 0 ? 0 : _centerline.Count - 1;

            // Compare every new segment with retained history.  Segments that are
            // adjacent along the one continuous centreline share an endpoint and are
            // intentionally excluded; all other close approaches are forbidden.
            for (var candidateIndex = 0; candidateIndex < candidate.Centerline.Count - 1; candidateIndex++)
            {
                var candidateGlobalIndex = candidateStart + candidateIndex;
                for (var historyIndex = 0; historyIndex < _centerline.Count - 1; historyIndex++)
                {
                    // Only directly connected segments may share an endpoint.
                    // Detect a true crossing even inside the local clearance
                    // exemption used for one continuous swept corridor.
                    if (Mathf.Abs(candidateGlobalIndex - historyIndex) > 1 &&
                        PlanarSegmentDistanceSquared(candidate.Centerline[candidateIndex], candidate.Centerline[candidateIndex + 1], _centerline[historyIndex], _centerline[historyIndex + 1]) <= 0.00000001f)
                    {
                        intersects = true;
                        rejection = $"history segments {historyIndex}/{candidateGlobalIndex}";
                        return true;
                    }
                    if (Mathf.Abs(candidateGlobalIndex - historyIndex) <= adjacentSamples)
                        continue;
                    if (PlanarSegmentDistanceSquared(candidate.Centerline[candidateIndex], candidate.Centerline[candidateIndex + 1], _centerline[historyIndex], _centerline[historyIndex + 1]) < MinimumNonAdjacentCenterlineSeparation * MinimumNonAdjacentCenterlineSeparation)
                    {
                        rejection = $"history clearance {historyIndex}/{candidateGlobalIndex}";
                        return true;
                    }
                }
            }

            // A single primitive can be long enough to bend its exit back toward
            // its entry.  It is subject to exactly the same road-and-wall envelope
            // clearance as retained history.  Exempt only the locally continuous
            // connection along the same centreline, not every pair in the chunk.
            for (var later = 0; later < candidate.Centerline.Count - 1; later++)
            {
                for (var earlier = 0; earlier < later; earlier++)
                {
                    if (later - earlier > 1 &&
                        PlanarSegmentDistanceSquared(candidate.Centerline[earlier], candidate.Centerline[earlier + 1], candidate.Centerline[later], candidate.Centerline[later + 1]) <= 0.00000001f)
                    {
                        intersects = true;
                        rejection = $"candidate segments {earlier}/{later}";
                        return true;
                    }
                    if (later - earlier <= adjacentSamples)
                        continue;
                    if (PlanarSegmentDistanceSquared(candidate.Centerline[earlier], candidate.Centerline[earlier + 1], candidate.Centerline[later], candidate.Centerline[later + 1]) < MinimumNonAdjacentCenterlineSeparation * MinimumNonAdjacentCenterlineSeparation)
                    {
                        rejection = $"candidate clearance {earlier}/{later}";
                        return true;
                    }
                }
            }
            return false;
        }

        bool MakesOutwardProgress(GeneratedCourse candidate, Vector3 startPosition)
        {
            if (_centerline.Count == 0 || candidate.Centerline.Count < 2)
                return true;

            var end = candidate.Centerline[candidate.Centerline.Count - 1];
            var direction = end - candidate.Centerline[candidate.Centerline.Count - 2];
            direction.y = 0f;
            var startOffset = startPosition - _streamOrigin;
            var endOffset = end - _streamOrigin;
            startOffset.y = 0f;
            endOffset.y = 0f;
            var candidateLength = 0f;
            for (var index = 1; index < candidate.Centerline.Count; index++)
                candidateLength += Vector3.Distance(candidate.Centerline[index - 1], candidate.Centerline[index]);

            if (endOffset.magnitude < startOffset.magnitude + candidateLength * MinimumOutwardProgressFraction)
                return false;
            return endOffset.sqrMagnitude < 0.0001f ||
                   Vector3.Dot(direction.normalized, endOffset.normalized) >= MinimumOutwardHeadingAlignment;
        }

        /// <summary>Squared distance between two finite segments projected onto the XZ course plane.</summary>
        public static float PlanarSegmentDistanceSquared(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            var p = new Vector2(a.x, a.z);
            var r = new Vector2(b.x - a.x, b.z - a.z);
            var q = new Vector2(c.x, c.z);
            var s = new Vector2(d.x - c.x, d.z - c.z);
            var cross = Cross(r, s);
            var qMinusP = q - p;
            if (Mathf.Abs(cross) > 0.000001f)
            {
                var t = Cross(qMinusP, s) / cross;
                var u = Cross(qMinusP, r) / cross;
                if (t >= 0f && t <= 1f && u >= 0f && u <= 1f)
                    return 0f;
            }
            return Mathf.Min(
                Mathf.Min(PointSegmentDistanceSquared(p, q, q + s), PointSegmentDistanceSquared(p + r, q, q + s)),
                Mathf.Min(PointSegmentDistanceSquared(q, p, p + r), PointSegmentDistanceSquared(q + s, p, p + r)));
        }

        static float PointSegmentDistanceSquared(Vector2 point, Vector2 start, Vector2 end)
        {
            var delta = end - start;
            var denominator = delta.sqrMagnitude;
            if (denominator < 0.000001f)
                return (point - start).sqrMagnitude;
            var t = Mathf.Clamp01(Vector2.Dot(point - start, delta) / denominator);
            return (point - (start + delta * t)).sqrMagnitude;
        }

        static float Cross(Vector2 left, Vector2 right) => left.x * right.y - left.y * right.x;

        static int ChunkAttemptSeed(int seed, int chunkIndex, int attempt)
        {
            unchecked
            {
                return seed * 486187739 + chunkIndex * 16777619 + attempt * 374761393;
            }
        }

        static void Validate(CourseGenerationSettings settings)
        {
            if (settings.SampleSpacing <= 0f || settings.MinimumStraightLength <= 0f ||
                settings.MaximumStraightLength < settings.MinimumStraightLength ||
                settings.MinimumCornerRadius <= 0f || settings.MaximumCornerRadius < settings.MinimumCornerRadius ||
                settings.MaximumCornerAngleDegrees < 20f)
                throw new ArgumentException("Invalid streaming course generation settings.");
        }
    }
}
