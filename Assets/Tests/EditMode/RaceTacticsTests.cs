using System.Collections.Generic;
using System.Reflection;
using PhysicalAi.Course;
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

        [Test]
        public void ClosedLoopOpponent_WrapsAtTheFinishInsteadOfStopping()
        {
            var root = new GameObject("opponent");
            root.AddComponent<Rigidbody>();
            var follower = root.AddComponent<OpponentCourseFollower>();
            SetField(follower, "_initialProgressMeters", 0f);
            var points = new List<Vector3>
            {
                Vector3.zero, Vector3.forward, Vector3.forward + Vector3.right,
                Vector3.right, Vector3.zero
            };
            var course = new GeneratedCourse(points, new List<float> { 0f, 0f, 0f, 0f, 0f }, new List<float> { 1f, 1f, 1f, 1f, 1f }, true);
            follower.SetCourse(course);

            Invoke(follower, "Advance", 5f);

            Assert.That((int)GetField(follower, "_segment"), Is.EqualTo(1));
            Object.DestroyImmediate(root);
        }

        static RaceTacticInput DefaultInput() => new RaceTacticInput
        {
            TrackWidth = 4f,
            VehicleHalfWidth = 0.5f,
            SafetyMargin = 0.25f,
            MinimumTimeToCollision = 2f
        };

        static void SetField(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        static object GetField(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        static void Invoke(object target, string name, params object[] arguments) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, arguments);
    }
}
