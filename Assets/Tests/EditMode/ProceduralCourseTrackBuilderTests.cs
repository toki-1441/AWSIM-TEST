using NUnit.Framework;
using PhysicalAi.Course;
using UnityEngine;

namespace PhysicalAi.Tests.EditMode
{
    public class ProceduralCourseTrackBuilderTests
    {
        [Test]
        public void Build_CreatesVisualSegmentsWhileLeavingOneContinuousDrivingSurface()
        {
            var root = new GameObject("TrackTest");
            var builder = root.AddComponent<ProceduralCourseTrackBuilder>();
            var course = PhysicsConstrainedCourseGenerator.Generate(1, CourseGenerationSettings.Default, Vector3.zero, 0f);

            builder.Build(course);

            Assert.That(builder.SegmentCount, Is.EqualTo(course.Centerline.Count - 1));
            foreach (var segment in root.GetComponentsInChildren<CourseTrackSegment>())
            {
                if (!segment.IsRoad)
                    continue;
                Assert.That(segment.GetComponent<Collider>(), Is.Null,
                    "Road streaming markers must not compete with the continuous venue floor for wheel raycasts.");
            }
            var roadSurface = root.transform.Find("RoadSurface").GetComponent<MeshFilter>();
            Assert.That(roadSurface, Is.Not.Null);
            Assert.That(roadSurface.sharedMesh.vertexCount, Is.EqualTo(course.Centerline.Count * 2));
            var boundaries = root.GetComponentsInChildren<CourseBoundary>();
            Assert.That(boundaries, Has.Length.EqualTo(builder.SegmentCount * 2));
            foreach (var boundary in boundaries)
                Assert.That(boundary.GetComponent<BoxCollider>().enabled, Is.True,
                    "Course boundaries must physically block the vehicle body.");
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

        [Test]
        public void RebuiltSurface_AfterWorldRebaseRemainsAlignedWithAuthoritativeCourse()
        {
            var root = new GameObject("TrackTest");
            var builder = root.AddComponent<ProceduralCourseTrackBuilder>();
            var course = PhysicsConstrainedCourseGenerator.Generate(1, CourseGenerationSettings.Default, Vector3.zero, 0f);
            builder.Build(course);
            var offset = new Vector3(60f, 0f, -40f);

            course.Translate(offset);
            builder.Translate(offset);
            // Rebuilding after a rebase is the path used by streaming append and
            // discard.  The mesh must not acquire the root translation twice.
            builder.Build(course);

            var surface = root.transform.Find("RoadSurface").GetComponent<MeshFilter>();
            var vertices = surface.sharedMesh.vertices;
            var worldCentre = (surface.transform.TransformPoint(vertices[0]) + surface.transform.TransformPoint(vertices[1])) * 0.5f;
            Assert.That(Vector3.Distance(worldCentre, course.Centerline[0] + Vector3.up * 0.01f), Is.LessThan(0.0001f));
            Assert.That(Vector3.Distance(builder.CenterlineRenderer.GetPosition(0), course.Centerline[0] + Vector3.up * 0.013f), Is.LessThan(0.0001f));
            Object.DestroyImmediate(root);
        }

        [Test]
        public void Build_LeavesMicroJointsBetweenAdjacentBoundaryColliders()
        {
            var root = new GameObject("TrackTest");
            var builder = root.AddComponent<ProceduralCourseTrackBuilder>();
            var course = new GeneratedCourse(
                new System.Collections.Generic.List<Vector3> { Vector3.zero, Vector3.forward * 0.1f, Vector3.forward * 0.2f },
                new System.Collections.Generic.List<float> { 0f, 0f, 0f },
                new System.Collections.Generic.List<float> { 3f, 3f, 3f });

            builder.Build(course);

            var first = root.transform.Find("BoundaryLeft-0000");
            var second = root.transform.Find("BoundaryLeft-0001");
            // EditMode does not necessarily synchronise PhysX bounds after a
            // transform-scale write. The authored cube length is the source of
            // truth here; the PlayMode boundary test covers actual collision.
            Assert.That(first.localScale.z, Is.LessThan(0.1f));
            Assert.That(second.localScale.z, Is.LessThan(0.1f));
            Object.DestroyImmediate(root);
        }
    }
}
