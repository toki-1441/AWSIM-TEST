using System.Collections.Generic;
using NUnit.Framework;
using PhysicalAi.Course;
using UnityEngine;

namespace PhysicalAi.Tests.EditMode
{
    /// <summary>
    /// The streaming instrumentation records the vehicle's state across a transition.
    /// It cannot see a kink in the generated geometry, because a wall can bend without
    /// the car touching it at that instant; the operator meets it later, as a force.
    /// These tests measure the built wall objects instead of the code that places them,
    /// so that reproducing the placement formula here cannot hide a defect in it.
    /// </summary>
    public class StreamingWallContinuityTests
    {
        [Test]
        public void BuiltWalls_MeetWithoutAStepTheVehicleCanCatchOn()
        {
            var streaming = new StreamingCourseState();
            streaming.Reset(4242, CourseGenerationSettings.Default, Vector3.zero, 0f);
            for (var chunk = 0; chunk < 4; chunk++)
                streaming.AppendNextChunk();

            var host = new GameObject("WallContinuityTrack");
            try
            {
                var builder = host.AddComponent<ProceduralCourseTrackBuilder>();
                builder.Build(streaming.Course);

                var worst = 0f;
                var worstName = string.Empty;
                foreach (var side in new[] { "BoundaryLeft", "BoundaryRight" })
                {
                    var walls = CollectWalls(host.transform, side);
                    for (var index = 1; index < walls.Count; index++)
                    {
                        // The far end face of one wall and the near end face of the next
                        // must describe the same corridor edge point.
                        var gap = Vector3.Distance(EndFace(walls[index - 1], +1f), EndFace(walls[index], -1f));
                        if (gap <= worst)
                            continue;
                        worst = gap;
                        worstName = walls[index].name;
                    }
                }

                // The body is 0.19 m wide in a 1.2 m corridor. Before the walls were
                // derived from shared centreline edge points this reached 31.7 mm on a
                // minimum-radius corner, which an operator feels as an impact.
                Assert.That(worst, Is.LessThan(0.01f),
                    $"largest wall seam step {worst * 1000f:F1} mm at {worstName}");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        static List<Transform> CollectWalls(Transform root, string prefix)
        {
            var walls = new List<Transform>();
            foreach (Transform child in root)
                if (child.name.StartsWith(prefix))
                    walls.Add(child);
            return walls;
        }

        /// <summary>Centre of a wall box's near or far end face, in world space.</summary>
        static Vector3 EndFace(Transform wall, float alongForward)
        {
            var halfLength = wall.localScale.z * 0.5f;
            var face = wall.position + wall.forward * (halfLength * alongForward);
            // Walls are lifted by half their height; compare them on the ground plane.
            face.y = 0f;
            return face;
        }
    }
}
