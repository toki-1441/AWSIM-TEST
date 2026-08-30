using NUnit.Framework;
using PhysicalAi.Evaluation;
using PhysicalAi.Course;
using UnityEngine;
using System.Collections.Generic;

namespace PhysicalAi.Tests.EditMode
{
    public class HoldoutCourseEvaluatorTests
    {
        [Test]
        public void Evaluate_AggregatesCompletionAndSafetyMetrics()
        {
            var report = HoldoutCourseEvaluator.Evaluate(new[]
            {
                new CourseEpisodeResult { CourseSeed = 1009, Completed = true, LapTimeSeconds = 10f },
                new CourseEpisodeResult { CourseSeed = 2027, Completed = true, LapTimeSeconds = 14f, CollisionCount = 1, BlindCornerHazardCollisionCount = 1 },
                new CourseEpisodeResult { CourseSeed = 3037, Completed = false, OffCourseCount = 1, OpponentCollisionCount = 1, TimeoutCount = 1 }
            });

            Assert.That(report.CompletionRate, Is.EqualTo(2f / 3f).Within(0.001f));
            Assert.That(report.MeanCompletedLapTimeSeconds, Is.EqualTo(12f));
            Assert.That(report.CollisionRate, Is.EqualTo(1f / 3f).Within(0.001f));
            Assert.That(report.OffCourseRate, Is.EqualTo(1f / 3f).Within(0.001f));
            Assert.That(report.BlindCornerHazardCollisionRate, Is.EqualTo(1f / 3f).Within(0.001f));
            Assert.That(report.OpponentCollisionRate, Is.EqualTo(1f / 3f).Within(0.001f));
            Assert.That(report.TimeoutRate, Is.EqualTo(1f / 3f).Within(0.001f));
        }

        [Test]
        public void HoldoutSeeds_AreRecognizedAndSeparated()
        {
            Assert.That(HoldoutCourseEvaluator.IsHoldoutSeed(1009), Is.True);
            Assert.That(HoldoutCourseEvaluator.IsHoldoutSeed(42), Is.False);
            Assert.That(HoldoutCourseSuite.TrainingEvaluationSeeds, Is.All.LessThan(1000));
        }

        [Test]
        public void Session_RejectsTrainingSeedsAndBuildsRuntimeResults()
        {
            var session = new HoldoutEvaluationSession();
            Assert.That(() => session.BeginEpisode(42, 1f), Throws.ArgumentException);

            session.BeginEpisode(1009, 4f);
            var result = session.CompleteEpisode(true, true, false, 15f, true, true, true);

            Assert.That(result.CourseSeed, Is.EqualTo(1009));
            Assert.That(result.LapTimeSeconds, Is.EqualTo(11f));
            Assert.That(result.CollisionCount, Is.EqualTo(1));
            Assert.That(result.BlindCornerHazardCollisionCount, Is.EqualTo(1));
            Assert.That(result.OpponentCollisionCount, Is.EqualTo(1));
            Assert.That(result.TimeoutCount, Is.EqualTo(1));
            Assert.That(session.BuildReport().CompletionRate, Is.EqualTo(1f));
        }

        [Test]
        public void TrainingDistributionSession_AcceptsDedicatedTrainingSeeds()
        {
            var session = new HoldoutEvaluationSession(requireHoldoutSeed: false);
            session.BeginEpisode(HoldoutCourseSuite.TrainingEvaluationSeeds[0], 1f);
            var result = session.CompleteEpisode(true, false, false, 2f);

            Assert.That(result.CourseSeed, Is.EqualTo(HoldoutCourseSuite.TrainingEvaluationSeeds[0]));
        }

        [Test]
        public void ReportSerialization_PreservesPerEpisodeEvidence()
        {
            var report = HoldoutCourseEvaluator.Evaluate(new[]
            {
                new CourseEpisodeResult { CourseSeed = 1009, Completed = true, LapTimeSeconds = 12f },
                new CourseEpisodeResult { CourseSeed = 2027, TimeoutCount = 1 }
            });
            var json = HoldoutEvaluationReportWriter.Serialize(report, new List<CourseEpisodeResult>
            {
                new CourseEpisodeResult { CourseSeed = 1009, Completed = true, LapTimeSeconds = 12f },
                new CourseEpisodeResult { CourseSeed = 2027, TimeoutCount = 1 }
            });

            Assert.That(json, Does.Contain("\"episodes\""));
            Assert.That(json, Does.Contain("\"courseSeed\": 1009"));
            Assert.That(json, Does.Contain("\"timeoutCount\": 1"));
        }

        [Test]
        public void FixedSeeds_GenerateClosedHoldoutCourses()
        {
            foreach (var seed in HoldoutCourseSuite.Seeds)
                Assert.That(ClosedHoldoutCourseGenerator.Generate(seed, CourseGenerationSettings.Default, Vector3.zero, 0f).IsClosedLoop, Is.True);
        }
    }
}
