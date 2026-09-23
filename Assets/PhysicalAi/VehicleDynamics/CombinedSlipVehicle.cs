// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using System;
using UnityEngine;

namespace PhysicalAi.VehicleDynamics
{
    /// <summary>
    /// Compatibility component name; now a PhysX adapter for planar single-track tire dynamics. PhysX owns pose and velocity, including
    /// collision response. Only steering actuator state persists between tire steps.
    /// Manual driving can opt into a separate velocity-constrained no-slip aid.
    /// Positive public steering means right, matching the existing keyboard/ROS2 interface.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    public sealed class CombinedSlipVehicle : MonoBehaviour
    {
        [SerializeField] VehicleSpecification _specification = VehicleSpecification.Default;
        [SerializeField, Min(0.01f)] float _maximumSteeringRate = 3.2f;
        [SerializeField, Range(-1f, 1f)] float _throttle;
        [SerializeField, Range(0f, 1f)] float _brake;
        [SerializeField, Range(-1f, 1f)] float _steering;
        [SerializeField, Min(0f)] float _surfaceFriction = 0.9f;

        Rigidbody _body;
        SingleTrackVehicleModel _model;
        double _actualSteeringRadians;

        // Temporary handling aid, selected by the manual driver only. Not a tire model.
        public bool NoSlipAssistance { get; set; }
        public string VehicleModelName => NoSlipAssistance ? "planar-no-slip-assist" : "planar-single-track";

        public const RigidbodyConstraints PlanarMotionConstraints = RigidbodyConstraints.FreezePositionY |
            RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

        public float Throttle { get => _throttle; set => _throttle = FiniteClamp(value, -1, 1); }
        public float Brake { get => _brake; set => _brake = FiniteClamp(value, 0, 1); }
        public float Steering { get => _steering; set => _steering = FiniteClamp(value, -1, 1); }
        public float SurfaceFriction { get => _surfaceFriction; set => _surfaceFriction = FiniteClamp(value, 0, float.MaxValue); }
        public float ActualSteeringAngleRadians => (float)-_actualSteeringRadians;
        public SingleTrackAxleForce FrontAxleForce { get; private set; }
        public SingleTrackAxleForce RearAxleForce { get; private set; }
        public float DynamicWeight { get; private set; }
        public float PeakTireUtilization { get; private set; }
        public VehicleSpecification Specification => _specification;

        void Awake() => Initialize();

        /// <summary>Configure before driving. Changing physical parameters clears actuator state.</summary>
        public void Configure(VehicleSpecification specification, float maximumSteeringRate = 3.2f)
        {
            var parameters = SingleTrackParameters.FromSpecification(specification);
            parameters.MaximumSteeringRate = maximumSteeringRate;
            parameters.Validate();
            _specification = specification;
            _maximumSteeringRate = maximumSteeringRate;
            _surfaceFriction = specification.SurfaceFriction;
            Initialize();
            ResetState();
        }

        void Initialize()
        {
            _body = GetComponent<Rigidbody>();
            var parameters = SingleTrackParameters.FromSpecification(_specification);
            parameters.MaximumSteeringRate = _maximumSteeringRate;
            _model = new SingleTrackVehicleModel(parameters);
            _body.isKinematic = false;
            _body.mass = _specification.Mass;
            _body.centerOfMass = Vector3.up * (_specification.CenterOfGravityHeight - _specification.BodyCenterHeight);
            _body.inertiaTensor = _specification.InertiaTensor;
            _body.inertiaTensorRotation = Quaternion.identity;
            _body.constraints |= PlanarMotionConstraints;
            _body.useGravity = false;
            _body.linearDamping = 0;
            _body.angularDamping = 0;
            _body.maxAngularVelocity = 100f;
            _body.sleepThreshold = 0;
            _body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }

        /// <summary>Actuator/diagnostic reset only; the episode owner controls Rigidbody reset.</summary>
        public void ResetState()
        {
            _throttle = _brake = _steering = 0;
            _actualSteeringRadians = 0;
            FrontAxleForce = RearAxleForce = default;
            DynamicWeight = PeakTireUtilization = 0;
        }

        /// <summary>
        /// Fresh physical state, never the previous model prediction. A teleport/rebase or
        /// collision needs no separate notification and cannot restore a stale pose/velocity.
        /// </summary>
        public SingleTrackState CaptureState()
        {
            if (_model == null) Initialize();
            var rotation = _body.rotation;
            var forward = rotation * Vector3.forward;
            var localVelocity = Quaternion.Inverse(rotation) * _body.linearVelocity;
            return new SingleTrackState
            {
                X = _body.position.z, Y = -_body.position.x,
                Heading = Math.Atan2(-forward.x, forward.z),
                LongitudinalVelocity = localVelocity.z, LateralVelocity = -localVelocity.x,
                YawRate = -_body.angularVelocity.y, SteeringAngle = _actualSteeringRadians
            };
        }

        void FixedUpdate() => ApplyTireForces(Time.fixedDeltaTime);

        /// <summary>
        /// Queue tire forces once before a physics simulation. Public for isolated physics-scene
        /// tests; manual callers must disable this component's automatic FixedUpdate.
        /// The physical mode queues forces only. The temporary no-slip aid instead constrains
        /// velocity before PhysX; neither mode assigns pose or uses kinematic MovePosition.
        /// </summary>
        public void ApplyTireForces(float dt)
        {
            var before = CaptureState();
            if (NoSlipAssistance)
            {
                ApplyNoSlipAssistance(before, dt);
                return;
            }
            var result = _model.Step(before, new SingleTrackInput
            {
                Throttle = _throttle, Brake = _brake, Steering = -_steering, SurfaceFriction = _surfaceFriction
            }, dt);
            var delta = (before.SteeringAngle + result.State.SteeringAngle) * 0.5;
            var sin = Math.Sin(delta);
            var cos = Math.Cos(delta);
            var frontForward = result.Front.Longitudinal * cos - result.Front.Lateral * sin;
            var frontLeft = result.Front.Longitudinal * sin + result.Front.Lateral * cos;
            var forwardForce = frontForward + result.Rear.Longitudinal;
            var leftForce = frontLeft + result.Rear.Lateral;
            var yawTorque = _model.Parameters.FrontDistance * frontLeft -
                _model.Parameters.RearDistance * result.Rear.Lateral;
            var rotation = _body.rotation;
            _body.AddForce(rotation * new Vector3((float)-leftForce, 0, (float)forwardForce), ForceMode.Force);
            _body.AddTorque(Vector3.up * (float)-yawTorque, ForceMode.Force);
            _actualSteeringRadians = result.State.SteeringAngle;
            FrontAxleForce = result.Front;
            RearAxleForce = result.Rear;
            DynamicWeight = (float)result.DynamicWeight;
            PeakTireUtilization = Mathf.Max(Utilization(result.Front), Utilization(result.Rear));
        }

        /// <summary>
        /// Fraction of full steering travel the tires can still use at the current speed,
        /// with <paramref name="margin"/> allowing the transient turn-in that a
        /// steady-state limit alone would forbid. An RC transmitter has a dual-rate dial
        /// for exactly this; a keyboard has no dial, so a key press commands full lock at
        /// any speed -- at 4 m/s a corner demanding 3.3 times the available friction.
        /// </summary>
        public float GripLimitedSteeringTravel(float margin)
        {
            if (_model == null)
                return 1f;
            var p = _model.Parameters;
            var speed = Math.Abs(CaptureState().LongitudinalVelocity);
            return Mathf.Clamp01((float)(Math.Max(0f, margin) *
                p.GripLimitedSteeringAngle(speed, _surfaceFriction) / p.MaximumSteeringAngle));
        }

        void ApplyNoSlipAssistance(SingleTrackState before, float dt)
        {
            // Reuse only the straight-line drive/brake envelope. Lateral/yaw momentum is
            // intentionally discarded in this arcade aid, not hidden as extra tire grip.
            var drive = new SingleTrackState { LongitudinalVelocity = before.LongitudinalVelocity };
            var result = _model.Step(drive, new SingleTrackInput
            {
                Throttle = _throttle, Brake = _brake, SurfaceFriction = _surfaceFriction
            }, dt);
            var p = _model.Parameters;
            var target = -_steering * p.MaximumSteeringAngle;
            _actualSteeringRadians += Math.Max(-p.MaximumSteeringRate * dt,
                Math.Min(p.MaximumSteeringRate * dt, target - _actualSteeringRadians));
            var speed = (float)result.State.LongitudinalVelocity;
            var yawRate = (float)(-speed * Math.Tan(_actualSteeringRadians) / (p.FrontDistance + p.RearDistance));
            // Mid-step heading reduces the discrete-step sideways residual. PhysX still
            // integrates pose and resolves wall contacts AFTER this velocity constraint.
            var direction = _body.rotation * Quaternion.AngleAxis(yawRate * dt * 0.5f * Mathf.Rad2Deg, Vector3.up) * Vector3.forward;
            _body.linearVelocity = direction * speed;
            _body.angularVelocity = Vector3.up * yawRate;
            FrontAxleForce = RearAxleForce = default; // No fictitious lateral tire forces.
            DynamicWeight = PeakTireUtilization = 0;
        }

        float Utilization(SingleTrackAxleForce axle)
        {
            var capacity = _surfaceFriction * axle.NormalLoad;
            return capacity <= 0 ? 0 : (float)(Math.Sqrt(axle.Longitudinal * axle.Longitudinal +
                axle.Lateral * axle.Lateral) / capacity);
        }

        static float FiniteClamp(float value, float min, float max)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value), "Vehicle commands must be finite.");
            return Mathf.Clamp(value, min, max);
        }
    }
}
