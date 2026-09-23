// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using System;

namespace PhysicalAi.VehicleDynamics
{
    /// <summary>SI units, independent of scene gravity, suspension and PhysX.</summary>
    public struct SingleTrackParameters
    {
        public double Mass, FrontDistance, RearDistance, YawInertia, Gravity;
        public double MaximumDriveForce, MaximumBrakeForce, NoLoadSpeed;
        /// <summary>
        /// Deceleration with the throttle released, as a fraction of speed per second.
        /// Nothing resisted a coasting vehicle before: rolling and aerodynamic drag alone
        /// would let a 1/10 car roll 34 m from 4 m/s, so releasing the throttle did
        /// nothing an operator could feel. A geared RC car is dominated instead by
        /// drivetrain drag and motor back-EMF, both roughly proportional to speed.
        /// It is capped by <see cref="MaximumCoastDeceleration"/>.
        /// </summary>
        public double CoastDecelerationRate;
        /// <summary>
        /// Ceiling on the coast-down deceleration, in m/s^2. A purely proportional rate
        /// grows without bound: at 2.4 per second it demanded 9.6 m/s^2 at 4 m/s, more
        /// than the 8.83 m/s^2 the tires can produce at all, so coasting consumed the
        /// whole friction budget and left the axles nothing to corner with. Releasing
        /// the throttle above 3.7 m/s removed all lateral grip. Drivetrain drag is a
        /// fraction of braking effort, never more than it.
        /// </summary>
        public double MaximumCoastDeceleration;
        public double MaximumSteeringAngle, MaximumSteeringRate;
        public double KinematicSpeed, DynamicSpeed;
        /// <summary>
        /// Lateral curve stiffness per axle. One shared value made the vehicle exactly
        /// neutral-steer at every speed and every load split: D is a fraction of mu*Fz,
        /// so each axle's cornering stiffness came out exactly proportional to its own
        /// load and the understeer gradient (Wf/Cf - Wr/Cr) cancelled to zero
        /// identically. A neutral car saturates both axles at the same instant, so it
        /// snaps into a spin instead of pushing wide, and its yaw gain never rolls off
        /// with speed. A softer front than rear restores a positive margin.
        /// </summary>
        public double TireBFront, TireBRear;
        public double TireC, TireD, TireE;

        public static SingleTrackParameters FromSpecification(VehicleSpecification vehicle)
        {
            var front = CombinedSlipTireModel.DefaultLateralFront;
            var rear = CombinedSlipTireModel.DefaultLateralRear;
            var result = new SingleTrackParameters
            {
                Mass = vehicle.Mass, FrontDistance = vehicle.DistanceToFrontAxle,
                RearDistance = vehicle.DistanceToRearAxle, YawInertia = vehicle.YawInertia,
                Gravity = 9.81,
                MaximumDriveForce = 4.0 * vehicle.MaximumDriveTorquePerWheel / vehicle.TireRadius,
                MaximumBrakeForce = 4.0 * vehicle.MaximumBrakeTorquePerWheel / vehicle.TireRadius,
                NoLoadSpeed = vehicle.MaximumWheelAngularVelocity * vehicle.TireRadius,
                MaximumSteeringAngle = vehicle.MaximumSteeringAngleDegrees * Math.PI / 180.0,
                // Provisional actuator assumption, not a measured TT-02 servo rate.
                MaximumSteeringRate = 3.2,
                KinematicSpeed = 0.5, DynamicSpeed = 1.0,
                CoastDecelerationRate = 2.4, MaximumCoastDeceleration = 2.0,
                TireBFront = front.B, TireBRear = rear.B,
                TireC = front.C, TireD = front.D, TireE = front.E
            };
            result.Validate();
            return result;
        }

        /// <summary>
        /// Largest steering angle whose steady-state corner the tires can still hold at
        /// this speed, from a_lat = mu*g and R = wheelbase / tan(delta). Full lock is a
        /// 0.55 m radius, which needs 8.8 m/s^2 above 2.2 m/s -- everything the surface
        /// has. Beyond this angle the extra lock buys no more cornering, only slip.
        /// </summary>
        public double GripLimitedSteeringAngle(double speed, double surfaceFriction)
        {
            var lateralLimit = Math.Max(0, surfaceFriction) * Gravity;
            var speedSquared = speed * speed;
            // At a standstill any angle is holdable, and the radius is undefined.
            if (speedSquared <= 1e-9 || lateralLimit <= 0)
                return MaximumSteeringAngle;
            return Math.Min(MaximumSteeringAngle,
                Math.Atan((FrontDistance + RearDistance) * lateralLimit / speedSquared));
        }

