// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using UnityEngine;

namespace PhysicalAi.VehicleDynamics
{
    /// <summary>
    /// Four-wheel vehicle physics based on ray-cast suspension and a
    /// combined-slip Pacejka tire model. This component deliberately does not
    /// use WheelCollider: suspension contact and every tire force are explicit.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class CombinedSlipVehicle : MonoBehaviour
    {
        [Serializable]
        public sealed class Wheel
        {
            [SerializeField] Transform _suspensionMount;
            [SerializeField] bool _steerable;
            [SerializeField] bool _driven = true;
            [SerializeField, Min(0.01f)] float _radius = 0.15f;
            [SerializeField, Min(0.01f)] float _suspensionRestLength = 0.08f;
            [SerializeField, Min(1f)] float _springRate = 18000f;
            [SerializeField, Min(0f)] float _damperRate = 1800f;
            [SerializeField, Min(0.1f)] float _maximumNormalForce = 13f;
            [SerializeField, Min(0.0001f)] float _rotationalInertia = 0.012f;

            float _angularVelocity;

            public bool IsGrounded { get; private set; }
            public float AngularVelocity => _angularVelocity;
            public TireForce LastTireForce { get; private set; }
            public float LastNormalForce { get; private set; }
            public float LastLongitudinalSlip { get; private set; }
            public float LastSlipAngleRadians { get; private set; }
            public float LastLongitudinalVelocity { get; private set; }
            public float LastSuspensionCompression { get; private set; }
            public float LastSuspensionVelocity { get; private set; }

            public void Step(
                Rigidbody body,
                Transform vehicleTransform,
                float steeringAngleDegrees,
                float driveTorque,
                float brakeTorque,
                float surfaceFriction,
                PacejkaCoefficients longitudinalCoefficients,
                PacejkaCoefficients lateralCoefficients,
                LayerMask groundMask,
                float deltaTime)
            {
                if (_suspensionMount == null)
                {
                    LastTireForce = default;
                    ClearContactTelemetry();
                    return;
                }

                var up = vehicleTransform.up;
                var rayLength = _suspensionRestLength + _radius;
                IsGrounded = Physics.Raycast(
                    _suspensionMount.position,
                    -up,
                    out var hit,
                    rayLength,
                    groundMask,
                    QueryTriggerInteraction.Ignore);

                if (!IsGrounded)
                {
                    LastTireForce = default;
                    ClearContactTelemetry();
                    // Airborne: no contact, so no slip feedback to stabilise against.
                    IntegrateWheel(driveTorque, brakeTorque, 0f, 0f, deltaTime);
                    return;
                }

                var suspensionLength = Mathf.Max(0f, hit.distance - _radius);
                var compression = Mathf.Clamp(_suspensionRestLength - suspensionLength, 0f, _suspensionRestLength);
                var pointVelocity = body.GetPointVelocity(hit.point);
                var suspensionVelocity = Vector3.Dot(pointVelocity, hit.normal);
                // A real damper and bump stop cannot create arbitrary load. The
                // uncalibrated ray-cast spring once let full compression inject a
                // many-g acceleration into the chassis, which appears as chatter.
                var normalForce = Mathf.Clamp(
                    _springRate * compression - _damperRate * suspensionVelocity,
                    0f,
                    _maximumNormalForce);
                body.AddForceAtPosition(hit.normal * normalForce, hit.point, ForceMode.Force);

                var steerRotation = _steerable ? Quaternion.AngleAxis(steeringAngleDegrees, up) : Quaternion.identity;
                var forward = Vector3.ProjectOnPlane(steerRotation * vehicleTransform.forward, hit.normal).normalized;
                var right = Vector3.Cross(hit.normal, forward).normalized;
                var longitudinalVelocity = Vector3.Dot(pointVelocity, forward);
                var lateralVelocity = Vector3.Dot(pointVelocity, right);
                var wheelSurfaceVelocity = _angularVelocity * _radius;
                var slipDenominator = Mathf.Max(1f, Mathf.Abs(longitudinalVelocity));
                var longitudinalSlip = Mathf.Clamp((wheelSurfaceVelocity - longitudinalVelocity) / slipDenominator, -3f, 3f);
                var slipAngle = Mathf.Atan2(lateralVelocity, Mathf.Max(0.5f, Mathf.Abs(longitudinalVelocity)));
                LastNormalForce = normalForce;
                LastLongitudinalSlip = longitudinalSlip;
                LastSlipAngleRadians = slipAngle;
                LastLongitudinalVelocity = longitudinalVelocity;
                LastSuspensionCompression = compression;
                LastSuspensionVelocity = suspensionVelocity;

                var tireForce = CombinedSlipTireModel.Evaluate(
                    longitudinalSlip,
                    slipAngle,
                    normalForce,
                    surfaceFriction,
                    longitudinalCoefficients,
                    lateralCoefficients);
                LastTireForce = tireForce;
                body.AddForceAtPosition(forward * tireForce.Longitudinal + right * tireForce.Lateral, hit.point, ForceMode.Force);
                IntegrateWheel(
                    driveTorque,
                    brakeTorque,
                    tireForce.Longitudinal,
                    ContactDamping(normalForce, surfaceFriction, longitudinalCoefficients, slipDenominator),
                    deltaTime);
            }

            /// <summary>
            /// Rate at which contact torque opposes a change in wheel speed, in N·m·s.
            /// Derived from the slip stiffness at zero slip, <c>B·C·D·μ·Fz</c>, converted
            /// to an angular rate through <c>∂slip/∂ω = radius / slipDenominator</c> and
            /// back into torque through the radius. Past the tire's peak the true slope
            /// is smaller, so this is an upper bound: it can over-damp slightly but
            /// never under-damps, which is the safe direction for stability.
            /// </summary>
            float ContactDamping(float normalForce, float surfaceFriction, PacejkaCoefficients longitudinal, float slipDenominator)
            {
                var slipStiffness = longitudinal.B * longitudinal.C * Mathf.Max(0.001f, longitudinal.D)
                                    * Mathf.Max(0f, surfaceFriction) * Mathf.Max(0f, normalForce);
                return slipStiffness * _radius * _radius / Mathf.Max(0.0001f, slipDenominator);
            }

            /// <summary>
            /// Advances wheel speed with the contact reaction treated implicitly.
            /// Tire force depends on slip, and slip depends on this very speed, so an
            /// explicit update is stiff: it stays stable only while
            /// <c>deltaTime &lt; 2·inertia/contactDamping</c>, which a 1/10 scale wheel
            /// (6e-5 kg·m²) violates by several times at a 0.01 s step. Solving the
            /// linearised reaction implicitly removes that limit without altering any
            /// physical parameter — see docs/physical-ai-vehicle-oscillation.md.
            /// </summary>
            void IntegrateWheel(float driveTorque, float brakeTorque, float longitudinalForce, float contactDamping, float deltaTime)
            {
                var brakeDirection = Mathf.Sign(_angularVelocity);
                if (Mathf.Abs(_angularVelocity) < 0.01f)
                    brakeDirection = Mathf.Sign(driveTorque - longitudinalForce * _radius);

                var netTorque = driveTorque - longitudinalForce * _radius - brakeDirection * brakeTorque;
                var effectiveInertia = _rotationalInertia + Mathf.Max(0f, contactDamping) * deltaTime;
                var previousAngularVelocity = _angularVelocity;
                _angularVelocity += netTorque * deltaTime / effectiveInertia;
                if (brakeTorque > 0f && Mathf.Sign(previousAngularVelocity) != Mathf.Sign(_angularVelocity))
                    _angularVelocity = 0f;
            }

            /// <summary>
            /// First-order DC motor torque-speed envelope at the wheel. A fixed
            /// torque source lets an airborne 1/10 wheel accumulate arbitrary
            /// energy, whereas a real motor's torque falls to zero at no-load RPM.
            /// </summary>
            public float DriveTorque(float throttle, float maximumDriveTorque, float maximumAngularVelocity)
            {
                if (!_driven || Mathf.Approximately(throttle, 0f)) return 0f;
                var requestedDirection = Mathf.Sign(throttle);
                var spinningInRequestedDirection = Mathf.Sign(_angularVelocity) == requestedDirection;
                var speedFraction = spinningInRequestedDirection
                    ? Mathf.Clamp01(Mathf.Abs(_angularVelocity) / Mathf.Max(0.01f, maximumAngularVelocity))
                    : 0f;
                return Mathf.Clamp(throttle, -1f, 1f) * maximumDriveTorque * (1f - speedFraction);
            }

            public void ResetState()
            {
                _angularVelocity = 0f;
                IsGrounded = false;
                LastTireForce = default;
                ClearContactTelemetry();
            }

            void ClearContactTelemetry()
            {
                LastNormalForce = 0f;
                LastLongitudinalSlip = 0f;
                LastSlipAngleRadians = 0f;
                LastLongitudinalVelocity = 0f;
                LastSuspensionCompression = 0f;
                LastSuspensionVelocity = 0f;
            }
        }

        [Header("Vehicle command")]
        [Range(-1f, 1f), SerializeField] float _throttle;
        [Range(0f, 1f), SerializeField] float _brake;
        [Range(-1f, 1f), SerializeField] float _steering;

        [Header("Tire and road")]
        [SerializeField, Min(0.01f)] float _surfaceFriction = 1f;
        [SerializeField] PacejkaCoefficients _longitudinalCoefficients = new PacejkaCoefficients(10f, 1.9f, 1f, 0.97f);
        [SerializeField] PacejkaCoefficients _lateralCoefficients = new PacejkaCoefficients(5.2f, 1.3f, 1f, -1.6f);

        [Header("Drivetrain")]
        [SerializeField, Min(0f)] float _maximumDriveTorque = 65f;
        [SerializeField, Min(0f)] float _maximumBrakeTorque = 120f;
        [SerializeField, Min(0.01f)] float _maximumWheelAngularVelocity = 200f;
        [SerializeField, Min(0f)] float _maximumSteeringAngle = 28f;
        [SerializeField] LayerMask _groundMask = ~0;
        [SerializeField] Wheel[] _wheels = Array.Empty<Wheel>();

        Rigidbody _rigidbody;

        public float Throttle { get => _throttle; set => _throttle = Mathf.Clamp(value, -1f, 1f); }
        public float Brake { get => _brake; set => _brake = Mathf.Clamp01(value); }
        public float Steering { get => _steering; set => _steering = Mathf.Clamp(value, -1f, 1f); }
        public float SurfaceFriction { get => _surfaceFriction; set => _surfaceFriction = Mathf.Max(0.01f, value); }
        public IReadOnlyList<Wheel> Wheels => _wheels;
        public int GroundedWheelCount { get; private set; }
        public float PeakTireUtilization { get; private set; }

        /// <summary>Clears actuator and wheel-integrator state at an episode boundary.</summary>
        public void ResetState()
        {
            _throttle = 0f;
            _brake = 0f;
            _steering = 0f;
            GroundedWheelCount = 0;
            PeakTireUtilization = 0f;
            foreach (var wheel in _wheels)
                wheel?.ResetState();
        }

        void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
        }

        void FixedUpdate()
        {
            GroundedWheelCount = 0;
            PeakTireUtilization = 0f;
            var steeringAngle = _steering * _maximumSteeringAngle;
            var brakeTorque = _brake * _maximumBrakeTorque;
            foreach (var wheel in _wheels)
            {
                wheel?.Step(
                    _rigidbody,
                    transform,
                    steeringAngle,
                    wheel.DriveTorque(_throttle, _maximumDriveTorque, _maximumWheelAngularVelocity),
                    brakeTorque,
                    _surfaceFriction,
                    _longitudinalCoefficients,
                    _lateralCoefficients,
                    _groundMask,
                    Time.fixedDeltaTime);
                if (wheel != null && wheel.IsGrounded)
                {
                    GroundedWheelCount++;
                    PeakTireUtilization = Mathf.Max(PeakTireUtilization, wheel.LastTireForce.Utilization);
                }
            }
        }
    }
}
