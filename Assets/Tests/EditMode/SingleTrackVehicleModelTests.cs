using System;
using NUnit.Framework;
using PhysicalAi.VehicleDynamics;

namespace PhysicalAi.Tests.EditMode
{
    public class SingleTrackVehicleModelTests
    {
        static SingleTrackVehicleModel Model() =>
            new SingleTrackVehicleModel(SingleTrackParameters.FromSpecification(VehicleSpecification.Default));

        static double Speed(SingleTrackState s) => Math.Sqrt(
            s.LongitudinalVelocity * s.LongitudinalVelocity + s.LateralVelocity * s.LateralVelocity);

        static double Energy(SingleTrackState s, SingleTrackParameters p) =>
            0.5 * p.Mass * Speed(s) * Speed(s) + 0.5 * p.YawInertia * s.YawRate * s.YawRate;

        static void AssertCircle(SingleTrackAxleForce axle, double mu)
        {
            Assert.That(Math.Sqrt(axle.Longitudinal * axle.Longitudinal + axle.Lateral * axle.Lateral),
                Is.LessThanOrEqualTo(mu * axle.NormalLoad + 1e-10));
        }

        [Test]
        public void DefaultsUseTt02NotReferenceTruck()
        {
            var p = Model().Parameters;
            Assert.That(p.Mass, Is.EqualTo(2.1).Within(1e-6));
            Assert.That(p.FrontDistance + p.RearDistance, Is.EqualTo(0.257).Within(1e-6));
            Assert.That(p.YawInertia, Is.EqualTo(0.016).Within(1e-6));
            Assert.That(p.MaximumDriveForce, Is.EqualTo(18.5).Within(1e-5));
            Assert.That(p.KinematicSpeed, Is.EqualTo(0.5));
        }

        [TestCase(0.0)]
        [TestCase(1.0)]
        [TestCase(-1.0)]
        public void AtRestSteeringCannotCreateMotion(double steering)
        {
            var model = Model();
            var state = new SingleTrackState();
            for (var i = 0; i < 10000; i++)
                state = model.Step(state, new SingleTrackInput { Steering = steering, SurfaceFriction = 0.9 }, 0.01).State;
            Assert.That(state.X, Is.Zero);
            Assert.That(state.Y, Is.Zero);
            Assert.That(state.Heading, Is.Zero);
            Assert.That(state.YawRate, Is.Zero);
            Assert.That(Speed(state), Is.Zero);
        }

        [Test]
        public void SteeringHasFiniteRateAndAngle()
        {
            var model = Model();
            var input = new SingleTrackInput { Steering = 10, SurfaceFriction = 0.9 };
            var state = model.Step(new SingleTrackState(), input, 0.01).State;
            Assert.That(state.SteeringAngle, Is.EqualTo(model.Parameters.MaximumSteeringRate * 0.01).Within(1e-12));
            for (var i = 0; i < 100; i++) state = model.Step(state, input, 0.01).State;
            Assert.That(state.SteeringAngle, Is.EqualTo(model.Parameters.MaximumSteeringAngle));
        }

        [TestCase(1.0)]
        [TestCase(-1.0)]
        public void StraightDriveIsBoundedAndBrakeStopsWithoutReversing(double direction)
        {
            var model = Model();
            var state = new SingleTrackState();
            for (var i = 0; i < 3000; i++)
            {
                var previous = state;
                var result = model.Step(state, new SingleTrackInput { Throttle = direction, SurfaceFriction = 0.9 }, 0.01);
                state = result.State;
                Assert.That(Speed(state), Is.LessThanOrEqualTo(model.Parameters.NoLoadSpeed + 1e-9));
                Assert.That(Speed(state) - Speed(previous), Is.LessThanOrEqualTo(0.9 * model.Parameters.Gravity * 0.01 + 1e-9));
                AssertCircle(result.Front, 0.9);
                AssertCircle(result.Rear, 0.9);
            }
            Assert.That(Speed(state), Is.GreaterThan(6));
            for (var i = 0; i < 1000; i++)
            {
                state = model.Step(state, new SingleTrackInput { Throttle = direction, Brake = 1, SurfaceFriction = 0.9 }, 0.01).State;
                Assert.That(direction * state.LongitudinalVelocity, Is.GreaterThanOrEqualTo(0));
            }
            Assert.That(Speed(state), Is.LessThan(1e-10));
            Assert.That(state.YawRate, Is.Zero);
            Assert.That(state.Y, Is.Zero);
        }