        public void Validate()
        {
            foreach (var value in new[] { Mass, FrontDistance, RearDistance, YawInertia, Gravity,
                NoLoadSpeed, MaximumSteeringAngle, MaximumSteeringRate, KinematicSpeed,
                DynamicSpeed, TireBFront, TireBRear, TireC, TireD })
                if (!SingleTrackVehicleModel.Finite(value) || value <= 0)
                    throw new ArgumentException("Single-track parameters must be finite and positive.");
            if (!SingleTrackVehicleModel.Finite(CoastDecelerationRate) || CoastDecelerationRate < 0 ||
                !SingleTrackVehicleModel.Finite(MaximumCoastDeceleration) || MaximumCoastDeceleration < 0 ||
                !SingleTrackVehicleModel.Finite(MaximumDriveForce) || MaximumDriveForce < 0 ||
                !SingleTrackVehicleModel.Finite(MaximumBrakeForce) || MaximumBrakeForce < 0 ||
                !SingleTrackVehicleModel.Finite(TireE) || DynamicSpeed <= KinematicSpeed ||
                MaximumSteeringAngle >= Math.PI / 2 || TireC > 2 || TireD > 1 || TireE > 1)
                throw new ArgumentException("Invalid single-track actuator, transition or tire parameters.");
        }
    }

    /// <summary>
    /// Planar right-handed coordinates: X forward at heading zero, Y left, heading CCW.
    /// Body velocities are forward/left, positive steering turns left. Positions refer to CG.
    /// A Unity adapter must map these explicitly; this class does not read any Transform.
    /// </summary>
    public struct SingleTrackState
    {
        public double X, Y, Heading;
        public double LongitudinalVelocity, LateralVelocity, YawRate, SteeringAngle;
    }

    public struct SingleTrackInput
    {
        public double Throttle, Brake, Steering, SurfaceFriction;
    }

    /// <summary>Average axle forces over the step, in each axle's wheel frame.</summary>
    public readonly struct SingleTrackAxleForce
    {
        public readonly double Longitudinal, Lateral, NormalLoad;
        public SingleTrackAxleForce(double longitudinal, double lateral, double normalLoad)
        { Longitudinal = longitudinal; Lateral = lateral; NormalLoad = normalLoad; }
    }

    public readonly struct SingleTrackStep
    {
        public readonly SingleTrackState State;
        public readonly SingleTrackAxleForce Front, Rear;
        public readonly double DynamicWeight;
        public SingleTrackStep(SingleTrackState state, SingleTrackAxleForce front,
            SingleTrackAxleForce rear, double dynamicWeight)
        { State = state; Front = front; Rear = rear; DynamicWeight = dynamicWeight; }
    }

    /// <summary>
    /// Pure planar two-axle model. Below 0.5 m/s the lateral solve enforces kinematic
    /// rolling constraints, subject to friction. Above 1 m/s it uses a semi-implicit
    /// Pacejka secant compliance; a smoothstep blends compliance in between. State is
    /// never projected or reset at a speed threshold. No vertical or wheel-spin states.
    /// </summary>
    public sealed class SingleTrackVehicleModel
    {
        public SingleTrackParameters Parameters { get; }

        public SingleTrackVehicleModel(SingleTrackParameters parameters)
        { parameters.Validate(); Parameters = parameters; }

