using NUnit.Framework;
using PhysicalAi.Racing;
using UnityEngine;

namespace PhysicalAi.Tests.EditMode
{
    public class RaceTacticsTests
    {
        [Test]
        public void Choose_ClosingOpponentWithClearLeftLane_OvertakesLeft()
        {
            var input = DefaultInput();
            input.LeftLaneClear = true;
            input.Opponent = new OpponentKinematics { LongitudinalDistance = 5f, RelativeLongitudinalSpeed = 5f };

            Assert.That(RaceTactics.Choose(input), Is.EqualTo(RaceTactic.OvertakeLeft));
        }

        [Test]
        public void Choose_ClosingOpponentWithoutSafeLane_Brakes()
        {
            var input = DefaultInput();
            input.Opponent = new OpponentKinematics { LongitudinalDistance = 5f, RelativeLongitudinalSpeed = 5f };

            Assert.That(RaceTactics.Choose(input), Is.EqualTo(RaceTactic.BrakeForSafety));
        }

        [Test]
        public void PredictIntent_LateralMotionFromBehind_IdentifiesOvertakeSide()
        {
            var opponent = new OpponentKinematics { LongitudinalDistance = -3f, RelativeLateralSpeed = -1f };

            Assert.That(RaceTactics.PredictIntent(opponent), Is.EqualTo(OpponentIntent.OvertakingLeft));
        }

        [Test]
        public void ToEgoLocal_UsesRelativePositionAndClosingSpeed()
        {
            var ego = new GameObject("ego");
            var observation = HeadToHeadScenario.ToEgoLocal(ego.transform, new Vector3(0f, 0f, 8f), new Vector3(1.5f, 0f, 12f), new Vector3(0f, 0f, 5f));

            Assert.That(observation.LongitudinalDistance, Is.EqualTo(12f));
            Assert.That(observation.LateralOffset, Is.EqualTo(1.5f));
            Assert.That(observation.RelativeLongitudinalSpeed, Is.EqualTo(3f));
            Object.DestroyImmediate(ego);
        }

        static RaceTacticInput DefaultInput() => new RaceTacticInput
        {
            TrackWidth = 4f,
            VehicleHalfWidth = 0.5f,
            SafetyMargin = 0.25f,
            MinimumTimeToCollision = 2f
        };
    }
}
