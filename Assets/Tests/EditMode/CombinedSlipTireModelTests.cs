using NUnit.Framework;
using UnityEngine;
using PhysicalAi.VehicleDynamics;

namespace PhysicalAi.Tests.EditMode
{
    public class CombinedSlipTireModelTests
    {
        [Test]
        public void Evaluate_ZeroNormalLoad_ProducesNoForce()
        {
            var force = CombinedSlipTireModel.Evaluate(1f, 1f, 0f, 1f, CombinedSlipTireModel.DefaultLongitudinal, CombinedSlipTireModel.DefaultLateralFront);

            Assert.That(force.Longitudinal, Is.EqualTo(0f));
            Assert.That(force.Lateral, Is.EqualTo(0f));
            Assert.That(force.Utilization, Is.EqualTo(0f));
        }

        [Test]
        public void Evaluate_CombinedSlip_StaysInsideFrictionCircle()
        {
            const float normalLoad = 1000f;
            const float friction = 0.8f;
            var force = CombinedSlipTireModel.Evaluate(1f, 0.8f, normalLoad, friction, CombinedSlipTireModel.DefaultLongitudinal, CombinedSlipTireModel.DefaultLateralFront);

            Assert.That(force.Utilization, Is.LessThanOrEqualTo(1f));
            Assert.That(force.Longitudinal * force.Longitudinal + force.Lateral * force.Lateral, Is.LessThanOrEqualTo(normalLoad * normalLoad * friction * friction + 0.01f));
        }

        [Test]
        public void Evaluate_PositiveSlipAngle_GeneratesRestoringLateralForce()
        {
            var force = CombinedSlipTireModel.Evaluate(0f, 0.2f, 1000f, 1f, CombinedSlipTireModel.DefaultLongitudinal, CombinedSlipTireModel.DefaultLateralFront);

            Assert.That(force.Lateral, Is.LessThan(0f));
        }
    
        [Test]
        public void LateralCurvesPeakWhereRubberActuallyPeaks()
        {
            // B multiplies a slip angle in RADIANS, so it sets where the force peaks.
            // At B = 5.2 the peak sat at 18.1 degrees: the tire only delivered the mu*Fz
            // the friction circle already assumed after the axle had crabbed more than
            // twice as far as any real tire, so every corner entry felt like a slide.
            // Rubber peaks between roughly 4 and 12 degrees at any scale. The three
            // pre-existing tests here constrain only sign and magnitude bound, so they
            // pass for any (B, C, D, E) and never noticed.
            foreach (var pair in new[]
            {
                new { Name = "front", Coefficients = CombinedSlipTireModel.DefaultLateralFront },
                new { Name = "rear", Coefficients = CombinedSlipTireModel.DefaultLateralRear },
            })
            {
                var peak = PeakSlipAngleDegrees(pair.Coefficients);
                Assert.That(peak, Is.InRange(4f, 12f), $"{pair.Name} tire peaks at {peak:F1} degrees");
            }

            // A softer front than rear is what gives the vehicle a positive understeer
            // margin; see TheVehicleUndersteersInsteadOfBeingExactlyNeutral.
            Assert.That(CombinedSlipTireModel.DefaultLateralFront.B,
                Is.LessThan(CombinedSlipTireModel.DefaultLateralRear.B));
        }

        /// <summary>Slip angle of maximum lateral force, found by sampling rather than by formula.</summary>
        static float PeakSlipAngleDegrees(PacejkaCoefficients coefficients)
        {
            var best = 0f;
            var bestForce = float.NegativeInfinity;
            for (var angle = 0.0005f; angle < 1.2f; angle += 0.0005f)
            {
                var force = Mathf.Abs(CombinedSlipTireModel.Evaluate(
                    0f, angle, 100f, 1f, CombinedSlipTireModel.DefaultLongitudinal, coefficients).Lateral);
                if (force <= bestForce) continue;
                bestForce = force;
                best = angle;
            }
            return best * Mathf.Rad2Deg;
        }
}
}
