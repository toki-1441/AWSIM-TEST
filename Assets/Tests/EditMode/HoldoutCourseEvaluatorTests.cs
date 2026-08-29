using NUnit.Framework;
using PhysicalAi.Evaluation;

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
                new CourseEpisodeResult { CourseSeed = 2027, Completed = true, LapTimeSeconds = 14f, CollisionCount = 1 },
                new CourseEpisodeResult { CourseSeed = 3037, Completed = false, OffCourseCount = 1 }
            });

            Assert.That(report.CompletionRate, Is.EqualTo(2f / 3f).Within(0.001f));
            Assert.That(report.MeanCompletedLapTimeSeconds, Is.EqualTo(12f));
            Assert.That(report.CollisionRate, Is.EqualTo(1f / 3f).Within(0.001f));
            Assert.That(report.OffCourseRate, Is.EqualTo(1f / 3f).Within(0.001f));
        }

        [Test]
        public void HoldoutSeeds_AreRecognizedAndSeparated()
        {
            Assert.That(HoldoutCourseEvaluator.IsHoldoutSeed(1009), Is.True);
            Assert.That(HoldoutCourseEvaluator.IsHoldoutSeed(42), Is.False);
        }
    }
}
