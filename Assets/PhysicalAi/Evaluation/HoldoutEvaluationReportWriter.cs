// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;
using UnityEngine;

namespace PhysicalAi.Evaluation
{
    /// <summary>Persists holdout metrics as a machine-readable JSON artifact after each full suite.</summary>
    public sealed class HoldoutEvaluationReportWriter : MonoBehaviour
    {
        [SerializeField] HoldoutEvaluationRunner _runner;
        [SerializeField] string _fileName = "physical-ai-holdout-report.json";

        [Serializable]
        sealed class ReportDocument
        {
            public int episodeCount;
            public float completionRate;
            public float meanCompletedLapTimeSeconds;
            public float collisionRate;
            public float offCourseRate;
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
            var directory = Path.Combine(Application.persistentDataPath, "PhysicalAi");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, _fileName), Serialize(report));
        }

        public static string Serialize(HoldoutEvaluationReport report)
        {
            return JsonUtility.ToJson(new ReportDocument
            {
                episodeCount = report.EpisodeCount,
                completionRate = report.CompletionRate,
                meanCompletedLapTimeSeconds = float.IsPositiveInfinity(report.MeanCompletedLapTimeSeconds) ? -1f : report.MeanCompletedLapTimeSeconds,
                collisionRate = report.CollisionRate,
                offCourseRate = report.OffCourseRate
            }, true);
        }
    }
}
