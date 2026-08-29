using NUnit.Framework;
using PhysicalAi.Safety;

namespace PhysicalAi.Tests.EditMode
{
    public class BlindCornerSafetyTests
    {
        [Test]
        public void MaximumSafeSpeed_IncreasesWithVisibleDistance()
        {
            var shortSight = BlindCornerSafety.MaximumSafeSpeed(5f, 0.5f, 6f);
            var longSight = BlindCornerSafety.MaximumSafeSpeed(20f, 0.5f, 6f);

            Assert.That(longSight, Is.GreaterThan(shortSight));
        }

        [Test]
        public void MaximumSafeSpeed_AccountsForReactionAndBrakingDistance()
        {
            const float speed = 8f;
            const float reaction = 0.5f;
            const float deceleration = 6f;
            var distance = speed * reaction + speed * speed / (2f * deceleration);

            Assert.That(BlindCornerSafety.MaximumSafeSpeed(distance, reaction, deceleration), Is.EqualTo(speed).Within(0.001f));
        }
    }
}
