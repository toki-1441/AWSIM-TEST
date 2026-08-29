// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using PhysicalAi.VehicleDynamics;
using UnityEngine;

namespace PhysicalAi.Planning
{
    /// <summary>Converts the learned planning command to low-level classical actuation.</summary>
    public sealed class PlanningLowLevelController : MonoBehaviour
    {
        [SerializeField] CombinedSlipVehicle _vehicle;
        [SerializeField, Min(0.01f)] float _speedGain = 0.25f;
        [SerializeField, Min(0.01f)] float _lateralGain = 0.8f;

        public void Apply(PlanningCommand command, float currentSpeed, float headingErrorRadians)
        {
            if (_vehicle == null)
                return;

            var speedError = command.DesiredSpeed - currentSpeed;
            _vehicle.Throttle = Mathf.Clamp01(speedError * _speedGain);
            _vehicle.Brake = Mathf.Clamp01(-speedError * _speedGain);
            _vehicle.Steering = Mathf.Clamp(command.DesiredLateralOffsetNormalized * _lateralGain + headingErrorRadians, -1f, 1f);
        }
    }
}
