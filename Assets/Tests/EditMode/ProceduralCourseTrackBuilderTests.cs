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

        [Test]
        public void DiscardBefore_RemovesPassedRoadAndBoundaryColliders()
        {
            var root = new GameObject("TrackTest");
            var builder = root.AddComponent<ProceduralCourseTrackBuilder>();
            var course = PhysicsConstrainedCourseGenerator.Generate(1, CourseGenerationSettings.Default, Vector3.zero, 0f);
            builder.Build(course);
            var originalSegments = builder.SegmentCount;

            builder.DiscardBefore(30, 2);

            Assert.That(builder.SegmentCount, Is.LessThan(originalSegments));
            Assert.That(root.GetComponentsInChildren<CourseTrackSegment>().Length, Is.EqualTo(builder.SegmentCount * 3));
            Object.DestroyImmediate(root);
        }

        [Test]
        public void Translate_MovesColliderGeometryAndWorldSpaceCentrelineTogether()
        {
            var root = new GameObject("TrackTest");
            var builder = root.AddComponent<ProceduralCourseTrackBuilder>();
            var course = PhysicsConstrainedCourseGenerator.Generate(1, CourseGenerationSettings.Default, Vector3.zero, 0f);
            builder.Build(course);
            var road = root.GetComponentInChildren<CourseTrackSegment>();
            var roadPosition = road.transform.position;
            var linePosition = builder.CenterlineRenderer.GetPosition(0);
            var offset = new Vector3(60f, 0f, -40f);

            builder.Translate(offset);

            Assert.That(road.transform.position, Is.EqualTo(roadPosition + offset));
            Assert.That(builder.CenterlineRenderer.GetPosition(0), Is.EqualTo(linePosition + offset));
            Object.DestroyImmediate(root);
        }
    }
}
