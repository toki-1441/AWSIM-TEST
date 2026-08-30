// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;

namespace PhysicalAi.Evaluation
{
    /// <summary>Stateful, deterministic collector for one or more holdout episodes.</summary>
    public sealed class HoldoutEvaluationSession
    {
        readonly List<CourseEpisodeResult> _results = new List<CourseEpisodeResult>();
        int _activeSeed;
        float _startTime;
        bool _active;
        readonly bool _requireHoldoutSeed;

        public HoldoutEvaluationSession(bool requireHoldoutSeed = true) => _requireHoldoutSeed = requireHoldoutSeed;

        public IReadOnlyList<CourseEpisodeResult> Results => _results;
        public bool HasActiveEpisode => _active;

        public void BeginEpisode(int seed, float startTimeSeconds)
        {
            if (_requireHoldoutSeed && !HoldoutCourseEvaluator.IsHoldoutSeed(seed))
                throw new ArgumentException("Evaluation episodes must use a fixed holdout seed.", nameof(seed));
            if (_active)
                throw new InvalidOperationException("Complete the active episode before starting another one.");
            _activeSeed = seed;
            _startTime = startTimeSeconds;
            _active = true;
        }

        public CourseEpisodeResult CompleteEpisode(bool completed, bool collision, bool offCourse, float endTimeSeconds, bool blindCornerHazardCollision = false, bool opponentCollision = false, bool timedOut = false, float progressMeters = 0f, float distanceFromCenterline = 0f)
        {
            if (!_active)
                throw new InvalidOperationException("No active holdout episode.");
            var result = new CourseEpisodeResult
            {
                CourseSeed = _activeSeed,
                Completed = completed,
                LapTimeSeconds = Math.Max(0f, endTimeSeconds - _startTime),
                CollisionCount = collision ? 1 : 0,
                OffCourseCount = offCourse ? 1 : 0,
                BlindCornerHazardCollisionCount = blindCornerHazardCollision ? 1 : 0,
                OpponentCollisionCount = opponentCollision ? 1 : 0
                , TimeoutCount = timedOut ? 1 : 0,
                ProgressMeters = Math.Max(0f, progressMeters),
                DistanceFromCenterline = Math.Max(0f, distanceFromCenterline)
            };
            _results.Add(result);
            _active = false;
            return result;
        }

        public HoldoutEvaluationReport BuildReport() => HoldoutCourseEvaluator.Evaluate(_results);
    }
}
