// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using UnityEngine;

namespace PhysicalAi.Course
{
    /// <summary>Deterministically extends an open training road in independently generated chunks.</summary>
    public sealed class StreamingCourseState
    {
        readonly List<Vector3> _centerline = new List<Vector3>();
        readonly List<float> _curvature = new List<float>();
        readonly List<float> _safeSpeed = new List<float>();
        CourseGenerationSettings _settings;
        int _seed;
        int _chunkIndex;

        public GeneratedCourse Course { get; private set; }
        public int ChunkCount => _chunkIndex;

        public void Reset(int seed, CourseGenerationSettings settings, Vector3 startPosition, float startHeadingDegrees)
        {
            _centerline.Clear();
            _curvature.Clear();
            _safeSpeed.Clear();
            _seed = seed;
            _settings = settings;
            _chunkIndex = 0;
            AppendChunk(startPosition, startHeadingDegrees);
        }

        public int AppendNextChunk()
        {
            if (Course == null || _centerline.Count < 2)
                throw new InvalidOperationException("Reset streaming state before extending it.");
            var end = _centerline[_centerline.Count - 1];
            var previous = _centerline[_centerline.Count - 2];
            var heading = Mathf.Atan2(end.x - previous.x, end.z - previous.z) * Mathf.Rad2Deg;
            var firstNewIndex = _centerline.Count;
            AppendChunk(end, heading);
            return firstNewIndex;
        }

        void AppendChunk(Vector3 startPosition, float startHeadingDegrees)
        {
            var chunkSettings = _settings;
            chunkSettings.SegmentCount = Mathf.Max(2, _settings.SegmentCount / 3);
            var chunkSeed = unchecked(_seed + _chunkIndex * 7919);
            var chunk = PhysicsConstrainedCourseGenerator.Generate(chunkSeed, chunkSettings, startPosition, startHeadingDegrees);
            var first = _centerline.Count == 0 ? 0 : 1;
            for (var index = first; index < chunk.Centerline.Count; index++)
            {
                _centerline.Add(chunk.Centerline[index]);
                _curvature.Add(chunk.Curvature[index]);
                _safeSpeed.Add(chunk.MaximumSafeSpeed[index]);
            }
            _chunkIndex++;
            Course = new GeneratedCourse(_centerline, _curvature, _safeSpeed);
        }
    }
}
