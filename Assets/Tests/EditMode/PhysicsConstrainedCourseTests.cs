using NUnit.Framework;
using PhysicalAi.Course;
using PhysicalAi.Evaluation;
using UnityEngine;

namespace PhysicalAi.Tests.EditMode
{
    public class PhysicsConstrainedCourseTests
    {
        [Test]
        public void Generate_AllCornerSamplesAreTraversableAtDesignSpeed()
        {
            var settings = CourseGenerationSettings.Default;
            var course = PhysicsConstrainedCourseGenerator.Generate(42, settings, Vector3.zero, 0f);

            for (var i = 0; i < course.Centerline.Count; i++)
                Assert.That(CoursePhysicsConstraints.IsTraversable(settings.DesignSpeed, course.Curvature[i], settings.FrictionCoefficient, settings.MaximumBankDegrees), Is.True);
        }

        [Test]
        public void DefaultSettings_MatchMagpScaleAndVehicleSpeed()
        {
            var settings = CourseGenerationSettings.Default;

            Assert.That(settings.SampleSpacing, Is.EqualTo(0.1f));
            Assert.That(settings.MinimumCornerRadius, Is.EqualTo(1.4f));
            Assert.That(settings.MaximumCornerRadius, Is.EqualTo(4f));
            Assert.That(settings.DesignSpeed, Is.EqualTo(3.5f));
        }

        [Test]
        public void ClosedHoldoutGenerator_BuildsDeterministicClosedCircuit()
        {
            var first = ClosedHoldoutCourseGenerator.Generate(1009, CourseGenerationSettings.Default, Vector3.zero, 0f);
            var second = ClosedHoldoutCourseGenerator.Generate(1009, CourseGenerationSettings.Default, Vector3.zero, 0f);

            Assert.That(first.IsClosedLoop, Is.True);
            Assert.That(Vector3.Distance(first.Centerline[0], first.Centerline[first.Centerline.Count - 1]), Is.LessThan(0.0001f));
            Assert.That(first.Centerline.Count, Is.EqualTo(second.Centerline.Count));
            Assert.That(first.Centerline.Count, Is.GreaterThan(100));
        }

        [Test]
        public void StreamingState_AppendsContinuousDeterministicChunks()
        {
            var state = new StreamingCourseState();
            state.Reset(17, CourseGenerationSettings.Default, Vector3.zero, 0f);
            var first = state.Course;
            var firstCount = first.Centerline.Count;
            var tail = first.Centerline[firstCount - 1];

            var firstNewIndex = state.AppendNextChunk();

            Assert.That(firstNewIndex, Is.EqualTo(firstCount));
            Assert.That(state.ChunkCount, Is.EqualTo(2));
            Assert.That(state.Course.Centerline.Count, Is.GreaterThan(firstCount));
            Assert.That(state.Course.Centerline[firstNewIndex - 1], Is.EqualTo(tail));
        }

        [Test]
        public void StreamingState_TrimBoundsWaypointHistoryAndKeepsTail()
        {
            var state = new StreamingCourseState();
            state.Reset(17, CourseGenerationSettings.Default, Vector3.zero, 0f);
            state.AppendNextChunk();
            var retained = state.Course.Centerline[40];
            var count = state.Course.Centerline.Count;

            var removed = state.TrimBefore(40);

            Assert.That(removed, Is.EqualTo(40));
            Assert.That(state.Course.Centerline.Count, Is.EqualTo(count - 40));
            Assert.That(state.Course.Centerline[0], Is.EqualTo(retained));
        }

        [Test]
        public void Encode_TranslationOfCourseAndEgoDoesNotChangeObservation()
        {
            var course = PhysicsConstrainedCourseGenerator.Generate(8, CourseGenerationSettings.Default, Vector3.zero, 0f);
            var baseObservation = RelativeCourseObservation.Encode(course, Vector3.zero, Quaternion.identity, 0, 4);
            var translation = new Vector3(100f, 0f, -50f);
            var translatedCourse = PhysicsConstrainedCourseGenerator.Generate(8, CourseGenerationSettings.Default, translation, 0f);
            var translatedObservation = RelativeCourseObservation.Encode(translatedCourse, translation, Quaternion.identity, 0, 4);

            for (var i = 0; i < baseObservation.Length; i++)
            {
                Assert.That(Vector3.Distance(translatedObservation[i].LocalPosition, baseObservation[i].LocalPosition), Is.LessThan(0.0001f));
                Assert.That(translatedObservation[i].Curvature, Is.EqualTo(baseObservation[i].Curvature));
            }
        }

        [Test]
        public void GeneratedCourse_TranslateMovesEveryWaypointWithoutChangingRelativeObservation()
        {
            var course = PhysicsConstrainedCourseGenerator.Generate(8, CourseGenerationSettings.Default, Vector3.zero, 0f);
            var before = RelativeCourseObservation.Encode(course, Vector3.zero, Quaternion.identity, 0, 4);
            var offset = new Vector3(-75f, 0f, 120f);

            course.Translate(offset);
            var after = RelativeCourseObservation.Encode(course, offset, Quaternion.identity, 0, 4);

            Assert.That(course.Centerline[0], Is.EqualTo(offset));
            for (var index = 0; index < before.Length; index++)
                Assert.That(Vector3.Distance(before[index].LocalPosition, after[index].LocalPosition), Is.LessThan(0.0001f));
        }

        [Test]
        public void HoldoutReport_UsesMachineReadableFiniteValues()
        {
            var report = new HoldoutEvaluationReport(2, 0f, float.PositiveInfinity, 0.5f, 1f, 0.25f, 0.75f, 0.5f);
            var json = HoldoutEvaluationReportWriter.Serialize(report);

            StringAssert.Contains("\"episodeCount\": 2", json);
            StringAssert.Contains("\"meanCompletedLapTimeSeconds\": -1.0", json);
            StringAssert.DoesNotContain("Infinity", json);
            StringAssert.Contains("\"blindCornerHazardCollisionRate\": 0.25", json);
            StringAssert.Contains("\"opponentCollisionRate\": 0.75", json);
            StringAssert.Contains("\"timeoutRate\": 0.5", json);
        }
    }
}
