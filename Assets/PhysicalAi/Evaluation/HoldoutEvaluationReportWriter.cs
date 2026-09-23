// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using PhysicalAi.Planning;
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
            public string policyLabel;
            public string evaluationScenario;
            public string observationContract;
            public int cameraFramesCaptured;
            public int cameraFramesWithheld;
            public int episodeCount;
            public float completionRate;
            public float meanCompletedLapTimeSeconds;
            public float collisionRate;
            public float offCourseRate;
            public float blindCornerHazardCollisionRate;
            public float hazardEpisodeRate;
            public float opponentCollisionRate;
            public float egoOvertakeRate;
            public float opponentOvertakeRate;
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
            public bool hazardWasSpawned;
            public int opponentCollisionCount;
            public int egoOvertakeCount;
            public int opponentOvertakeCount;
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
            var bridge = FindFirstObjectByType<PythonRlBridge>();
            File.WriteAllText(Path.Combine(directory, fileName), Serialize(report, _runner?.Results, _runner?.PolicyLabel,
                bridge == null ? 0 : bridge.CameraFramesCaptured,
                bridge == null ? 0 : bridge.CameraFramesWithheld,
                _runner?.EvaluationScenarioLabel));
        }

        public static string Serialize(HoldoutEvaluationReport report)
        {
            return Serialize(report, null, null, 0, 0, null);
        }

        public static string Serialize(HoldoutEvaluationReport report, IReadOnlyList<CourseEpisodeResult> episodes)
        {
            return Serialize(report, episodes, null, 0, 0, null);
        }

        /// <summary>
        /// Writes policy provenance with the fixed relative-waypoint observation
        /// contract. Camera-only policies may use the same external command path,
        /// so their checkpoint/run label must be passed via -physicalAiPolicyLabel.
        /// </summary>
        public static string Serialize(HoldoutEvaluationReport report, IReadOnlyList<CourseEpisodeResult> episodes, string policyLabel,
            int cameraFramesCaptured = 0, int cameraFramesWithheld = 0, string evaluationScenario = null)
        {
            return JsonUtility.ToJson(new ReportDocument
            {
                policyLabel = string.IsNullOrEmpty(policyLabel) ? "unspecified" : policyLabel,
                evaluationScenario = string.IsNullOrEmpty(evaluationScenario) ? "unspecified" : evaluationScenario,
                observationContract = "relative-course-waypoints+speed+visibility",
                cameraFramesCaptured = Mathf.Max(0, cameraFramesCaptured),
                cameraFramesWithheld = Mathf.Max(0, cameraFramesWithheld),
                episodeCount = report.EpisodeCount,
                completionRate = report.CompletionRate,
                meanCompletedLapTimeSeconds = float.IsPositiveInfinity(report.MeanCompletedLapTimeSeconds) ? -1f : report.MeanCompletedLapTimeSeconds,
                collisionRate = report.CollisionRate,
                offCourseRate = report.OffCourseRate,
                blindCornerHazardCollisionRate = report.BlindCornerHazardCollisionRate,
                hazardEpisodeRate = report.HazardEpisodeRate,
                opponentCollisionRate = report.OpponentCollisionRate,
                egoOvertakeRate = report.EgoOvertakeRate,
                opponentOvertakeRate = report.OpponentOvertakeRate,
                timeoutRate = report.TimeoutRate,
                episodes = episodes?.Select(result => new EpisodeDocument
                {
                    courseSeed = result.CourseSeed,
                    completed = result.Completed,
                    lapTimeSeconds = result.LapTimeSeconds,
                    collisionCount = result.CollisionCount,
                    offCourseCount = result.OffCourseCount,
                    blindCornerHazardCollisionCount = result.BlindCornerHazardCollisionCount,
                    hazardWasSpawned = result.HazardWasSpawned,
                    opponentCollisionCount = result.OpponentCollisionCount,
                    egoOvertakeCount = result.EgoOvertakeCount,
                    opponentOvertakeCount = result.OpponentOvertakeCount,
                    timeoutCount = result.TimeoutCount,
                    progressMeters = result.ProgressMeters,
                    distanceFromCenterline = result.DistanceFromCenterline
                }).ToArray() ?? Array.Empty<EpisodeDocument>()
            }, true);
        }

        static string GetCommandLineArg(string parameter) => PhysicalAiLaunchArguments.GetValue(parameter);
    }
}
