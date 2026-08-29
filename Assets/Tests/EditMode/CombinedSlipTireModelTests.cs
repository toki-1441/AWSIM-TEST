using NUnit.Framework;
using PhysicalAi.VehicleDynamics;

namespace PhysicalAi.Tests.EditMode
{
    public class CombinedSlipTireModelTests
    {
        [Test]
        public void Evaluate_ZeroNormalLoad_ProducesNoForce()
        {
            var force = CombinedSlipTireModel.Evaluate(1f, 1f, 0f, 1f, CombinedSlipTireModel.DefaultLongitudinal, CombinedSlipTireModel.DefaultLateral);

            Assert.That(force.Longitudinal, Is.EqualTo(0f));
            Assert.That(force.Lateral, Is.EqualTo(0f));
            Assert.That(force.Utilization, Is.EqualTo(0f));
        }

        [Test]
        public void Evaluate_CombinedSlip_StaysInsideFrictionCircle()
        {
            const float normalLoad = 1000f;
            const float friction = 0.8f;
            var force = CombinedSlipTireModel.Evaluate(1f, 0.8f, normalLoad, friction, CombinedSlipTireModel.DefaultLongitudinal, CombinedSlipTireModel.DefaultLateral);

            Assert.That(force.Utilization, Is.LessThanOrEqualTo(1f));
            Assert.That(force.Longitudinal * force.Longitudinal + force.Lateral * force.Lateral, Is.LessThanOrEqualTo(normalLoad * normalLoad * friction * friction + 0.01f));
        }

        [Test]
        public void Evaluate_PositiveSlipAngle_GeneratesRestoringLateralForce()
        {
            var force = CombinedSlipTireModel.Evaluate(0f, 0.2f, 1000f, 1f, CombinedSlipTireModel.DefaultLongitudinal, CombinedSlipTireModel.DefaultLateral);

            Assert.That(force.Lateral, Is.LessThan(0f));
        }
    }
}
