// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using PhysicalAi.Planning;
using UnityEngine;

namespace PhysicalAi.Evaluation
{
    /// <summary>Runs the fixed unseen course suite serially and emits a comparable evaluation report.</summary>
    public sealed class HoldoutEvaluationRunner : MonoBehaviour
    {
        [SerializeField] PhysicalAiEpisodeOrchestrator _orchestrator;
        [SerializeField] bool _runOnStart;
        [SerializeField, Min(1f)] float _maximumEpisodeSeconds = 120f;

        HoldoutEvaluationSession _session;
        int _nextSeedIndex;
        IReadOnlyList<int> _seeds;
        float _episodeStartTime;
        bool _timedOut;
        bool _trainingDistribution;
        string _policyLabel;
        PythonRlBridge _bridge;
        bool _cancelled;
        public event Action<HoldoutEvaluationReport> EvaluationCompleted;
        /// <summary>Raised after an episode is measured and before the next seed is reset.</summary>
        public event Action<CourseEpisodeResult> EpisodeMeasured;
        public bool IsTrainingDistributionSuite => _trainingDistribution;
        /// <summary>Policy identity captured before the first evaluated reset.</summary>
        public string PolicyLabel => _policyLabel ?? "unknown";
        public string EvaluationScenarioLabel { get; private set; } = "unconfigured";
        public IReadOnlyList<CourseEpisodeResult> Results => _session?.Results ?? Array.Empty<CourseEpisodeResult>();

        void OnEnable()
        {
            _bridge = GetComponent<PythonRlBridge>();
            if (_bridge != null)
            {
                _bridge.TrainingRequested += RunTrainingRollouts;
                _bridge.TrainingCancelled += CancelSuite;
            }
            if (_orchestrator != null)
                _orchestrator.EpisodeFinished += OnEpisodeFinished;
        }

        IEnumerator Start()
        {
            // The orchestrator also initializes in Start.  Evaluation must reset
            // its finite course after that initialization, otherwise Unity's
            // unspecified Start ordering can restore the streaming training map.
            yield return null;
            // Managed training starts only after the trainer handshake; the operator can restart/resume it later.
            if (PhysicalAiLaunchArguments.GetValue("-physicalAiRlManaged") == "true") yield break;
            if (PhysicalAiLaunchArguments.HasFlag("-physicalAiRlPort") && (_bridge == null || !_bridge.IsConnected)) yield break;
            // Player builds can run a reproducible suite without editing the scene:
            // -physicalAiEvaluation holdout | training.
            var maximumSeconds = GetCommandLineArg("-physicalAiMaximumEpisodeSeconds");
            if (float.TryParse(maximumSeconds, out var requestedMaximum) && requestedMaximum > 0f)
                _maximumEpisodeSeconds = requestedMaximum;
            var evaluation = GetCommandLineArg("-physicalAiEvaluation");
            if (evaluation == "holdout")
                RunSuite();
            else if (evaluation == "training")
                RunTrainingSuite();
            else if (evaluation == "rollout")
                RunTrainingRollouts();
            else if (_runOnStart)
                RunSuite();
        }

        void OnDisable()
        {
            if (_bridge != null)
            {
                _bridge.TrainingRequested -= RunTrainingRollouts;
                _bridge.TrainingCancelled -= CancelSuite;
            }
            if (_orchestrator != null)
                _orchestrator.EpisodeFinished -= OnEpisodeFinished;
        }

        void Update()
        {
            if (!_cancelled && _session != null && _session.HasActiveEpisode && !_timedOut && Time.time - _episodeStartTime >= _maximumEpisodeSeconds)
            {
                _timedOut = true;
                _orchestrator.EndEpisode(false, false, true);
            }
        }

        public void RunSuite()
        {
            Run(false, HoldoutCourseSuite.Seeds);
        }

        public void RunTrainingSuite()
        {
            Run(true, HoldoutCourseSuite.TrainingEvaluationSeeds);
        }

        /// <summary>Runs a unique, reproducible training batch for one RL round.</summary>
        public void RunTrainingRollouts()
        {
            var round = GetCommandLineInt("-physicalAiTrainingRound", 0);
            var count = GetCommandLineInt("-physicalAiEpisodeCount", HoldoutCourseSuite.TrainingEvaluationSeeds.Length);
            _maximumEpisodeSeconds = PhysicalAiLaunchArguments.GetFloat("-physicalAiMaximumEpisodeSeconds", _maximumEpisodeSeconds);
            Run(true, PhysicalAiLaunchArguments.GetValue("-physicalAiMode") == "rl"
                ? HoldoutCourseSuite.OperatorRolloutSeeds(PhysicalAiLaunchArguments.GetInt("-physicalAiSeed", 1), _bridge?.CompletedEpisodes ?? 0, count)
                : HoldoutCourseSuite.TrainingRolloutSeeds(round, count));
        }

        public void CancelSuite() { _cancelled = true; StopAllCoroutines(); }

