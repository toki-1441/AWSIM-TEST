// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using UnityEngine;

namespace PhysicalAi.Planning
{
    public readonly struct DirectVehicleActuation
    {
        public readonly float Throttle;
        public readonly float Brake;
        public readonly float Steering;

        public DirectVehicleActuation(float throttle, float brake, float steering)
        {
            Throttle = throttle;
            Brake = brake;
            Steering = steering;
        }
    }

    /// <summary>Maps Autoware acceleration and steering commands to normalized vehicle actuation.</summary>
    public static class Ros2ControlMapping
    {
        public static DirectVehicleActuation FromAccelerationAndSteering(float acceleration, float steeringRadians, float maximumAcceleration, float maximumSteeringDegrees)
        {
            var normalizedAcceleration = acceleration / Mathf.Max(0.001f, maximumAcceleration);
            return new DirectVehicleActuation(
                Mathf.Clamp01(normalizedAcceleration),
                Mathf.Clamp01(-normalizedAcceleration),
                Mathf.Clamp(steeringRadians * Mathf.Rad2Deg / Mathf.Max(0.001f, maximumSteeringDegrees), -1f, 1f));
        }
    }
}
