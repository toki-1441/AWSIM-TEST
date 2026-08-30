// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PhysicalAi.Evaluation
{
    /// <summary>Persists holdout metrics as a machine-readable JSON artifact after each full suite.</summary>
    public sealed class HoldoutEvaluationReportWriter : MonoBehaviour
    {
        [SerializeField] HoldoutEvaluationRunner _runner;
        [SerializeField] string _holdoutFileName = "physical-ai-holdout-report.json";
        [SerializeField] string _trainingFileName = "physical-ai-training-report.json";

        [Serializable]
        sealed class ReportDocument
        {
            public int episodeCount;
            public float completionRate;
            public float meanCompletedLapTimeSeconds;
            public float collisionRate;
            public float offCourseRate;
            public float blindCornerHazardCollisionRate;
            public float opponentCollisionRate;
            public float timeoutRate;
            public EpisodeDocument[] episodes;
        }

        [Serializable]
        sealed class EpisodeDocument
        {
            public int courseSeed;
            public bool completed;
            public float lapTimeSeconds;
            public int collisionCount;
            public int offCourseCount;
            public int blindCornerHazardCollisionCount;
            public int opponentCollisionCount;
            public int timeoutCount;
            public float progressMeters;
            public float distanceFromCenterline;
        }

        void OnEnable()
        {
            if (_runner != null)
                _runner.EvaluationCompleted += Write;
        }

        void OnDisable()
        {
            if (_runner != null)
                _runner.EvaluationCompleted -= Write;
        }

        public void Write(HoldoutEvaluationReport report)
        {
            var directory = GetCommandLineArg("-physicalAiReportDirectory");
            if (string.IsNullOrEmpty(directory))
                directory = Path.Combine(Application.persistentDataPath, "PhysicalAi");
            Directory.CreateDirectory(directory);
            var fileName = _runner != null && _runner.IsTrainingDistributionSuite ? _trainingFileName : _holdoutFileName;
            File.WriteAllText(Path.Combine(directory, fileName), Serialize(report, _runner?.Results));
        }

        public static string Serialize(HoldoutEvaluationReport report)
        {
            return Serialize(report, null);
        }

        public static string Serialize(HoldoutEvaluationReport report, IReadOnlyList<CourseEpisodeResult> episodes)
        {
            return JsonUtility.ToJson(new ReportDocument
            {
                episodeCount = report.EpisodeCount,
                completionRate = report.CompletionRate,
                meanCompletedLapTimeSeconds = float.IsPositiveInfinity(report.MeanCompletedLapTimeSeconds) ? -1f : report.MeanCompletedLapTimeSeconds,
                collisionRate = report.CollisionRate,
                offCourseRate = report.OffCourseRate,
                blindCornerHazardCollisionRate = report.BlindCornerHazardCollisionRate,
                opponentCollisionRate = report.OpponentCollisionRate,
                timeoutRate = report.TimeoutRate,
                episodes = episodes?.Select(result => new EpisodeDocument
                {
                    courseSeed = result.CourseSeed,
                    completed = result.Completed,
                    lapTimeSeconds = result.LapTimeSeconds,
                    collisionCount = result.CollisionCount,
                    offCourseCount = result.OffCourseCount,
                    blindCornerHazardCollisionCount = result.BlindCornerHazardCollisionCount,
                    opponentCollisionCount = result.OpponentCollisionCount,
                    timeoutCount = result.TimeoutCount,
                    progressMeters = result.ProgressMeters,
                    distanceFromCenterline = result.DistanceFromCenterline
                }).ToArray() ?? Array.Empty<EpisodeDocument>()
            }, true);
        }

        static string GetCommandLineArg(string parameter)
        {
            var arguments = Environment.GetCommandLineArgs();
            for (var index = 0; index + 1 < arguments.Length; index++)
                if (arguments[index] == parameter) return arguments[index + 1];
            return null;
        }
    }
}
