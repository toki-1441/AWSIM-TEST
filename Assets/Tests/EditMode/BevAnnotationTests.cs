// Copyright 2026
// SPDX-License-Identifier: Apache-2.0
using NUnit.Framework;
using PhysicalAi.Course;
using PhysicalAi.Planning;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using PhysicalAi.Racing;
using PhysicalAi.Safety;

namespace PhysicalAi.Tests.EditMode
{
    public class BevAnnotationTests
    {
        [Test]
        public void HazardsAndOpponentsHaveDistinctClassesAndEgoFrameBoxPoses()
        {
            var ego = new GameObject("BevEgo");
            var hazard = new GameObject("BevHazard");
            var opponent = new GameObject("BevOpponent");
            var capture = new BevAnnotationCapture(32, 0.1f);
            Texture2D preview = null;
            try
            {
                ego.transform.SetPositionAndRotation(new Vector3(4, 0, 3), Quaternion.Euler(0, 90, 0));
                ego.AddComponent<BoxCollider>().size = Vector3.one * 0.3f;
                hazard.AddComponent<EpisodeHazard>();
                hazard.AddComponent<BoxCollider>().size = Vector3.one * 0.4f;
                hazard.transform.position = ego.transform.TransformPoint(new Vector3(0, 0, 1));
                opponent.AddComponent<OpponentCourseFollower>();
                opponent.AddComponent<BoxCollider>().size = Vector3.one * 0.4f;
                opponent.transform.SetPositionAndRotation(ego.transform.TransformPoint(new Vector3(0, 0, -1)),
                    ego.transform.rotation * Quaternion.Euler(0, 30, 0));
                var frame = capture.Capture(null, ego.transform, 5, 2);
                preview = frame.Preview;
                Assert.That(frame.Labels[6 * 32 + 16], Is.EqualTo(3));
                Assert.That(frame.Labels[26 * 32 + 16], Is.EqualTo(4));
                Assert.That(frame.Labels[16 * 32 + 16], Is.EqualTo(5));
                var box = frame.Annotations.objects.Single(value => value.objectName == "BevOpponent");
                Assert.That(Vector3.Distance(box.centerEgo, new Vector3(0, 0, -1)), Is.LessThan(1e-5));
                Assert.That(Quaternion.Angle(box.rotationEgo, Quaternion.Euler(0, 30, 0)), Is.LessThan(0.01));
                hazard.SetActive(false);
                opponent.transform.position += Vector3.forward * 100;
                frame = capture.Capture(null, ego.transform, 6, 3);
                Assert.That(frame.Annotations.objects.Any(value => value.classId == 3 || value.classId == 4), Is.False);
            }
            finally
            {
                if (preview != null) Object.DestroyImmediate(preview);
                Object.DestroyImmediate(ego); Object.DestroyImmediate(hazard); Object.DestroyImmediate(opponent);
            }
        }

        [Test]
        public void EgoCentricBevUsesRenderedRoadAndRealBoxesWithForwardAtImageTop()
        {
            var ego = new GameObject("BevEgo");
            var track = new GameObject("BevTrack");
            var wall = new GameObject("BevWall");
            var capture = new BevAnnotationCapture(32, 0.1f);
            try
            {
                ego.AddComponent<BoxCollider>().size = new Vector3(0.2f, 0.1f, 0.4f);
                var builder = track.AddComponent<ProceduralCourseTrackBuilder>();
                var points = new List<Vector3> { new Vector3(0,0,-2), new Vector3(0,0,0), new Vector3(0,0,2) };
                builder.Build(new GeneratedCourse(points, new List<float> { 0,0,0 }, new List<float> { 3,3,3 }));
                wall.transform.SetParent(track.transform);
                wall.transform.position = new Vector3(0.3f, 0, 0.8f);
                wall.AddComponent<BoxCollider>().size = new Vector3(0.2f, 0.1f, 0.2f);
                wall.AddComponent<CourseBoundary>();
                var frame = capture.Capture(builder, ego.transform, 11, 0.5f);
                Assert.That(frame.Annotations.frameIndex, Is.EqualTo(11));
                Assert.That(frame.Labels[16 * 32 + 16], Is.EqualTo(5));
                Assert.That(frame.Labels[4 * 32 + 16], Is.EqualTo(1));
                Assert.That(frame.Labels[8 * 32 + 19], Is.EqualTo(2));
                Assert.That(frame.Labels[4 * 32 + 2], Is.Zero);
                var before = (byte[])frame.Labels.Clone();
                var shift = new Vector3(-100, 0, 20);
                // Shift the exact same scene geometry and ego; the ego-frame labels must not change.
                track.transform.position += shift; ego.transform.position += shift;
                var after = capture.Capture(builder, ego.transform, 12, 0.6f);
                Assert.That(after.Labels, Is.EqualTo(before));
            }
            finally
            {
                // Dispose's deferred texture release is for runtime; destroy the fixture texture immediately here.
                var frame = capture.Capture(null, ego.transform, 0, 0);
                Object.DestroyImmediate(frame.Preview);
                Object.DestroyImmediate(ego); Object.DestroyImmediate(track);
            }
        }
    }
}
