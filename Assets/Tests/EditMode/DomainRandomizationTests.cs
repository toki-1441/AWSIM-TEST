using NUnit.Framework;
using PhysicalAi.Randomization;

namespace PhysicalAi.Tests.EditMode
{
    public class DomainRandomizationTests
    {
        [Test]
        public void Sample_IsDeterministicAndWithinConfiguredBounds()
        {
            var settings = DomainRandomizationSettings.Default;
            var first = DomainRandomizationSampler.Sample(99, settings);
            var second = DomainRandomizationSampler.Sample(99, settings);

            Assert.That(first.Friction, Is.EqualTo(second.Friction));
            Assert.That(first.Friction, Is.InRange(settings.MinimumFriction, settings.MaximumFriction));
            Assert.That(first.MountPositionOffset.magnitude, Is.LessThanOrEqualTo(settings.MaximumMountPositionOffset * 1.74f));
        }
    }
}
