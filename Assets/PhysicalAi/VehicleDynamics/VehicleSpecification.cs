// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using System;
using UnityEngine;

namespace PhysicalAi.VehicleDynamics
{
    /// <summary>
    /// Physical specification of the target vehicle, in SI units. This is the
    /// single source of truth for the scene builder and for the dynamics
    /// defaults; see docs/physical-ai-vehicle-model.md for the provenance of
    /// every value and for which ones are measured, derived, or estimated.
    /// </summary>
    [Serializable]
    public struct VehicleSpecification
    {
        [Min(0.01f)] public float Mass;
        [Min(0.001f)] public float DistanceToFrontAxle;
        [Min(0.001f)] public float DistanceToRearAxle;
        [Min(0.001f)] public float TrackWidth;
        [Min(0f)] public float CenterOfGravityHeight;
        [Min(0.0001f)] public float YawInertia;
        [Min(0.001f)] public float TireRadius;
        [Min(0.001f)] public float BodyLength;
        [Min(0.001f)] public float BodyWidth;
        [Min(0.001f)] public float BodyHeight;
        [Min(0.001f)] public float SuspensionRestLength;
        /// <summary>
        /// Travel available below the spring's rest length, where the arm still holds
        /// the wheel down but the spring produces no force. Without it the wheel loses
        /// contact as soon as the body rises by the static sag, which for this vehicle
        /// is only 2.58 mm — less than the lift produced by a few degrees of roll.
        /// </summary>
        [Min(0f)] public float SuspensionDroopTravel;
        [Min(1f)] public float SpringRate;
        [Min(0f)] public float DamperRate;
        [Min(0.1f)] public float MaximumNormalForcePerWheel;
        [Min(0.0000001f)] public float WheelRotationalInertia;
        [Min(0f)] public float MaximumDriveTorquePerWheel;
        [Min(0f)] public float MaximumBrakeTorquePerWheel;
        [Min(0.01f)] public float MaximumWheelAngularVelocity;
        [Min(0f)] public float MaximumSteeringAngleDegrees;
        [Min(0.01f)] public float SurfaceFriction;

        public float Wheelbase => DistanceToFrontAxle + DistanceToRearAxle;

        /// <summary>
        /// Local-space diagonal inertia tensor in Unity's axis convention
        /// (x pitch, y yaw, z roll). Pitch and roll are approximated from the
        /// body box and scaled so that the yaw component matches the specified
        /// <see cref="YawInertia"/>, whose mass distribution is more centred
        /// than a uniform box.
        /// </summary>
        public Vector3 InertiaTensor
        {
            get
            {
                var uniformBoxYaw = Mass * (BodyLength * BodyLength + BodyWidth * BodyWidth) / 12f;
                var scale = uniformBoxYaw <= 0f ? 1f : YawInertia / uniformBoxYaw;
                return new Vector3(
                    Mass * (BodyLength * BodyLength + BodyHeight * BodyHeight) / 12f * scale,
                    YawInertia,
                    Mass * (BodyWidth * BodyWidth + BodyHeight * BodyHeight) / 12f * scale);
            }
        }

        /// <summary>Per-wheel static spring compression under the vehicle's own weight.</summary>
        public float StaticSuspensionSag => Mass * Physics.gravity.magnitude / (4f * Mathf.Max(0.001f, SpringRate));

        /// <summary>
        /// Height of the body centre above the ground at static ride height.
        /// The suspension mount must sit one tire radius plus the *uncompressed
        /// remainder* of travel above the road.  Using half the travel here
        /// preloaded each 2000 N/m spring by 10 mm (about four times the TT-02's
        /// weight), which caused hopping and unphysical normal-force spikes.
        /// </summary>
        public float BodyCenterHeight => TireRadius + Mathf.Max(0f, SuspensionRestLength - StaticSuspensionSag) + BodyHeight * 0.5f;

        /// <summary>
        /// Tamiya TT-02, built to the MAGP class regulations with a T21.5/T17.5
        /// brushless motor and the 8.27:1 on-road gear ratio.
        /// </summary>
        public static VehicleSpecification Default => new VehicleSpecification
        {
            Mass = 2.1f,
            DistanceToFrontAxle = 0.134f,
            DistanceToRearAxle = 0.123f,
            TrackWidth = 0.166f,
            CenterOfGravityHeight = 0.054f,
            YawInertia = 0.016f,
            TireRadius = 0.032f,
            BodyLength = 0.385f,
            BodyWidth = 0.19f,
            BodyHeight = 0.11f,
            SuspensionRestLength = 0.02f,
            SuspensionDroopTravel = 0.02f,
            SpringRate = 2000f,
            DamperRate = 35f,
            // Until damper/bump-stop data is measured, cap a contact to 2.5 times
            // the 5.15 N static corner load. This prevents a fully compressed
            // 20 mm numerical spring from turning a visible hop into traction.
            MaximumNormalForcePerWheel = 13f,
            WheelRotationalInertia = 0.00006f,
            MaximumDriveTorquePerWheel = 0.148f,
            MaximumBrakeTorquePerWheel = 0.148f,
            // T17.5 at 7.2 V: 15,840 motor rpm / 8.27:1 = 1,916 wheel rpm,
            // equivalent to 200.6 rad/s. The torque-speed curve reaches zero
            // there; 200 rad/s is the conservative shared class limit.
            MaximumWheelAngularVelocity = 200f,
            MaximumSteeringAngleDegrees = 25f,
            SurfaceFriction = 0.9f
        };

        /// <summary>
        /// Torque per wheel at which an all-wheel-drive vehicle saturates the
        /// available road friction. Beyond this the tires only spin, so it is
        /// the meaningful upper bound for the drivetrain regardless of how much
        /// torque the motor and gearing could deliver.
        /// </summary>
        public static float TractionLimitedWheelTorque(float mass, float surfaceFriction, float tireRadius, int drivenWheelCount)
        {
            if (drivenWheelCount < 1)
                throw new ArgumentOutOfRangeException(nameof(drivenWheelCount), "At least one wheel must be driven.");
            return mass * Physics.gravity.magnitude * Mathf.Max(0f, surfaceFriction) * Mathf.Max(0f, tireRadius) / drivenWheelCount;
        }

        public void Validate()
        {
            if (Mass <= 0f || TireRadius <= 0f || TrackWidth <= 0f || Wheelbase <= 0f || YawInertia <= 0f || SurfaceFriction <= 0f)
                throw new ArgumentException("Vehicle specification must use positive mass, geometry, inertia, and friction.");
            if (SuspensionRestLength <= 0f || SpringRate <= 0f || MaximumNormalForcePerWheel <= 0f || WheelRotationalInertia <= 0f || MaximumWheelAngularVelocity <= 0f)
                throw new ArgumentException("Vehicle specification must use a positive suspension and wheel inertia.");
        }
    }
}
