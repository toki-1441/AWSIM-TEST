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
        public static readonly int[] TrainingEvaluationSeeds = { 101, 211, 307, 419, 503, 607, 709, 811 };

        /// <summary>
        /// Generates a reproducible training-only seed batch.  These seeds are
        /// deliberately separate from both the fixed in-distribution evaluation
        /// set and the unseen holdout set, so repeated rollouts do not train on
        /// the metric being reported.
        /// </summary>
        public static int[] TrainingRolloutSeeds(int round, int count)
        {
            if (round < 0)
                throw new ArgumentOutOfRangeException(nameof(round));
            if (count < 1 || count > 999)
                throw new ArgumentOutOfRangeException(nameof(count));

            var seeds = new int[count];
            var first = 10000 + round * 1000;
            for (var index = 0; index < seeds.Length; index++)
                seeds[index] = first + index;
            return seeds;
        }

        /// <summary>Operator seed controls the first episode; resume advances the same training-only sequence.</summary>
        public static int[] OperatorRolloutSeeds(int baseSeed, int completedEpisodes, int count)
        {
            if (baseSeed < 0 || completedEpisodes < 0 || count < 1 || count > 999 ||
                Seeds.Contains(baseSeed) || TrainingEvaluationSeeds.Contains(baseSeed))
                throw new ArgumentException("Choose a nonnegative training seed outside the reserved evaluation seeds, and 1..999 episodes.");
            var output = new List<int>();
            long candidate = baseSeed;
            var skipped = 0;
            while (output.Count < count)
            {
                if (candidate > int.MaxValue) throw new ArgumentException("Training seed sequence exhausted.");
                var seed = (int)candidate++;
                if (Seeds.Contains(seed) || TrainingEvaluationSeeds.Contains(seed)) continue;
                if (skipped++ < completedEpisodes) continue;
                output.Add(seed);
            }
            return output.ToArray();
        }
    }

    [Serializable]
    public struct CourseEpisodeResult
    {
        public int CourseSeed;
        public bool Completed;
        public float LapTimeSeconds;
        public int CollisionCount;
        public int OffCourseCount;
        public int BlindCornerHazardCollisionCount;
        public bool HazardWasSpawned;
        public int OpponentCollisionCount;
        public int EgoOvertakeCount;
        public int OpponentOvertakeCount;
        public int TimeoutCount;
        public float ProgressMeters;
        public float DistanceFromCenterline;
    }

    public readonly struct HoldoutEvaluationReport
    {
        public readonly int EpisodeCount;
        public readonly float CompletionRate;
        public readonly float MeanCompletedLapTimeSeconds;
        public readonly float CollisionRate;
        public readonly float OffCourseRate;
        public readonly float BlindCornerHazardCollisionRate;
        public readonly float HazardEpisodeRate;
        public readonly float OpponentCollisionRate;
        public readonly float EgoOvertakeRate;
        public readonly float OpponentOvertakeRate;
        public readonly float TimeoutRate;

        public HoldoutEvaluationReport(int episodeCount, float completionRate, float meanCompletedLapTimeSeconds, float collisionRate, float offCourseRate, float blindCornerHazardCollisionRate = 0f, float opponentCollisionRate = 0f, float timeoutRate = 0f, float egoOvertakeRate = 0f, float opponentOvertakeRate = 0f, float hazardEpisodeRate = 0f)
        {
            EpisodeCount = episodeCount;
            CompletionRate = completionRate;
            MeanCompletedLapTimeSeconds = meanCompletedLapTimeSeconds;
            CollisionRate = collisionRate;
            OffCourseRate = offCourseRate;
            BlindCornerHazardCollisionRate = blindCornerHazardCollisionRate;
            OpponentCollisionRate = opponentCollisionRate;
            TimeoutRate = timeoutRate;
            EgoOvertakeRate = egoOvertakeRate;
            OpponentOvertakeRate = opponentOvertakeRate;
            HazardEpisodeRate = hazardEpisodeRate;
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
                (float)results.Count(result => result.OffCourseCount > 0) / results.Count,
                (float)results.Count(result => result.BlindCornerHazardCollisionCount > 0) / results.Count,
                (float)results.Count(result => result.OpponentCollisionCount > 0) / results.Count,
                (float)results.Count(result => result.TimeoutCount > 0) / results.Count,
                (float)results.Count(result => result.EgoOvertakeCount > 0) / results.Count,
                (float)results.Count(result => result.OpponentOvertakeCount > 0) / results.Count,
                (float)results.Count(result => result.HazardWasSpawned) / results.Count);
        }

        public static bool IsHoldoutSeed(int seed) => Array.IndexOf(HoldoutCourseSuite.Seeds, seed) >= 0;
    }
}
