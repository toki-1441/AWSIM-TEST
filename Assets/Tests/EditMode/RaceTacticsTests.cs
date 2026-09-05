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

        [Test]
        public void OpponentConfiguration_OverridesOnlyTheRequestedEvaluationFields()
        {
            var root = new GameObject("opponent");
            root.AddComponent<Rigidbody>();
            var follower = root.AddComponent<OpponentCourseFollower>();
            SetField(follower, "_targetSpeed", 3f);
            SetField(follower, "_initialProgressMeters", 10f);
            SetField(follower, "_lateralOffset", 0.1f);

            follower.Configure(targetSpeed: 1.25f, initialProgressMeters: 1.5f,
                lateralOscillationAmplitude: 0.08f, lateralOscillationFrequencyHz: 0.5f);

            Assert.That(follower.TargetSpeed, Is.EqualTo(1.25f));
            Assert.That(follower.InitialProgressMeters, Is.EqualTo(1.5f));
            Assert.That(follower.LateralOffset, Is.EqualTo(0.1f));
            Assert.That(follower.LateralOscillationAmplitude, Is.EqualTo(0.08f));
            Assert.That(follower.LateralOscillationFrequencyHz, Is.EqualTo(0.5f));
            Object.DestroyImmediate(root);
        }

        [Test]
        public void OpponentLateralProbe_IsDeterministicAndChangesDirection()
        {
            Assert.That(OpponentCourseFollower.LateralOffsetAtTime(-0.28f, 0.08f, 0.5f, 0f), Is.EqualTo(-0.28f).Within(0.0001f));
            Assert.That(OpponentCourseFollower.LateralOffsetAtTime(-0.28f, 0.08f, 0.5f, 0.5f), Is.EqualTo(-0.20f).Within(0.0001f));
            Assert.That(OpponentCourseFollower.LateralOffsetAtTime(-0.28f, 0.08f, 0.5f, 1.5f), Is.EqualTo(-0.36f).Within(0.0001f));
        }

        [Test]
        public void HeadToHeadScenario_RecordsAnEgoPassByCourseProgress()
        {
            var ego = new GameObject("ego");
            ego.AddComponent<Rigidbody>();
            var opponentRoot = new GameObject("opponent");
            opponentRoot.AddComponent<Rigidbody>();
            var opponent = opponentRoot.AddComponent<OpponentCourseFollower>();
            SetField(opponent, "_initialProgressMeters", 2f);
            var scenario = ego.AddComponent<HeadToHeadScenario>();
            SetField(scenario, "_egoBody", ego.GetComponent<Rigidbody>());
            SetField(scenario, "_opponents", new[] { opponent });
            var course = new GeneratedCourse(new List<Vector3> { Vector3.zero, Vector3.forward * 10f },
                new List<float> { 0f, 0f }, new List<float> { 1f, 1f }, false);

            scenario.SetInteractionEnabled(true);
            scenario.SetCourse(course);
            scenario.UpdateEgoProgress(0f);
            scenario.UpdateEgoProgress(3f);

            Assert.That(scenario.EgoOvertakeCount, Is.EqualTo(1));
            Object.DestroyImmediate(opponentRoot);
            Object.DestroyImmediate(ego);
        }

        [Test]
        public void HeadToHeadScenario_StreamExtensionPreservesRaceState()
        {
            var ego = new GameObject("ego");
            ego.AddComponent<Rigidbody>();
            var opponentRoot = new GameObject("opponent");
            opponentRoot.AddComponent<Rigidbody>();
            var opponent = opponentRoot.AddComponent<OpponentCourseFollower>();
            SetField(opponent, "_initialProgressMeters", 2f);
            var scenario = ego.AddComponent<HeadToHeadScenario>();
            SetField(scenario, "_egoBody", ego.GetComponent<Rigidbody>());
            SetField(scenario, "_opponents", new[] { opponent });
            var initialCourse = new GeneratedCourse(new List<Vector3> { Vector3.zero, Vector3.forward * 10f },
                new List<float> { 0f, 0f }, new List<float> { 1f, 1f }, false);
            var extendedCourse = new GeneratedCourse(new List<Vector3>
                {
                    Vector3.zero, Vector3.forward * 10f, Vector3.forward * 20f
                }, new List<float> { 0f, 0f, 0f }, new List<float> { 1f, 1f, 1f }, false);

            scenario.SetInteractionEnabled(true);
            scenario.SetCourse(initialCourse);
            scenario.UpdateEgoProgress(0f);
            scenario.UpdateEgoProgress(3f);
            var progressBeforeExtension = opponent.ProgressMeters;

            scenario.ExtendCourse(extendedCourse);

            Assert.That(scenario.EgoOvertakeCount, Is.EqualTo(1));
            Assert.That(opponent.ProgressMeters, Is.EqualTo(progressBeforeExtension));
            Assert.That((int)GetField(opponent, "_segment"), Is.EqualTo(0));
            Object.DestroyImmediate(opponentRoot);
            Object.DestroyImmediate(ego);
        }

        [Test]
        public void HeadToHeadScenario_DoesNotTurnAnOpponentLapIntoAnEgoPass()
        {
            var ego = new GameObject("ego");
            ego.AddComponent<Rigidbody>();
            var opponentRoot = new GameObject("opponent");
            opponentRoot.AddComponent<Rigidbody>();
            var opponent = opponentRoot.AddComponent<OpponentCourseFollower>();
            SetField(opponent, "_initialProgressMeters", 2f);
            var scenario = ego.AddComponent<HeadToHeadScenario>();
            SetField(scenario, "_egoBody", ego.GetComponent<Rigidbody>());
            SetField(scenario, "_opponents", new[] { opponent });
            var course = new GeneratedCourse(new List<Vector3> { Vector3.zero, Vector3.forward * 10f },
                new List<float> { 0f, 0f }, new List<float> { 1f, 1f }, false);

            scenario.SetInteractionEnabled(true);
            scenario.SetCourse(course);
            scenario.UpdateEgoProgress(0f);
            // The opponent has completed an additional course length while the
            // ego is still behind. Its progress is deliberately unwrapped.
            SetField(opponent, "_progress", 12f);
            scenario.UpdateEgoProgress(3f);

            Assert.That(scenario.EgoOvertakeCount, Is.Zero);
            Object.DestroyImmediate(opponentRoot);
            Object.DestroyImmediate(ego);
        }

        [Test]
        public void HeadToHeadScenario_DoesNotTurnEgoStartLineCrossingIntoOpponentPass()
        {
            var ego = new GameObject("ego");
            ego.AddComponent<Rigidbody>();
            var opponentRoot = new GameObject("opponent");
            opponentRoot.AddComponent<Rigidbody>();
            var opponent = opponentRoot.AddComponent<OpponentCourseFollower>();
            SetField(opponent, "_initialProgressMeters", 10f);
            var scenario = ego.AddComponent<HeadToHeadScenario>();
            SetField(scenario, "_egoBody", ego.GetComponent<Rigidbody>());
            SetField(scenario, "_opponents", new[] { opponent });
            var course = new GeneratedCourse(new List<Vector3>
            {
                Vector3.zero, Vector3.forward * 10f, Vector3.zero
            }, new List<float> { 0f, 0f, 0f }, new List<float> { 1f, 1f, 1f }, true);

            scenario.SetInteractionEnabled(true);
            scenario.SetCourse(course);
            scenario.UpdateEgoProgress(18f);
            // The monitor reports a closed-course start-line crossing as 1 m,
            // although the ego's physical distance is now 21 m.
            scenario.UpdateEgoProgress(1f);

            Assert.That(scenario.EgoOvertakeCount, Is.Zero);
            Assert.That(scenario.OpponentOvertakeCount, Is.Zero);
            Object.DestroyImmediate(opponentRoot);
            Object.DestroyImmediate(ego);
        }

        [Test]
        public void HeadToHeadScenario_CanDisableOpponentsForSoloEvaluation()
        {
            var ego = new GameObject("ego");
            ego.AddComponent<Rigidbody>();
            var opponentRoot = new GameObject("opponent");
            opponentRoot.AddComponent<Rigidbody>();
            var opponent = opponentRoot.AddComponent<OpponentCourseFollower>();
            var scenario = ego.AddComponent<HeadToHeadScenario>();
            SetField(scenario, "_egoBody", ego.GetComponent<Rigidbody>());
            SetField(scenario, "_opponents", new[] { opponent });

            Assert.That(scenario.InteractionEnabled, Is.False);
            scenario.SetInteractionEnabled(false);

            Assert.That(opponentRoot.activeSelf, Is.False);
            Assert.That(scenario.ObserveClosestOpponent().LongitudinalDistance, Is.Zero);
            scenario.SetInteractionEnabled(true);
            Assert.That(opponentRoot.activeSelf, Is.True);
            Object.DestroyImmediate(opponentRoot);
            Object.DestroyImmediate(ego);
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