        [TestCase(0.0)]
        [TestCase(0.05)]
        [TestCase(0.9)]
        [TestCase(1.5)]
        public void CombinedForcesAndWorldAccelerationRespectFriction(double mu)
        {
            var model = Model();
            var state = new SingleTrackState { LongitudinalVelocity = 3, LateralVelocity = 0.7, YawRate = -2 };
            for (var i = 0; i < 5000; i++)
            {
                var oldX = state.LongitudinalVelocity * Math.Cos(state.Heading) - state.LateralVelocity * Math.Sin(state.Heading);
                var oldY = state.LongitudinalVelocity * Math.Sin(state.Heading) + state.LateralVelocity * Math.Cos(state.Heading);
                var result = model.Step(state, new SingleTrackInput
                {
                    Throttle = Math.Sin(i * 0.03), Brake = i % 200 > 150 ? 0.6 : 0,
                    Steering = Math.Sin(i * 0.013), SurfaceFriction = mu
                }, 0.01);
                state = result.State;
                AssertCircle(result.Front, mu);
                AssertCircle(result.Rear, mu);
                var dx = state.LongitudinalVelocity * Math.Cos(state.Heading) - state.LateralVelocity * Math.Sin(state.Heading) - oldX;
                var dy = state.LongitudinalVelocity * Math.Sin(state.Heading) + state.LateralVelocity * Math.Cos(state.Heading) - oldY;
                Assert.That(Math.Sqrt(dx * dx + dy * dy) / 0.01,
                    Is.LessThanOrEqualTo(mu * model.Parameters.Gravity + 1e-9), "World acceleration, including slip, must obey mu*g.");
            }
        }

        [TestCase(0.5)]
        [TestCase(1.0)]
        [TestCase(-0.5)]
        [TestCase(-1.0)]
        public void ModelTransitionDoesNotResetOrJumpState(double boundary)
        {
            var model = Model();
            var input = new SingleTrackInput { Throttle = 0.2, Steering = 0.3, SurfaceFriction = 0.9 };
            var below = new SingleTrackState { LongitudinalVelocity = boundary - 1e-7, YawRate = 0.1, SteeringAngle = 0.1 };
            var above = below;
            above.LongitudinalVelocity = boundary + 1e-7;
            var a = model.Step(below, input, 0.01);
            var b = model.Step(above, input, 0.01);
            Assert.That(Math.Abs(a.State.YawRate - b.State.YawRate), Is.LessThan(1e-5));
            Assert.That(Math.Abs(a.State.LateralVelocity - b.State.LateralVelocity), Is.LessThan(1e-5));
            Assert.That(Math.Abs(a.State.Heading - b.State.Heading), Is.LessThan(1e-6));
            Assert.That(Math.Abs(a.Front.Lateral - b.Front.Lateral), Is.LessThan(1e-4));
            Assert.That(Math.Abs(a.Rear.Lateral - b.Rear.Lateral), Is.LessThan(1e-4));
        }

        [TestCase(0.3, 0.15)]
        [TestCase(-0.3, 0.15)]
        [TestCase(2.0, 0.05)]
        [TestCase(-2.0, 0.05)]
        public void SteadyCornerMatchesBicycleRadiusBelowFrictionLimit(double targetSpeed, double delta)
        {
            var model = Model();
            var p = model.Parameters;
            var state = new SingleTrackState { LongitudinalVelocity = targetSpeed, SteeringAngle = delta };
            for (var i = 0; i < 3000; i++)
                state = model.Step(state, new SingleTrackInput
                {
                    Throttle = Math.Sign(targetSpeed) * Math.Max(0, (Math.Abs(targetSpeed) - Speed(state)) * 0.5),
                    Steering = delta / p.MaximumSteeringAngle, SurfaceFriction = 0.9
                }, 0.01).State;
            var beta = Math.Atan(p.RearDistance * Math.Tan(delta) / (p.FrontDistance + p.RearDistance));
            var radius = (p.FrontDistance + p.RearDistance) / (Math.Cos(beta) * Math.Tan(delta));
            Assert.That(state.YawRate / Speed(state), Is.EqualTo(Math.Sign(targetSpeed) / radius).Within(0.04 / radius));
            Assert.That(Speed(state) * Math.Abs(state.YawRate), Is.LessThanOrEqualTo(0.9 * p.Gravity));
        }