        void Run(bool trainingDistribution, IReadOnlyList<int> seeds)
        {
            if (_orchestrator == null)
                throw new InvalidOperationException("Assign a PhysicalAiEpisodeOrchestrator before running evaluation.");
            if (seeds == null || seeds.Count == 0)
                throw new ArgumentException("At least one episode seed is required.", nameof(seeds));
            if (PhysicalAiLaunchArguments.HasFlag("-physicalAiRlPort") && (_bridge == null || !_bridge.IsConnected))
                throw new InvalidOperationException("RL evaluation requires a connected trainer; no fallback rollout is permitted.");
            _cancelled = false;
            _trainingDistribution = trainingDistribution;
            // Generalisation metrics are solo driving metrics.  A racing opponent
            // is evaluated in its own opt-in scenario so it cannot turn every
            // holdout result into an opponent-collision failure.
            var headToHead = HasCommandLineFlag("-physicalAiHeadToHead");
            _orchestrator.SetHeadToHeadEnabled(headToHead);
            if (headToHead)
            {
                var speed = GetCommandLineFloat("-physicalAiOpponentTargetSpeed");
                var initialProgress = GetCommandLineFloat("-physicalAiOpponentInitialProgressMeters");
                var lateralOffset = GetCommandLineFloat("-physicalAiOpponentLateralOffset");
                var lateralAmplitude = GetCommandLineFloat("-physicalAiOpponentLateralOscillationAmplitude");
                var lateralFrequency = GetCommandLineFloat("-physicalAiOpponentLateralOscillationFrequencyHz");
                _orchestrator.ConfigureHeadToHead(speed, initialProgress, lateralOffset, lateralAmplitude, lateralFrequency);
                EvaluationScenarioLabel = $"head-to-head:targetSpeed={Format(speed)},initialProgress={Format(initialProgress)}," +
                    $"lateralOffset={Format(lateralOffset)},lateralAmplitude={Format(lateralAmplitude)},lateralFrequencyHz={Format(lateralFrequency)}";
            }
            else
                EvaluationScenarioLabel = "solo";
            // A supplied label is normally a checkpoint/run identifier. Preserve it
            // verbatim so a report remains attributable after a later scene change.
            _policyLabel = GetCommandLineArg("-physicalAiPolicyLabel") ?? _orchestrator.ActivePolicyLabel;
            _seeds = seeds;
            _session = new HoldoutEvaluationSession(!trainingDistribution);
            _nextSeedIndex = 0;
            _timedOut = false;
            StartNextEpisode();
        }

        void StartNextEpisode()
        {
            if (_cancelled) return;
            if (_nextSeedIndex >= _seeds.Count)
            {
                EvaluationCompleted?.Invoke(_session.BuildReport());
                if (_bridge != null && _bridge.IsManaged) _bridge.NotifyTrainingCompleted();
                if (PhysicalAiLaunchArguments.HasFlag("-physicalAiQuitAfterEvaluation"))
                    Application.Quit();
                return;
            }
            var seed = _seeds[_nextSeedIndex++];
            _episodeStartTime = Time.time;
            _timedOut = false;
            _session.BeginEpisode(seed, _episodeStartTime);
            if (_trainingDistribution && PhysicalAiLaunchArguments.GetValue("-physicalAiMapMode") != null) _orchestrator.ResetEpisode(seed);
            else if (_trainingDistribution) _orchestrator.ResetFiniteTrainingEpisode(seed);
            else _orchestrator.ResetHoldoutEpisode(seed);
        }

        void OnEpisodeFinished(bool completed, bool collision, bool offCourse)
        {
            if (_cancelled || _session == null || !_session.HasActiveEpisode)
                return;
            var result = _session.CompleteEpisode(completed, collision, offCourse, Time.time,
                _orchestrator.LastCollisionWasBlindCornerHazard, _orchestrator.LastCollisionWasOpponent, _timedOut,
                _orchestrator.ProgressMeters, _orchestrator.DistanceFromCenterline,
                _orchestrator.EgoOvertakeCount, _orchestrator.OpponentOvertakeCount,
                _orchestrator.HazardWasSpawned);
            EpisodeMeasured?.Invoke(result);
            // All terminal listeners (especially the trainer checkpoint ACK) must finish before resetting.
            StartCoroutine(AdvanceAfterTerminal(_session));
        }

        IEnumerator AdvanceAfterTerminal(HoldoutEvaluationSession session)
        {
            yield return null;
            if (_cancelled || _session != session) yield break;
            if (PhysicalAiLaunchArguments.HasFlag("-physicalAiRlPort") && (_bridge == null || !_bridge.IsConnected)) yield break;
            StartNextEpisode();
        }

        static string GetCommandLineArg(string parameter) => PhysicalAiLaunchArguments.GetValue(parameter);

        static int GetCommandLineInt(string parameter, int fallback)
        {
            var value = GetCommandLineArg(parameter);
            return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
        }

        static float? GetCommandLineFloat(string parameter)
        {
            var value = GetCommandLineArg(parameter);
            return float.TryParse(value, out var parsed) && !float.IsNaN(parsed) && !float.IsInfinity(parsed)
                ? parsed : (float?)null;
        }

        static string Format(float? value) => value.HasValue
            ? value.Value.ToString("R", CultureInfo.InvariantCulture)
            : "scene-default";

        static bool HasCommandLineFlag(string parameter)
        {
            foreach (var argument in PhysicalAiLaunchArguments.Current)
                if (argument == parameter) return true;
            return false;
        }
    }
}
