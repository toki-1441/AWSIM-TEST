using NUnit.Framework;
using PhysicalAi.Course;
using UnityEngine;

namespace PhysicalAi.Tests.EditMode
{
    public class ProceduralCourseTrackBuilderTests
    {
        [Test]
        public void Build_CreatesColliderBackedSegmentForEachCenterlineSegment()
        {
            var root = new GameObject("TrackTest");
            var builder = root.AddComponent<ProceduralCourseTrackBuilder>();
            var course = PhysicsConstrainedCourseGenerator.Generate(1, CourseGenerationSettings.Default, Vector3.zero, 0f);

            builder.Build(course);

            Assert.That(builder.SegmentCount, Is.EqualTo(course.Centerline.Count - 1));
            Assert.That(root.GetComponentInChildren<BoxCollider>(), Is.Not.Null);
            Assert.That(root.GetComponentsInChildren<CourseBoundary>(), Has.Length.EqualTo(builder.SegmentCount * 2));
            Assert.That(builder.CenterlineRenderer, Is.Not.Null);
            Assert.That(builder.CenterlineRenderer.positionCount, Is.EqualTo(course.Centerline.Count));
            Object.DestroyImmediate(root);
        }
    }
}