        [Test]
        public void LeftAndRightAreMirrorImagesIncludingReverse()
        {
            var model = Model();
            var left = new SingleTrackState();
            var right = left;
            for (var i = 0; i < 6000; i++)
            {
                var input = new SingleTrackInput { Throttle = i < 3000 ? 0.4 : -0.4, Steering = 0.6, SurfaceFriction = 0.9 };
                left = model.Step(left, input, 0.01).State;
                input.Steering *= -1;
                right = model.Step(right, input, 0.01).State;
                Assert.That(left.X, Is.EqualTo(right.X).Within(1e-9));
                Assert.That(left.Y, Is.EqualTo(-right.Y).Within(1e-9));
                Assert.That(left.YawRate, Is.EqualTo(-right.YawRate).Within(1e-9));
            }
        }

        [TestCase(0.01)]
        [TestCase(0.02)]
        public void UnpoweredMotionCannotGainEnergy(double dt)
        {
            var model = Model();
            var state = new SingleTrackState { LongitudinalVelocity = 0.1, LateralVelocity = 0.2, YawRate = 5.6 };
            for (var i = 0; i < 10000; i++)
            {
                var previousEnergy = Energy(state, model.Parameters);
                state = model.Step(state, new SingleTrackInput { Steering = Math.Sin(i * 0.05), SurfaceFriction = 0.9 }, dt).State;
                Assert.That(Energy(state, model.Parameters), Is.LessThanOrEqualTo(previousEnergy + 1e-12));
            }
        }

        [Test]
        public void BrakingWhileSteeringCannotInjectEnergy()
        {
            var model = Model();
            var state = new SingleTrackState { LongitudinalVelocity = -3, LateralVelocity = 1, YawRate = -5.6 };
            for (var i = 0; i < 10000; i++)
            {
                var previous = Energy(state, model.Parameters);
                state = model.Step(state, new SingleTrackInput
                {
                    Throttle = 1, Brake = 0.5, Steering = Math.Sin(i * 0.1), SurfaceFriction = 0.9
                }, 0.01).State;
                Assert.That(Energy(state, model.Parameters), Is.LessThanOrEqualTo(previous + 1e-12));
            }
            Assert.That(Speed(state), Is.LessThan(1e-10));
            Assert.That(Math.Abs(state.YawRate), Is.LessThan(1e-10));
        }

        [TestCase(0.01)]
        [TestCase(0.02)]
        public void PureStationarySpinDissipatesInsteadOfSaturatingIntoAYawCouple(double dt)
        {
            var model = Model();
            var state = new SingleTrackState { YawRate = 5.6 };
            for (var i = 0; i < 1000; i++)
                state = model.Step(state, new SingleTrackInput { SurfaceFriction = 0.9 }, dt).State;
            Assert.That(Math.Abs(state.YawRate), Is.LessThan(1e-10));
            // Unequal axle loads can convert some spin into translation. With no
            // rolling drag or brake that residual coast is allowed, not self-excitation.
            var initialEnergy = 0.5 * model.Parameters.YawInertia * 5.6 * 5.6;
            Assert.That(Energy(state, model.Parameters), Is.LessThan(initialEnergy * 0.001));
        }

        [Test]
        public void ExcessiveCorneringDemandProducesSlipNotAnImpossibleRadius()
        {
            var model = Model();
            var p = model.Parameters;
            var wheelbase = p.FrontDistance + p.RearDistance;
            var state = new SingleTrackState { LongitudinalVelocity = 4, SteeringAngle = 0.2 };
            var requestedAcceleration = 16 * Math.Tan(0.2) / wheelbase;
            Assert.That(requestedAcceleration, Is.GreaterThan(0.1 * p.Gravity * 10));

            // The path a vehicle with no slip would follow exactly.
            var kinematicRadius = wheelbase / Math.Tan(0.2);
            var tightestRadius = double.PositiveInfinity;
            var previousCourse = Course(state);
            for (var i = 0; i < 100; i++)
            {
                var result = model.Step(state, new SingleTrackInput { Steering = 0.2 / p.MaximumSteeringAngle, SurfaceFriction = 0.1 }, 0.01);
                state = result.State;
                AssertCircle(result.Front, 0.1);
                AssertCircle(result.Rear, 0.1);

                // Measure the radius the vehicle actually travels, from how fast its
                // velocity turns. Yaw rate cannot stand in for this: on a surface
                // this slippery the vehicle spins while sliding nearly straight on,
                // so its yaw rate rises past the kinematic rate at the very moment
                // its path is widest. That made the old yaw-rate bound read a spin
                // as an impossibly tight corner.
                var course = Course(state);
                var courseRate = Math.Abs(course - previousCourse) / 0.01;
                previousCourse = course;
                if (courseRate > 1e-9)
                    tightestRadius = Math.Min(tightestRadius, Speed(state) / courseRate);
            }

            // A slide, not a turn: about 50 degrees of body slip by the end.
            Assert.That(Math.Abs(state.LateralVelocity), Is.GreaterThan(0.05));
            Assert.That(tightestRadius, Is.GreaterThan(kinematicRadius * 5),
                $"tightest achieved radius {tightestRadius:F2} m against a demanded {kinematicRadius:F2} m");
        }

