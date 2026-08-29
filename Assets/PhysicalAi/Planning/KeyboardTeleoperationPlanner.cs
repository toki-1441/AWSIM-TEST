// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using UnityEngine;

namespace PhysicalAi.Planning
{
    /// <summary>Optional high-level keyboard planner for collecting human BC demonstrations.</summary>
    public sealed class KeyboardTeleoperationPlanner : MonoBehaviour
    {
        [SerializeField] PhysicalAiEpisodeOrchestrator _orchestrator;
        [SerializeField] PlanningDemonstrationRecorder _recorder;
        [SerializeField, Min(0.1f)] float _maximumDesiredSpeed = 5f;
        [SerializeField, Min(0.01f)] float _speedRate = 2f;
        [SerializeField, Min(0.01f)] float _lateralRate = 1f;
        [SerializeField] KeyCode _toggleKey = KeyCode.F8;
        [SerializeField] bool _activeOnStart;

        PlanningCommand _command;
        public bool IsActive { get; private set; }

        void Start() => SetActive(_activeOnStart);

        void Update()
        {
            if (Input.GetKeyDown(_toggleKey)) SetActive(!IsActive);
            if (!IsActive) return;
            var speedInput = (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1f : 0f) -
                             (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);
            var lateralInput = (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1f : 0f) -
                               (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);
            _command.DesiredSpeed = Mathf.Clamp(_command.DesiredSpeed + speedInput * _speedRate * Time.deltaTime, 0f, _maximumDesiredSpeed);
            _command.DesiredLateralOffsetNormalized = Mathf.Clamp(_command.DesiredLateralOffsetNormalized + lateralInput * _lateralRate * Time.deltaTime, -1f, 1f);
        }

        public void SetActive(bool active)
        {
            IsActive = active;
            _recorder?.SetRecordingEnabled(active);
            if (_orchestrator == null) return;
            if (active)
            {
                _orchestrator.CommandProvider = GetCommand;
                _orchestrator.ResetEpisode(_orchestrator.EpisodeSeed);
            }
            else if (_orchestrator.CommandProvider == GetCommand)
                _orchestrator.CommandProvider = null;
        }

        PlanningCommand GetCommand(PlanningObservation _) => _command;
    }
}
