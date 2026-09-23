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

        [Test]
        public void MountNoiseIsAlwaysZeroEvenWithLegacyNonzeroSettings()
        {
            var settings = DomainRandomizationSettings.Default;
            settings.MaximumMountPositionOffset = 0.03f;
            settings.MaximumMountRotationDegrees = 3f;
            for (var seed = 0; seed < 1000; seed++)
            {
                var sample = DomainRandomizationSampler.Sample(seed, settings);
                Assert.That(sample.MountPositionOffset, Is.EqualTo(UnityEngine.Vector3.zero));
                Assert.That(sample.MountRotationOffsetDegrees, Is.EqualTo(UnityEngine.Vector3.zero));
            }
        }
    }
}