        [Test]
        public void CoastingLeavesEnoughGripToKeepCornering()
        {
            var model = Model();
            var p = model.Parameters;
            // A purely speed-proportional coast rate demanded 2.4*v m/s^2, which passes
            // mu*g = 8.83 at 3.68 m/s. Above that, coasting spent the whole friction
            // budget and the axles had nothing left: releasing the throttle to turn in,
            // which is what a driver does, removed all cornering grip.
            foreach (var speed in new[] { 2.0, 3.0, 4.0, 5.0, 6.0 })
            {
                var state = new SingleTrackState { LongitudinalVelocity = speed };
                var result = model.Step(state, new SingleTrackInput { Steering = 0.3, SurfaceFriction = 0.9 }, 0.01);
                var rearCapacity = 0.9 * result.Rear.NormalLoad;
                var spent = Math.Abs(result.Rear.Longitudinal) / rearCapacity;
                Assert.That(spent, Is.LessThan(0.35),
                    $"coasting at {speed} m/s spends {spent:P0} of the rear friction budget");
            }

            // The whole point of a released throttle is that it is gentler than the brake.
            Assert.That(p.MaximumCoastDeceleration, Is.LessThan(p.MaximumBrakeForce / p.Mass * 0.5));
        }

        [Test]
        public void CoastingStillSlowsTheVehicleNoticeably()
        {
            var model = Model();
            var state = new SingleTrackState { LongitudinalVelocity = 4 };
            var steps = 0;
            while (state.LongitudinalVelocity > 0.05 && steps++ < 5000)
                state = model.Step(state, Coasting, 0.01).State;
            // Far enough to feel, far short of the 34 m an undamped roll would have taken.
            Assert.That(state.X, Is.InRange(2.0, 8.0), $"coasted {state.X:F2} m from 4 m/s");
        }

        [Test]
        public void TheVehicleUndersteersInsteadOfBeingExactlyNeutral()
        {
            var p = Model().Parameters;
            // D is a fraction of mu*Fz, so a single shared stiffness made each axle's
            // cornering stiffness exactly proportional to its own load and the understeer
            // gradient Wf/Cf - Wr/Cr cancelled to zero for every mass distribution. A
            // neutral vehicle saturates both axles at the same instant: it snaps into a
            // spin rather than pushing wide, and its yaw gain never rolls off with speed.
            Assert.That(p.TireBFront, Is.LessThan(p.TireBRear),
                "a softer front than rear is what makes the understeer gradient positive");
            var gradient = (1 / (p.TireBFront * p.TireC * p.TireD) -
                            1 / (p.TireBRear * p.TireC * p.TireD)) / 0.9;
            Assert.That(gradient, Is.GreaterThan(0));
        }

        [Test]
        public void SteeringTravelIsLimitedToWhatTheSurfaceCanHold()
        {
            var p = Model().Parameters;
            // Full lock is a 0.55 m radius. Holding it needs mu*g above 2.21 m/s, so
            // beyond that the extra lock buys slip rather than cornering.
            Assert.That(p.GripLimitedSteeringAngle(0, 0.9), Is.EqualTo(p.MaximumSteeringAngle));
            Assert.That(p.GripLimitedSteeringAngle(2.0, 0.9), Is.EqualTo(p.MaximumSteeringAngle));

            var previous = p.MaximumSteeringAngle;
            foreach (var speed in new[] { 3.0, 4.0, 5.0, 6.4 })
            {
                var limit = p.GripLimitedSteeringAngle(speed, 0.9);
                Assert.That(limit, Is.LessThan(previous), $"travel must keep shrinking at {speed} m/s");
                // The angle it allows is exactly the one whose corner the tires can hold.
                var radius = (p.FrontDistance + p.RearDistance) / Math.Tan(limit);
                Assert.That(speed * speed / radius, Is.EqualTo(0.9 * p.Gravity).Within(1e-6));
                previous = limit;
            }

            // A slippery surface must give less travel than a grippy one at the same speed.
            Assert.That(p.GripLimitedSteeringAngle(4.0, 0.3), Is.LessThan(p.GripLimitedSteeringAngle(4.0, 0.9)));
        }

        /// <summary>Direction the vehicle is actually travelling, body slip included.</summary>
        static double Course(SingleTrackState s) =>
            s.Heading + Math.Atan2(s.LateralVelocity, s.LongitudinalVelocity);

