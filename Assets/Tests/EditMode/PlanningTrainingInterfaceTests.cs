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
            var waypoints = new[]
            {
                new RelativeCourseWaypoint(new Vector3(1f, 0f, 5f), 0.1f, 7f),
                new RelativeCourseWaypoint(new Vector3(2f, 0f, 8f), 0.2f, 6f)
            };
            var observation = PlanningObservationEncoder.Encode(waypoints, new OpponentKinematics
            {
                LongitudinalDistance = 4f,
                LateralOffset = 1f,
                RelativeLongitudinalSpeed = 2.5f,
                RelativeLateralSpeed = -0.75f
            }, 3f, 10f, 0.5f, 6f);

            Assert.That(observation.Features.Length, Is.EqualTo(14));
            Assert.That(observation.Features[0], Is.EqualTo(3f));
            Assert.That(observation.Features[1], Is.GreaterThan(0f));
            Assert.That(observation.Features[2], Is.EqualTo(4f));
            Assert.That(observation.Features[3], Is.EqualTo(1f));
            Assert.That(observation.Features[4], Is.EqualTo(1f));
            Assert.That(observation.Features[5], Is.EqualTo(5f));
            Assert.That(observation.Features[8], Is.EqualTo(2.5f));
            Assert.That(observation.Features[13], Is.EqualTo(-0.75f));
        }

        [Test]
        public void Calculate_UnsafeCollisionIsPenalizedMoreThanProgress()
        {
            var reward = PlanningReward.Calculate(1f, true, false, 10f, 4f);

            Assert.That(reward, Is.LessThan(0f));
        }

        [Test]
        public void CalculateTerminal_TimeoutIsWorseThanSafeCompletion()
        {
            var completion = PlanningReward.CalculateTerminal(true, false, false, false, 0f, 4f);
            var timeout = PlanningReward.CalculateTerminal(false, false, false, true, 0f, 4f);

            Assert.That(completion, Is.EqualTo(10f));
            Assert.That(timeout, Is.EqualTo(-5f));
            Assert.That(timeout, Is.LessThan(completion));
        }

        [Test]
        public void Calculate_OvertakeEventsRewardSafePassAndPenalizeBeingPassed()
        {
            var baseline = PlanningReward.Calculate(0.2f, false, false, 2f, 3f);
            var passedOpponent = PlanningReward.Calculate(0.2f, false, false, 2f, 3f, egoOvertakeCount: 1);
            var wasPassed = PlanningReward.Calculate(0.2f, false, false, 2f, 3f, opponentOvertakeCount: 1);

            Assert.That(passedOpponent, Is.EqualTo(baseline + 5f));
            Assert.That(wasPassed, Is.EqualTo(baseline - 3f));
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

        [Test]
        public void FeasibilityProjection_ClampsLearnedSpeedToCameraAndCourseLimits()
        {
            var waypoints = new[]
            {
                new RelativeCourseWaypoint(Vector3.zero, 0f, 4f),
                new RelativeCourseWaypoint(Vector3.forward, 0f, 2f),
                new RelativeCourseWaypoint(Vector3.forward * 2f, 0f, 3f)
            };

            var command = PlanningCommandFeasibility.Project(new PlanningCommand
            {
                DesiredSpeed = 8f,
                DesiredLateralOffsetNormalized = 2f
            }, waypoints, 3f);

            Assert.That(command.DesiredSpeed, Is.EqualTo(2f));
            Assert.That(command.DesiredLateralOffsetNormalized, Is.EqualTo(1f));
        }
    }
}