        public SingleTrackStep Step(SingleTrackState state, SingleTrackInput input, double dt)
        {
            Validate(state, input, dt);
            var p = Parameters;
            var steer = Clamp(input.Steering, -1, 1) * p.MaximumSteeringAngle;
            var nextSteer = state.SteeringAngle + Clamp(steer - state.SteeringAngle,
                -p.MaximumSteeringRate * dt, p.MaximumSteeringRate * dt);
            var delta = (state.SteeringAngle + nextSteer) * 0.5;
            var sin = Math.Sin(delta);
            var cos = Math.Cos(delta);
            var u = state.LongitudinalVelocity;
            var v = state.LateralVelocity;
            var r = state.YawRate;
            var massInverse = 1 / p.Mass;
            var inertiaInverse = 1 / p.YawInertia;
            var frontLoad = p.Mass * p.Gravity * p.RearDistance / (p.FrontDistance + p.RearDistance);
            var rearLoad = p.Mass * p.Gravity - frontLoad;
            var frontBudget = input.SurfaceFriction * frontLoad * dt;
            var rearBudget = input.SurfaceFriction * rearLoad * dt;
            var speed = Math.Sqrt(u * u + v * v);
            var blend = Clamp((speed - p.KinematicSpeed) / (p.DynamicSpeed - p.KinematicSpeed), 0, 1);
            blend = blend * blend * (3 - 2 * blend);

            // Frozen secant linearisation treats the stiff slip-dependent force implicitly.
            // Kinematic compliance is exactly zero: solve rolling constraints without 1/v.
            var frontSlipVelocity = -sin * u + cos * (v + p.FrontDistance * r);
            var rearSlipVelocity = v - p.RearDistance * r;
            var frontRollingSpeed = cos * u + sin * (v + p.FrontDistance * r);
            var frontCompliance = Compliance(frontSlipVelocity, frontRollingSpeed,
                input.SurfaceFriction * frontLoad, blend, p.TireBFront);
            var rearCompliance = Compliance(rearSlipVelocity, u, input.SurfaceFriction * rearLoad, blend, p.TireBRear);

            // Equal per-wheel AWD drive torque; brake overrides throttle and cannot reverse
            // an axle's rolling velocity. Longitudinal traction consumes lateral capacity.
            var brake = Clamp(input.Brake, 0, 1);
            var throttle = brake > 0 ? 0 : Clamp(input.Throttle, -1, 1);
            var frontLong = LongitudinalImpulse(frontRollingSpeed, throttle, brake, frontBudget,
                massInverse + p.FrontDistance * p.FrontDistance * sin * sin * inertiaInverse, dt);
            ApplyImpulse(ref u, ref v, ref r, frontLong * cos, frontLong * sin, p.FrontDistance);
            var rearLong = LongitudinalImpulse(u, throttle, brake, rearBudget, massInverse, dt);
            ApplyImpulse(ref u, ref v, ref r, rearLong, 0, -p.RearDistance);

            var frontLimit = Math.Sqrt(Math.Max(0, frontBudget * frontBudget - frontLong * frontLong));
            var rearLimit = Math.Sqrt(Math.Max(0, rearBudget * rearBudget - rearLong * rearLong));
            var frontArm = p.FrontDistance * cos;
            var a = massInverse + frontArm * frontArm * inertiaInverse + frontCompliance / dt;
            var b = cos * massInverse - frontArm * p.RearDistance * inertiaInverse;
            var c = massInverse + p.RearDistance * p.RearDistance * inertiaInverse + rearCompliance / dt;
            var frontRhs = sin * u - cos * (v + p.FrontDistance * r);
            var rearRhs = -(v - p.RearDistance * r);
            SolveBounded(a, b, c, frontRhs, rearRhs, frontLimit, rearLimit, out var frontLat, out var rearLat);
            ApplyImpulse(ref u, ref v, ref r, -sin * frontLat, cos * frontLat, p.FrontDistance);
            ApplyImpulse(ref u, ref v, ref r, 0, rearLat, -p.RearDistance);

            // Integrate world momentum, then express it in the new body frame. Rotating
            // the body must not rotate world velocity or create energy without a tire force.
            var headingChange = 0.5 * (state.YawRate + r) * dt;
            var headingCos = Math.Cos(state.Heading);
            var headingSin = Math.Sin(state.Heading);
            state.X += ((state.LongitudinalVelocity + u) * headingCos -
                (state.LateralVelocity + v) * headingSin) * (0.5 * dt);
            state.Y += ((state.LongitudinalVelocity + u) * headingSin +
                (state.LateralVelocity + v) * headingCos) * (0.5 * dt);
            state.Heading = Math.IEEERemainder(state.Heading + headingChange, 2 * Math.PI);
            state.LongitudinalVelocity = u * Math.Cos(headingChange) + v * Math.Sin(headingChange);
            state.LateralVelocity = -u * Math.Sin(headingChange) + v * Math.Cos(headingChange);
            state.YawRate = r;
            state.SteeringAngle = nextSteer;
            return new SingleTrackStep(state,
                new SingleTrackAxleForce(frontLong / dt, frontLat / dt, frontLoad),
                new SingleTrackAxleForce(rearLong / dt, rearLat / dt, rearLoad), blend);
        }

