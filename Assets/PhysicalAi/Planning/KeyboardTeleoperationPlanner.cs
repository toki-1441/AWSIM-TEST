// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using System;
using UnityEngine;
using PhysicalAi.VehicleDynamics;

namespace PhysicalAi.Planning
{
    /// <summary>Keyboard driver for interactive runs and human BC demonstrations.</summary>
    public sealed class KeyboardTeleoperationPlanner : MonoBehaviour
    {
        [SerializeField] PhysicalAiEpisodeOrchestrator _orchestrator;
        [SerializeField] PlanningDemonstrationRecorder _recorder;
        [SerializeField] CombinedSlipVehicle _vehicle;
        [SerializeField, Min(0.1f)] float _maximumDesiredSpeed = 5f;
        [SerializeField] KeyCode _recordToggleKey = KeyCode.F8;
        [SerializeField] bool _activeOnStart = true;
        [SerializeField] bool _recordOnStart;

        PlanningCommand _command;
        public bool IsActive { get; private set; }
        public bool IsRecording { get; private set; }

        void Start()
        {
            // A graphical player is normally interactive, but a visual-policy
            // evaluation is graphical precisely so HDRP can render JPEGs.  It is
            // not a manual session.  Leaving this direct actuator active there
            // silently prevents the Python/reference command from reaching the
            // low-level controller and turns a camera evaluation into a keyboard
            // no-input rollout.
            SetActive(ShouldActivateAtStart(_activeOnStart, Application.isBatchMode, Environment.GetCommandLineArgs()));
            SetRecordingEnabled(_recordOnStart && IsActive);
        }

        /// <summary>Separates an operator session from an automated policy run.</summary>
        public static bool ShouldActivateAtStart(bool configuredActive, bool isBatchMode, string[] commandLineArguments)
        {
            if (!configuredActive || isBatchMode)
                return false;
            if (commandLineArguments == null)
                return true;
            foreach (var argument in commandLineArguments)
            {
                if (argument == "-physicalAiEvaluation" || argument == "-physicalAiRlPort" ||
                    argument == "-physicalAiReferenceDriver")
                    return false;
            }
            return true;
        }

        void Update()
        {
            if (Input.GetKeyDown(_recordToggleKey)) SetRecordingEnabled(!IsRecording);
            if (!IsActive) return;
            var speedInput = (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1f : 0f) -
                             (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);
            var lateralInput = (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1f : 0f) -
                               (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);
            ApplyDirectInput(speedInput, lateralInput);
        }

        /// <summary>Applies normalized throttle/brake and steering for an interactive driver.</summary>
        public void ApplyDirectInput(float longitudinalInput, float steeringInput)
        {
            if (_vehicle == null)
                return;
            longitudinalInput = Mathf.Clamp(longitudinalInput, -1f, 1f);
            steeringInput = Mathf.Clamp(steeringInput, -1f, 1f);
            // A previous version integrated desired speed and lateral position. That
            // made a released key retain the last steering target and was not
            // controllable like an RC car. Interactive input is now direct and
            // stateless: W accelerates, S brakes, and releasing A/D straightens.
            _vehicle.Throttle = Mathf.Max(0f, longitudinalInput);
            _vehicle.Brake = Mathf.Max(0f, -longitudinalInput);
            _vehicle.Steering = steeringInput;

            // Preserve an intelligible command beside each optional recording. It
            // mirrors the direct controls but never drives the vehicle in this mode.
            _command.DesiredSpeed = Mathf.Max(0f, longitudinalInput) * _maximumDesiredSpeed;
            _command.DesiredLateralOffsetNormalized = steeringInput;
        }

        public void SetActive(bool active)
        {
            IsActive = active;
            if (!active)
                SetRecordingEnabled(false);
            if (_vehicle != null && !active)
            {
                _vehicle.Throttle = 0f;
                _vehicle.Brake = 0f;
                _vehicle.Steering = 0f;
            }
            if (_orchestrator == null) return;
            _orchestrator.ManualActuationOverride = active;
            if (active)
            {
                _orchestrator.CommandProvider = GetCommand;
                _orchestrator.ResetEpisode(_orchestrator.EpisodeSeed);
            }
            else if (_orchestrator.CommandProvider == GetCommand)
                _orchestrator.CommandProvider = null;
        }

        void SetRecordingEnabled(bool enabled)
        {
            IsRecording = enabled;
            _recorder?.SetRecordingEnabled(enabled);
        }

        PlanningCommand GetCommand(PlanningObservation _) => _command;
    }
}