        [Test]
        public void OneHundredHertzAgreesWithSmallerStepsOnCorneringTrajectory()
        {
            var model = Model();
            var coarse = new SingleTrackState { LongitudinalVelocity = 2 };
            var fine = coarse;
            var input = new SingleTrackInput { Throttle = 0.15, Steering = 0.2, SurfaceFriction = 0.9 };
            for (var i = 0; i < 200; i++) coarse = model.Step(coarse, input, 0.01).State;
            for (var i = 0; i < 800; i++) fine = model.Step(fine, input, 0.0025).State;
            Assert.That(Math.Abs(coarse.X - fine.X), Is.LessThan(0.05));
            Assert.That(Math.Abs(coarse.Y - fine.Y), Is.LessThan(0.05));
            Assert.That(Math.Abs(coarse.Heading - fine.Heading), Is.LessThan(0.03));
            Assert.That(Math.Abs(Speed(coarse) - Speed(fine)), Is.LessThan(0.05));
        }

        [Test]
        public void FloatingOriginOnlyChangesPosition()
        {
            var model = Model();
            var a = new SingleTrackState { X = 100, Y = -300, Heading = 0.7, LongitudinalVelocity = 2, YawRate = 0.2 };
            var b = a;
            b.X -= 100; b.Y += 300;
            var input = new SingleTrackInput { Steering = 0.2, SurfaceFriction = 0.9 };
            a = model.Step(a, input, 0.01).State;
            b = model.Step(b, input, 0.01).State;
            Assert.That(a.X - 100, Is.EqualTo(b.X).Within(1e-12));
            Assert.That(a.Y + 300, Is.EqualTo(b.Y).Within(1e-12));
            Assert.That(a.LongitudinalVelocity, Is.EqualTo(b.LongitudinalVelocity));
            Assert.That(a.LateralVelocity, Is.EqualTo(b.LateralVelocity));
            Assert.That(a.YawRate, Is.EqualTo(b.YawRate));
        }

        [Test]
        public void RejectsInvalidConfigurationStateAndInputs()
        {
            var p = Model().Parameters;
            p.Mass = double.NaN;
            Assert.Throws<ArgumentException>(() => new SingleTrackVehicleModel(p));
            var model = Model();
            Assert.Throws<ArgumentException>(() => model.Step(new SingleTrackState { YawRate = double.NaN }, new SingleTrackInput(), 0.01));
            Assert.Throws<ArgumentException>(() => model.Step(new SingleTrackState(), new SingleTrackInput { SurfaceFriction = -1 }, 0.01));
            Assert.Throws<ArgumentOutOfRangeException>(() => model.Step(new SingleTrackState(), new SingleTrackInput(), 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => model.Step(new SingleTrackState(), new SingleTrackInput(), 0.1));
        }

        [Test]
        public void ReleasingTheThrottleCoastsToAStopInsteadOfRollingOn()
        {
            var parameters = SingleTrackParameters.FromSpecification(VehicleSpecification.Default);
            var model = new SingleTrackVehicleModel(parameters);
            var state = new SingleTrackState { LongitudinalVelocity = 4.0 };

            var travelled = 0.0;
            for (var step = 0; step < 2000 && state.LongitudinalVelocity > 0.05; step++)
            {
                var before = state.X;
                state = model.Step(state, Coasting, 0.01).State;
                travelled += state.X - before;
            }

            // Rolling and aerodynamic drag alone would carry a 1/10 car about 34 m, far
            // enough that releasing the throttle feels like nothing happens. Drivetrain
            // drag dominates a geared RC car and stops it within a corridor length.
            Assert.That(state.LongitudinalVelocity, Is.LessThan(0.05),
                $"still rolling at {state.LongitudinalVelocity:F3} m/s");
            Assert.That(travelled, Is.InRange(1.0, 8.0), $"coasted {travelled:F2} m from 4 m/s");
        }

        [Test]
        public void CoastingNeverReversesTheVehicle()
        {
            var parameters = SingleTrackParameters.FromSpecification(VehicleSpecification.Default);
            var model = new SingleTrackVehicleModel(parameters);
            var state = new SingleTrackState { LongitudinalVelocity = 0.2 };

            for (var step = 0; step < 500; step++)
                state = model.Step(state, Coasting, 0.02).State;

            // Resistance must not overshoot through zero and push the vehicle backwards,
            // even at the largest step the model accepts.
            Assert.That(state.LongitudinalVelocity, Is.InRange(0.0, 0.2));
        }

        static SingleTrackInput Coasting => new SingleTrackInput { SurfaceFriction = 0.9 };
    }
}
