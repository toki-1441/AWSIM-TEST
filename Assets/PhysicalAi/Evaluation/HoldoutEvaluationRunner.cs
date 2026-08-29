// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using System;
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
        float _episodeStartTime;
        bool _timedOut;
        bool _trainingDistribution;
        public event Action<HoldoutEvaluationReport> EvaluationCompleted;
        public bool IsTrainingDistributionSuite => _trainingDistribution;

        void OnEnable()
        {
            if (_orchestrator != null)
                _orchestrator.EpisodeFinished += OnEpisodeFinished;
        }

        void Start()
        {
            if (_runOnStart)
                RunSuite();
        }

        void OnDisable()
        {
            if (_orchestrator != null)
                _orchestrator.EpisodeFinished -= OnEpisodeFinished;
        }

        void Update()
        {
            if (_session != null && _session.HasActiveEpisode && !_timedOut && Time.time - _episodeStartTime >= _maximumEpisodeSeconds)
            {
                _timedOut = true;
                _orchestrator.EndEpisode(false, false);
            }
        }

        public void RunSuite()
        {
            Run(false);
        }

        public void RunTrainingSuite()
        {
            Run(true);
        }

        void Run(bool trainingDistribution)
        {
            if (_orchestrator == null)
                throw new InvalidOperationException("Assign a PhysicalAiEpisodeOrchestrator before running evaluation.");
            _trainingDistribution = trainingDistribution;
            _session = new HoldoutEvaluationSession(!trainingDistribution);
            _nextSeedIndex = 0;
            _timedOut = false;
            StartNextEpisode();
        }

        void StartNextEpisode()
        {
            var seeds = _trainingDistribution ? HoldoutCourseSuite.TrainingEvaluationSeeds : HoldoutCourseSuite.Seeds;
            if (_nextSeedIndex >= seeds.Length)
            {
                EvaluationCompleted?.Invoke(_session.BuildReport());
                return;
            }
            var seed = seeds[_nextSeedIndex++];
            _episodeStartTime = Time.time;
            _timedOut = false;
            _session.BeginEpisode(seed, _episodeStartTime);
            if (_trainingDistribution) _orchestrator.ResetFiniteTrainingEpisode(seed);
            else _orchestrator.ResetHoldoutEpisode(seed);
        }

        void OnEpisodeFinished(bool completed, bool collision, bool offCourse)
        {
            if (_session == null || !_session.HasActiveEpisode)
                return;
            _session.CompleteEpisode(completed, collision, offCourse, Time.time,
                _orchestrator.LastCollisionWasBlindCornerHazard, _orchestrator.LastCollisionWasOpponent, _timedOut);
            StartNextEpisode();
        }
    }
}
