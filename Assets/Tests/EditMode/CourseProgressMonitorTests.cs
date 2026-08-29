using NUnit.Framework;
using PhysicalAi.Course;
using UnityEngine;

namespace PhysicalAi.Tests.EditMode
{
    public class CourseProgressMonitorTests
    {
        [Test]
        public void UpdatePosition_RecognizesOnCourseAndOffCoursePositions()
        {
            var root = new GameObject("ProgressTest");
            var monitor = root.AddComponent<CourseProgressMonitor>();
            monitor.SetCourse(PhysicsConstrainedCourseGenerator.Generate(1, CourseGenerationSettings.Default, Vector3.zero, 0f));

            monitor.UpdatePosition(Vector3.zero);
            Assert.That(monitor.IsOffCourse, Is.False);

            monitor.UpdatePosition(new Vector3(20f, 0f, 0f));
            Assert.That(monitor.IsOffCourse, Is.True);
            Object.DestroyImmediate(root);
        }

        [Test]
        public void UpdatePosition_TracksProgressAndRecognizesTheFinish()
        {
            var root = new GameObject("ProgressTest");
            var monitor = root.AddComponent<CourseProgressMonitor>();
            var course = PhysicsConstrainedCourseGenerator.Generate(1, CourseGenerationSettings.Default, Vector3.zero, 0f);
            monitor.SetCourse(course);

            monitor.UpdatePosition(course.Centerline[course.Centerline.Count - 1]);

            Assert.That(monitor.ProgressMeters, Is.GreaterThan(0f));
            Assert.That(monitor.DeltaProgressMeters, Is.EqualTo(monitor.ProgressMeters));
            Assert.That(monitor.IsCompleted, Is.True);
            Object.DestroyImmediate(root);
        }

        [Test]
        public void TrimCourse_PreservesLifetimeProgressWhileRebasingWaypointIndices()
        {
            var root = new GameObject("ProgressTest");
            var monitor = root.AddComponent<CourseProgressMonitor>();
            var streaming = new StreamingCourseState();
            streaming.Reset(4, CourseGenerationSettings.Default, Vector3.zero, 0f);
            streaming.AppendNextChunk();
            monitor.SetCourse(streaming.Course);
            monitor.UpdatePosition(streaming.Course.Centerline[60]);
            var progress = monitor.ProgressMeters;

            var removed = streaming.TrimBefore(40);
            monitor.TrimCourse(streaming.Course, removed);

            Assert.That(monitor.NearestWaypointIndex, Is.EqualTo(20));
            Assert.That(monitor.ProgressMeters, Is.EqualTo(progress).Within(0.0001f));
            Assert.That(monitor.DeltaProgressMeters, Is.Zero);
            Object.DestroyImmediate(root);
        }
    }
}
