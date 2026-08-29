// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using System;
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
            [SerializeField, Min(0.0001f)] float _rotationalInertia = 0.012f;

            float _angularVelocity;

            public bool IsGrounded { get; private set; }
            public float AngularVelocity => _angularVelocity;
            public TireForce LastTireForce { get; private set; }

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
                    IntegrateWheel(driveTorque, brakeTorque, 0f, deltaTime);
                    return;
                }

                var suspensionLength = Mathf.Max(0f, hit.distance - _radius);
                var compression = Mathf.Clamp(_suspensionRestLength - suspensionLength, 0f, _suspensionRestLength);
                var pointVelocity = body.GetPointVelocity(hit.point);
                var suspensionVelocity = Vector3.Dot(pointVelocity, hit.normal);
                var normalForce = Mathf.Max(0f, _springRate * compression - _damperRate * suspensionVelocity);
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

                var tireForce = CombinedSlipTireModel.Evaluate(
                    longitudinalSlip,
                    slipAngle,
                    normalForce,
                    surfaceFriction,
                    longitudinalCoefficients,
                    lateralCoefficients);
                LastTireForce = tireForce;
                body.AddForceAtPosition(forward * tireForce.Longitudinal + right * tireForce.Lateral, hit.point, ForceMode.Force);
                IntegrateWheel(driveTorque, brakeTorque, tireForce.Longitudinal, deltaTime);
            }

            void IntegrateWheel(float driveTorque, float brakeTorque, float longitudinalForce, float deltaTime)
            {
                var brakeDirection = Mathf.Sign(_angularVelocity);
                if (Mathf.Abs(_angularVelocity) < 0.01f)
                    brakeDirection = Mathf.Sign(driveTorque - longitudinalForce * _radius);

                var netTorque = driveTorque - longitudinalForce * _radius - brakeDirection * brakeTorque;
                var previousAngularVelocity = _angularVelocity;
                _angularVelocity += netTorque / _rotationalInertia * deltaTime;
                if (brakeTorque > 0f && Mathf.Sign(previousAngularVelocity) != Mathf.Sign(_angularVelocity))
                    _angularVelocity = 0f;
            }

            public float DriveTorque(float throttle, float maximumDriveTorque) => _driven ? Mathf.Clamp(throttle, -1f, 1f) * maximumDriveTorque : 0f;

            public void ResetState()
            {
                _angularVelocity = 0f;
                IsGrounded = false;
                LastTireForce = default;
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
        [SerializeField, Min(0f)] float _maximumSteeringAngle = 28f;
        [SerializeField] LayerMask _groundMask = ~0;
        [SerializeField] Wheel[] _wheels = Array.Empty<Wheel>();

        Rigidbody _rigidbody;

        public float Throttle { get => _throttle; set => _throttle = Mathf.Clamp(value, -1f, 1f); }
        public float Brake { get => _brake; set => _brake = Mathf.Clamp01(value); }
        public float Steering { get => _steering; set => _steering = Mathf.Clamp(value, -1f, 1f); }
        public float SurfaceFriction { get => _surfaceFriction; set => _surfaceFriction = Mathf.Max(0.01f, value); }
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
                    wheel.DriveTorque(_throttle, _maximumDriveTorque),
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
