using System;
using NUnit.Framework;
using PhysicalAi.VehicleDynamics;

namespace PhysicalAi.Tests.EditMode
{
    public class VehicleSpecificationTests
    {
        [Test]
        public void Default_MatchesTheTt02Geometry()
        {
            var specification = VehicleSpecification.Default;

            Assert.DoesNotThrow(() => specification.Validate());
            Assert.That(specification.Wheelbase, Is.EqualTo(0.257f).Within(0.001f));
            Assert.That(specification.TireRadius, Is.EqualTo(0.032f).Within(0.0001f));
        }

        [Test]
        public void Default_DriveTorqueStaysAtTheTractionLimit()
        {
            var specification = VehicleSpecification.Default;

            var tractionLimit = VehicleSpecification.TractionLimitedWheelTorque(
                specification.Mass, specification.SurfaceFriction, specification.TireRadius, 4);

            Assert.That(specification.MaximumDriveTorquePerWheel, Is.EqualTo(tractionLimit).Within(0.001f));
        }

        [Test]
        public void TractionLimitedWheelTorque_WithoutDrivenWheels_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => VehicleSpecification.TractionLimitedWheelTorque(2.1f, 0.9f, 0.032f, 0));
        }

        [Test]
        public void InertiaTensor_KeepsTheSpecifiedYawInertia()
        {
            var specification = VehicleSpecification.Default;

            var tensor = specification.InertiaTensor;

            Assert.That(tensor.y, Is.EqualTo(specification.YawInertia).Within(1e-6f));
            Assert.That(tensor.x, Is.GreaterThan(tensor.z), "A car is longer than it is wide, so pitch inertia exceeds roll inertia.");
        }

        [Test]
        public void BodyCenterHeight_LiftsTheBodyClearOfTheGround()
        {
            var specification = VehicleSpecification.Default;

            Assert.That(specification.BodyCenterHeight - specification.BodyHeight * 0.5f, Is.GreaterThan(specification.TireRadius));
        }
    }
}