        double Compliance(double slipVelocity, double rollingSpeed, double capacity, double blend, double b)
        {
            if (blend == 0 || capacity == 0) return 0;
            var p = Parameters;
            var speed = Math.Max(Math.Abs(rollingSpeed), p.KinematicSpeed);
            var slip = Math.Abs(slipVelocity);
            if (slip < 1e-9) return blend * speed / (capacity * b * p.TireC * p.TireD);
            var bx = b * Math.Atan2(slip, speed);
            var force = capacity * p.TireD * Math.Sin(p.TireC *
                Math.Atan(bx - p.TireE * (bx - Math.Atan(bx))));
            return blend * slip / Math.Max(force, capacity * 1e-12);
        }

        double LongitudinalImpulse(double speed, double throttle, double brake,
            double budget, double inverseEffectiveMass, double dt)
        {
            var p = Parameters;
            if (brake > 0)
                return -Math.Sign(speed) * Math.Min(Math.Abs(speed) / inverseEffectiveMass,
                    Math.Min(budget, 0.5 * brake * p.MaximumBrakeForce * dt));
            // Throttle is signed: negative drives in reverse, so only an exactly released
            // throttle is coasting.
            if (throttle == 0)
            {
                // Resist proportionally to speed, and never past a standstill. The half
                // matches the drive and brake branches: this runs once per axle, and the
                // two together must produce the documented rate, not twice it.
                var deceleration = Math.Min(p.CoastDecelerationRate * Math.Abs(speed), p.MaximumCoastDeceleration);
                var coast = Math.Min(Math.Abs(speed), 0.5 * deceleration * dt);
                return -Math.Sign(speed) * Math.Min(coast / inverseEffectiveMass, budget);
            }
            var envelope = Math.Max(0, 1 - Math.Max(0, speed * Math.Sign(throttle)) / p.NoLoadSpeed);
            return Clamp(0.5 * throttle * p.MaximumDriveForce * envelope * dt, -budget, budget);
        }

        void ApplyImpulse(ref double u, ref double v, ref double r, double x, double y, double arm)
        {
            u += x / Parameters.Mass;
            v += y / Parameters.Mass;
            r += arm * y / Parameters.YawInertia;
        }

        // Exact minimum of a positive-definite 2D quadratic in an axle-friction box.
        // Evaluating all four edges also handles both axles saturated, without iteration.
        static void SolveBounded(double a, double b, double c, double x, double y,
            double limitX, double limitY, out double jx, out double jy)
        {
            var determinant = a * c - b * b;
            jx = (c * x - b * y) / determinant;
            jy = (a * y - b * x) / determinant;
            if (Math.Abs(jx) <= limitX && Math.Abs(jy) <= limitY) return;
            var best = double.PositiveInfinity;
            jx = jy = 0;
            for (var edge = 0; edge < 4; edge++)
            {
                var px = edge < 2 ? (edge == 0 ? -limitX : limitX) : 0;
                var py = edge >= 2 ? (edge == 2 ? -limitY : limitY) : Clamp((y - b * px) / c, -limitY, limitY);
                if (edge >= 2) px = Clamp((x - b * py) / a, -limitX, limitX);
                var cost = 0.5 * (a * px * px + 2 * b * px * py + c * py * py) - x * px - y * py;
                if (cost < best) { best = cost; jx = px; jy = py; }
            }
        }

        void Validate(SingleTrackState state, SingleTrackInput input, double dt)
        {
            if (!Finite(dt) || dt <= 0 || dt > 0.02)
                throw new ArgumentOutOfRangeException(nameof(dt), "Use a physics step in (0, 0.02] seconds.");
            // No arrays/allocations on the per-step path.
            if (!Finite(state.X) || !Finite(state.Y) || !Finite(state.Heading) ||
                !Finite(state.LongitudinalVelocity) || !Finite(state.LateralVelocity) ||
                !Finite(state.YawRate) || !Finite(state.SteeringAngle) ||
                Math.Abs(state.SteeringAngle) > Parameters.MaximumSteeringAngle + 1e-12)
                throw new ArgumentException("State must be finite with steering inside actuator limits.");
            if (!Finite(input.Throttle) || !Finite(input.Brake) || !Finite(input.Steering) ||
                !Finite(input.SurfaceFriction) || input.SurfaceFriction < 0)
                throw new ArgumentException("Inputs must be finite; friction must be nonnegative.");
        }

        internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        static double Clamp(double value, double min, double max) => Math.Max(min, Math.Min(max, value));
    }
}
