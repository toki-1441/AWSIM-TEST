using NUnit.Framework;
using PhysicalAi.Course;
using PhysicalAi.Planning;
using PhysicalAi.Racing;
using UnityEngine;

namespace PhysicalAi.Tests.EditMode
{
    public class PlanningTrainingInterfaceTests
    {
        [Test]
        public void Encode_UsesOnlyRelativeFeaturesAndSafeSpeed()
        {
            var waypoints = new[] { new RelativeCourseWaypoint(new Vector3(1f, 0f, 5f), 0.1f, 7f) };
            var observation = PlanningObservationEncoder.Encode(waypoints, new OpponentKinematics { LongitudinalDistance = 4f, LateralOffset = 1f }, 3f, 10f, 0.5f, 6f);

            Assert.That(observation.Features.Length, Is.EqualTo(9));
            Assert.That(observation.Features[0], Is.EqualTo(3f));
            Assert.That(observation.Features[1], Is.GreaterThan(0f));
            Assert.That(observation.Features[4], Is.EqualTo(1f));
            Assert.That(observation.Features[5], Is.EqualTo(5f));
        }

        [Test]
        public void Calculate_UnsafeCollisionIsPenalizedMoreThanProgress()
        {
            var reward = PlanningReward.Calculate(1f, true, false, 10f, 4f);

            Assert.That(reward, Is.LessThan(0f));
        }

        [Test]
        public void BehaviorCloningInference_ProducesBoundedPlanningCommand()
        {
            var model = new BehaviorCloningModel
            {
                SpeedWeights = new[] { 2f, 1f },
                SpeedBias = -1f,
                LateralWeights = new[] { 0.5f, 0f },
                LateralBias = 0f
            };

            var command = BehaviorCloningInference.Predict(model, new PlanningObservation(new[] { 3f, 4f }));

            Assert.That(command.DesiredSpeed, Is.EqualTo(9f));
            Assert.That(command.DesiredLateralOffsetNormalized, Is.EqualTo(1f));
        }
    }
}
