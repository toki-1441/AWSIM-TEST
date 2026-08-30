using System.Collections.Generic;
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
        public void UpdatePosition_IgnoresSuspensionHeightWhenMeasuringDeparture()
        {
            var root = new GameObject("PlanarProgressTest");
            var monitor = root.AddComponent<CourseProgressMonitor>();
            monitor.SetCourse(PhysicsConstrainedCourseGenerator.Generate(1, CourseGenerationSettings.Default, Vector3.zero, 0f));

            monitor.UpdatePosition(new Vector3(0f, 2f, 0f));

            Assert.That(monitor.DistanceFromCenterline, Is.Zero.Within(0.0001f));
            Assert.That(monitor.IsOffCourse, Is.False);
            Object.DestroyImmediate(root);
        }

        [Test]
        public void UpdatePosition_TracksProgressAndRecognizesTheFinish()
        {
            var root = new GameObject("ProgressTest");
            var monitor = root.AddComponent<CourseProgressMonitor>();
            var points = new List<Vector3> { Vector3.zero, Vector3.forward, Vector3.forward * 2f };
            var course = new GeneratedCourse(points, new List<float> { 0f, 0f, 0f }, new List<float> { 1f, 1f, 1f });
            monitor.SetCourse(course);

            monitor.UpdatePosition(course.Centerline[0]);
            monitor.UpdatePosition(course.Centerline[1]);
            monitor.UpdatePosition(course.Centerline[course.Centerline.Count - 1]);

            Assert.That(monitor.ProgressMeters, Is.GreaterThan(0f));
            Assert.That(monitor.DeltaProgressMeters, Is.GreaterThan(0f));
            Assert.That(monitor.IsCompleted, Is.True);
            Object.DestroyImmediate(root);
        }

        [Test]
        public void UpdatePosition_OpenCourseCompletesWhenCrossingFinishWithinRoadWidth()
        {
            var root = new GameObject("OpenFinishLineTest");
            var monitor = root.AddComponent<CourseProgressMonitor>();
            var points = new List<Vector3> { Vector3.zero, Vector3.forward * 2f };
            monitor.SetCourse(new GeneratedCourse(points, new List<float> { 0f, 0f }, new List<float> { 1f, 1f }));

            monitor.UpdatePosition(points[0]);
            monitor.UpdatePosition(new Vector3(0.4f, 0f, 2f));

            Assert.That(monitor.IsOffCourse, Is.False);
            Assert.That(monitor.IsCompleted, Is.True);
            Object.DestroyImmediate(root);
        }

        [Test]
        public void UpdatePosition_OpenCourseDoesNotCompleteFromEndpointTeleport()
        {
            var root = new GameObject("OpenCourseTeleportTest");
            var monitor = root.AddComponent<CourseProgressMonitor>();
            var course = PhysicsConstrainedCourseGenerator.Generate(1, CourseGenerationSettings.Default, Vector3.zero, 0f);
            monitor.SetCourse(course);

            monitor.UpdatePosition(course.Centerline[course.Centerline.Count - 1]);

            Assert.That(monitor.IsCompleted, Is.False);
            Object.DestroyImmediate(root);
        }

        [Test]
        public void UpdatePosition_ClosedLoopCompletesAfterFinishApproachAndDuplicatedStartLine()
        {
            var root = new GameObject("ClosedLoopProgressTest");
            var monitor = root.AddComponent<CourseProgressMonitor>();
            var points = new List<Vector3>
            {
                Vector3.zero,
                Vector3.forward,
                Vector3.forward + Vector3.right,
                Vector3.right,
                Vector3.zero
            };
            var course = new GeneratedCourse(points, new List<float> { 0f, 0f, 0f, 0f, 0f }, new List<float> { 1f, 1f, 1f, 1f, 1f }, true);
            monitor.SetCourse(course);

            // The duplicated finish is accepted only after travelling around the
            // circuit; nearest-point index alone is insufficient for a closed loop.
            monitor.UpdatePosition(course.Centerline[0]);
            monitor.UpdatePosition(course.Centerline[1]);
            monitor.UpdatePosition(course.Centerline[2]);
            monitor.UpdatePosition(course.Centerline[course.Centerline.Count - 2]);
            Assert.That(monitor.IsCompleted, Is.False);
            monitor.UpdatePosition(course.Centerline[course.Centerline.Count - 1]);

            Assert.That(monitor.NearestWaypointIndex, Is.Zero);
            Assert.That(monitor.IsCompleted, Is.True);
            Object.DestroyImmediate(root);
        }

        [Test]
        public void UpdatePosition_ClosedLoopDoesNotCompleteFromStartLineTeleport()
        {
            var root = new GameObject("ClosedLoopStartTest");
            var monitor = root.AddComponent<CourseProgressMonitor>();
            var course = ClosedHoldoutCourseGenerator.Generate(1009, CourseGenerationSettings.Default, Vector3.zero, 0f);
            monitor.SetCourse(course);

            monitor.UpdatePosition(course.Centerline[course.Centerline.Count - 2]);
            monitor.UpdatePosition(course.Centerline[0]);

            Assert.That(monitor.IsCompleted, Is.False);
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
