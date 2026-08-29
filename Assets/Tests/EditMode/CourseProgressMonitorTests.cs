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
    }
}
