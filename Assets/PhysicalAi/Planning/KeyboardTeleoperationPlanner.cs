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
        /// <summary>
        /// Restarts the current episode from its start pose. An operator who leaves the
        /// corridor or wants another run at the same course otherwise has to quit and
        /// relaunch, which loses the streaming history a validation run is accumulating.
        /// </summary>
        [SerializeField] KeyCode _respawnKey = KeyCode.R;
        /// <summary>
        /// How far past the steady-state grip limit the operator may steer, the analogue
        /// of an RC transmitter's dual-rate dial. Held for a second at 4 m/s, 1.0 keeps
        /// body slip near 18 degrees while still turning a 1.3 m radius; 1.4 pushes it to
        /// 31 degrees, which is a slide rather than a corner. Raise it for more rotation
        /// on entry, at the cost of the attitude the operator then has to catch.
        /// </summary>
        [SerializeField, Min(1f)] float _steeringTravelMargin = 1f;
        [SerializeField] bool _activeOnStart = true;
        [SerializeField] bool _recordOnStart;

        PlanningCommand _command;
        public bool IsActive { get; private set; }
        public bool IsRecording => _recorder != null && _recorder.RecordingEnabled;

        void Start()
        {
            // A graphical player is normally interactive, but a visual-policy
            // evaluation is graphical precisely so HDRP can render JPEGs.  It is
            // not a manual session.  Leaving this direct actuator active there
            // silently prevents the Python/reference command from reaching the
            // low-level controller and turns a camera evaluation into a keyboard
            // no-input rollout.
            SetActive(ShouldActivateAtStart(_activeOnStart, Application.isBatchMode, PhysicalAiLaunchArguments.Current));
            if (IsActive) SetRecordingEnabled(_recordOnStart || PhysicalAiLaunchArguments.HasFlag("-physicalAiRecordOnStart"));
            if (IsActive && GetComponent<OperatorSessionHud>() == null) gameObject.AddComponent<OperatorSessionHud>();
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
            if (!IsActive) return;
            // Inactive teleoperation must not reset an RL episode, and typing "r" in a path/tag must not respawn.
            if (GetComponent<OperatorSessionHud>()?.IsEditingText == true) { ApplyDirectInput(0, 0); return; }
            if (Input.GetKeyDown(_recordToggleKey)) SetRecordingEnabled(!IsRecording);
            if (Input.GetKeyDown(_respawnKey)) Respawn();
            if (_orchestrator != null && _orchestrator.EpisodeEnded && _vehicle != null)
            {
                _vehicle.Throttle = 0; _vehicle.Brake = 1; _vehicle.Steering = 0;
                return;
            }
            var speedInput = (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1f : 0f) -
                             (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);
            var lateralInput = (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1f : 0f) -
                               (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);
            ApplyDirectInput(speedInput, lateralInput);
        }

        /// <summary>Restarts the current episode, keeping its seed so the course is unchanged.</summary>
        public void Respawn()
        {
            if (_orchestrator == null)
                return;
            ApplyDirectInput(0f, 0f);
            _orchestrator.ResetEpisode(_orchestrator.EpisodeSeed);
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
            // A key is either off or hard over, but only 32% of full lock is holdable at
            // 4 m/s and 13% at the 6.4 m/s top speed. Scaling the travel to what the tires
            // can use is what an RC driver does with the transmitter's dual-rate dial;
            // without it every steering input above 2.2 m/s was a commanded slide.
            var travel = _vehicle.GripLimitedSteeringTravel(_steeringTravelMargin);
            _vehicle.Throttle = Mathf.Max(0f, longitudinalInput);
            _vehicle.Brake = Mathf.Max(0f, -longitudinalInput);
            _vehicle.Steering = steeringInput * travel;

            // Preserve an intelligible command beside each optional recording. It
            // mirrors the direct controls but never drives the vehicle in this mode.
            _command.DesiredSpeed = Mathf.Max(0f, longitudinalInput) * _maximumDesiredSpeed;
            _command.DesiredLateralOffsetNormalized = steeringInput * travel;
        }

        public void SetActive(bool active)
        {
            var wasActive = IsActive;
            IsActive = active;
            if (_vehicle != null)
            {
                var model = PhysicalAiLaunchArguments.GetValue("-physicalAiVehicleModel");
                _vehicle.NoSlipAssistance = model == null ? active && !PhysicalAiLaunchArguments.HasFlag("-physicalAiAllowSlip") : model == "assisted";
                if (active) Debug.Log($"Physical AI manual vehicle model: {_vehicle.VehicleModelName}.");
            }
            if (!active && wasActive)
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
                if (_orchestrator.Course == null) _orchestrator.ResetEpisode(_orchestrator.EpisodeSeed);
            }
            else if (_orchestrator.CommandProvider == GetCommand)
                _orchestrator.CommandProvider = null;
        }

        public void SetRecordingEnabled(bool enabled)
        {
            _recorder?.SetRecordingEnabled(enabled);
        }

        PlanningCommand GetCommand(PlanningObservation _) => _command;
    }
}
