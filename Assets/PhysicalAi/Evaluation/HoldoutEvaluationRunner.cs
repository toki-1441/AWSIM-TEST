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
        public event Action<HoldoutEvaluationReport> EvaluationCompleted;

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
            if (_orchestrator == null)
                throw new InvalidOperationException("Assign a PhysicalAiEpisodeOrchestrator before running evaluation.");
            _session = new HoldoutEvaluationSession();
            _nextSeedIndex = 0;
            _timedOut = false;
            StartNextEpisode();
        }

        void StartNextEpisode()
        {
            if (_nextSeedIndex >= HoldoutCourseSuite.Seeds.Length)
            {
                EvaluationCompleted?.Invoke(_session.BuildReport());
                return;
            }
            var seed = HoldoutCourseSuite.Seeds[_nextSeedIndex++];
            _episodeStartTime = Time.time;
            _timedOut = false;
            _session.BeginEpisode(seed, _episodeStartTime);
            _orchestrator.ResetHoldoutEpisode(seed);
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
