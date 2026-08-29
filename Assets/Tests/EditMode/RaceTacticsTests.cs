using NUnit.Framework;
using PhysicalAi.Racing;

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

        static RaceTacticInput DefaultInput() => new RaceTacticInput
        {
            TrackWidth = 4f,
            VehicleHalfWidth = 0.5f,
            SafetyMargin = 0.25f,
            MinimumTimeToCollision = 2f
        };
    }
}
