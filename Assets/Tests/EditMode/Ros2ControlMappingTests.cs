using NUnit.Framework;
using PhysicalAi.Planning;

namespace PhysicalAi.Tests.EditMode
{
    public class Ros2ControlMappingTests
    {
        [Test]
        public void FromAccelerationAndSteering_MapsAccelerationToThrottleBrakeAndSteering()
        {
            var accelerating = Ros2ControlMapping.FromAccelerationAndSteering(1.5f, 0.2f, 3f, 30f);
            var braking = Ros2ControlMapping.FromAccelerationAndSteering(-3f, 0f, 3f, 30f);

            Assert.That(accelerating.Throttle, Is.EqualTo(0.5f));
            Assert.That(accelerating.Brake, Is.EqualTo(0f));
            Assert.That(accelerating.Steering, Is.GreaterThan(0f));
            Assert.That(braking.Throttle, Is.EqualTo(0f));
            Assert.That(braking.Brake, Is.EqualTo(1f));
        }
    }
}
