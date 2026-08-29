// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Linq;

namespace PhysicalAi.Evaluation
{
    /// <summary>Fixed unseen course seeds. Keep these out of the training seed range.</summary>
    public static class HoldoutCourseSuite
    {
        public static readonly int[] Seeds = { 1009, 2027, 3037, 4051, 5099, 6073, 7013, 8089 };
    }

    [Serializable]
    public struct CourseEpisodeResult
    {
        public int CourseSeed;
        public bool Completed;
        public float LapTimeSeconds;
        public int CollisionCount;
        public int OffCourseCount;
    }

    public readonly struct HoldoutEvaluationReport
    {
        public readonly int EpisodeCount;
        public readonly float CompletionRate;
        public readonly float MeanCompletedLapTimeSeconds;
        public readonly float CollisionRate;
        public readonly float OffCourseRate;

        public HoldoutEvaluationReport(int episodeCount, float completionRate, float meanCompletedLapTimeSeconds, float collisionRate, float offCourseRate)
        {
            EpisodeCount = episodeCount;
            CompletionRate = completionRate;
            MeanCompletedLapTimeSeconds = meanCompletedLapTimeSeconds;
            CollisionRate = collisionRate;
            OffCourseRate = offCourseRate;
        }
    }

    public static class HoldoutCourseEvaluator
    {
        public static HoldoutEvaluationReport Evaluate(IReadOnlyList<CourseEpisodeResult> results)
        {
            if (results == null || results.Count == 0)
                throw new ArgumentException("At least one episode result is required.", nameof(results));

            var completed = results.Where(result => result.Completed).ToArray();
            return new HoldoutEvaluationReport(
                results.Count,
                (float)completed.Length / results.Count,
                completed.Length == 0 ? float.PositiveInfinity : completed.Average(result => result.LapTimeSeconds),
                (float)results.Count(result => result.CollisionCount > 0) / results.Count,
                (float)results.Count(result => result.OffCourseCount > 0) / results.Count);
        }

        public static bool IsHoldoutSeed(int seed) => Array.IndexOf(HoldoutCourseSuite.Seeds, seed) >= 0;
    }
}
