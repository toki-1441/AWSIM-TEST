using NUnit.Framework;
using PhysicalAi.Course;
using PhysicalAi.Evaluation;
using UnityEngine;

namespace PhysicalAi.Tests.EditMode
{
    public class PhysicsConstrainedCourseTests
    {
        [Test]
        public void Generate_AllCornerSamplesAreTraversableAtDesignSpeed()
        {
            var settings = CourseGenerationSettings.Default;
            var course = PhysicsConstrainedCourseGenerator.Generate(42, settings, Vector3.zero, 0f);

            for (var i = 0; i < course.Centerline.Count; i++)
                Assert.That(CoursePhysicsConstraints.IsTraversable(settings.DesignSpeed, course.Curvature[i], settings.FrictionCoefficient, settings.MaximumBankDegrees), Is.True);
        }

        [Test]
        public void DefaultSettings_MatchMagpScaleAndVehicleSpeed()
        {
            var settings = CourseGenerationSettings.Default;

            Assert.That(settings.SampleSpacing, Is.EqualTo(0.1f));
            Assert.That(settings.MinimumCornerRadius, Is.EqualTo(1.4f));
            Assert.That(settings.MaximumCornerRadius, Is.EqualTo(4f));
            Assert.That(settings.DesignSpeed, Is.EqualTo(3.5f));
        }

        [Test]
        public void StreamingState_PlanarSegmentDistanceUsesXZCoordinates()
        {
            var distanceSquared = StreamingCourseState.PlanarSegmentDistanceSquared(
                new Vector3(0f, 100f, 10f), new Vector3(1f, -100f, 10f),
                new Vector3(0f, -50f, 12f), new Vector3(1f, 10f, 12f));

            Assert.That(distanceSquared, Is.EqualTo(4f).Within(0.000001f),
                "Course clearance must ignore Y and must not implicitly convert Vector3 to the XY plane.");
        }

        [Test]
        public void ClosedHoldoutGenerator_BuildsDeterministicClosedCircuit()
        {
            var first = ClosedHoldoutCourseGenerator.Generate(1009, CourseGenerationSettings.Default, Vector3.zero, 0f);
            var second = ClosedHoldoutCourseGenerator.Generate(1009, CourseGenerationSettings.Default, Vector3.zero, 0f);

            Assert.That(first.IsClosedLoop, Is.True);
            Assert.That(Vector3.Distance(first.Centerline[0], first.Centerline[first.Centerline.Count - 1]), Is.LessThan(0.0001f));
            Assert.That(first.Centerline.Count, Is.EqualTo(second.Centerline.Count));
            Assert.That(first.Centerline.Count, Is.GreaterThan(100));
        }

        [Test]
        public void StreamingState_AppendsContinuousDeterministicChunks()
        {
            var state = new StreamingCourseState();
            state.Reset(17, CourseGenerationSettings.Default, Vector3.zero, 0f);
            var first = state.Course;
            var firstCount = first.Centerline.Count;
            var tail = first.Centerline[firstCount - 1];

            var firstNewIndex = state.AppendNextChunk();

            Assert.That(firstNewIndex, Is.EqualTo(firstCount));
            Assert.That(state.ChunkCount, Is.EqualTo(2));
            Assert.That(state.Course.Centerline.Count, Is.GreaterThan(firstCount));
            Assert.That(state.Course.Centerline[firstNewIndex - 1], Is.EqualTo(tail));
        }

        [Test]
        public void StreamingState_ProducesCurvedNonIntersectingTrainingRoad()
        {
            var state = new StreamingCourseState();
            state.Reset(71, CourseGenerationSettings.Default, Vector3.zero, 0f);
            for (var index = 0; index < 15; index++)
                state.AppendNextChunk();

            Assert.That(state.Course.Curvature, Has.Some.Not.EqualTo(0f),
                "The endless training distribution must include corners, not a straight-only fallback.");
            Assert.That(state.ChunkCount, Is.EqualTo(16));
            for (var index = 0; index < state.Course.Centerline.Count; index++)
                Assert.That(CoursePhysicsConstraints.IsTraversable(
                    CourseGenerationSettings.Default.DesignSpeed,
                    state.Course.Curvature[index],
                    CourseGenerationSettings.Default.FrictionCoefficient,
                    CourseGenerationSettings.Default.MaximumBankDegrees), Is.True,
                    $"Streaming waypoint {index} exceeds the design-speed curvature limit.");
            AssertNoNonAdjacentSegmentIntersection(state.Course);
            AssertNoSeparatedCorridorOverlap(state.Course);
        }

        [Test]
        public void StreamingState_EachAppendedChunkContainsAParameterizedCorner()
        {
            // Intersection rejection must never turn the endless distribution
            // into an unnoticed straight-only escape route.  Inspect every
            // appended range, not merely the combined course where one old bend
            // could hide many later straight chunks.
            var state = new StreamingCourseState();
            state.Reset(71, CourseGenerationSettings.Default, Vector3.zero, 0f);
            var firstIndex = 0;
            for (var chunk = 0; chunk < 16; chunk++)
            {
                var lastIndex = state.Course.Curvature.Count;
                var containsCorner = false;
                for (var index = firstIndex; index < lastIndex; index++)
                    containsCorner |= Mathf.Abs(state.Course.Curvature[index]) > 0.00001f;
                Assert.That(containsCorner, Is.True, $"Streaming chunk {chunk} lost its required curved primitive.");
                firstIndex = lastIndex - 1;
                if (chunk < 15)
                    state.AppendNextChunk();
            }
        }

        [Test]
        public void StreamingState_KeepsCurvedCorridorsSeparatedAcrossSeedSet()
        {
            // A single curated seed cannot prove that rejection keeps working as
            // chunks are appended.  This is deliberately a small deterministic
            // sample of independent courses, long enough to expose a later chunk
            // folding into a previous corridor.
            const int maximumRetainedWaypoints = 400;
            for (var seed = 1; seed <= 24; seed++)
            {
                var state = new StreamingCourseState();
                state.Reset(seed, CourseGenerationSettings.Default, Vector3.zero, 0f);
                var sawLeftCorner = false;
                var sawRightCorner = false;
                RecordCornerDirections();
                for (var chunk = 1; chunk < 48; chunk++)
                {
                    try
                    {
                        state.AppendNextChunk();
                    }
                    catch (System.InvalidOperationException exception)
                    {
                        Assert.Fail($"Seed {seed}, chunk {chunk}: {exception.Message}");
                    }
                    RecordCornerDirections();
                    // This mirrors the runtime's bounded retained geometry: the
                    // course is endless, while geometry safely passed behind the
                    // vehicle is no longer relevant to collision admission.
                    if (state.Course.Centerline.Count > maximumRetainedWaypoints)
                        state.TrimBefore(state.Course.Centerline.Count - maximumRetainedWaypoints);
                }

                Assert.That(state.Course.Curvature, Has.Some.Not.EqualTo(0f),
                    $"Seed {seed} degraded to a straight-only course.");
                Assert.That(sawLeftCorner, Is.True, $"Seed {seed} never generated a left corner.");
                Assert.That(sawRightCorner, Is.True, $"Seed {seed} never generated a right corner.");
                AssertNoNonAdjacentSegmentIntersection(state.Course);
                AssertNoSeparatedCorridorOverlap(state.Course);

                void RecordCornerDirections()
                {
                    for (var index = 0; index < state.Course.Curvature.Count; index++)
                    {
                        sawLeftCorner |= state.Course.Curvature[index] > 0.00001f;
                        sawRightCorner |= state.Course.Curvature[index] < -0.00001f;
                    }
                }
            }
        }

        [Test]
        public void StreamingState_RebaseKeepsFutureGenerationTranslationInvariant()
        {
            var reference = new StreamingCourseState();
            var rebased = new StreamingCourseState();
            var settings = CourseGenerationSettings.Default;
            reference.Reset(93, settings, Vector3.zero, 0f);
            rebased.Reset(93, settings, Vector3.zero, 0f);
            for (var chunk = 1; chunk < 8; chunk++)
            {
                reference.AppendNextChunk();
                rebased.AppendNextChunk();
            }

            var offset = new Vector3(-37.5f, 0f, 24.25f);
            rebased.Translate(offset);
            for (var chunk = 8; chunk < 32; chunk++)
            {
                reference.AppendNextChunk();
                rebased.AppendNextChunk();
            }

            Assert.That(rebased.ChunkCount, Is.EqualTo(reference.ChunkCount));
            Assert.That(rebased.Course.Centerline.Count, Is.EqualTo(reference.Course.Centerline.Count));
            var maximumTranslationError = 0f;
            for (var index = 0; index < reference.Course.Centerline.Count; index++)
                maximumTranslationError = Mathf.Max(maximumTranslationError,
                    Vector3.Distance(rebased.Course.Centerline[index], reference.Course.Centerline[index] + offset));
            Assert.That(maximumTranslationError, Is.LessThan(0.002f),
                "Floating-origin generation changed the course by more than the 2 mm geometry tolerance.");
        }

        [Test]
        public void StreamingState_TrimBoundsWaypointHistoryAndKeepsTail()
        {
            var state = new StreamingCourseState();
            state.Reset(17, CourseGenerationSettings.Default, Vector3.zero, 0f);
            state.AppendNextChunk();
            var retained = state.Course.Centerline[40];
            var count = state.Course.Centerline.Count;

            var removed = state.TrimBefore(40);

            Assert.That(removed, Is.EqualTo(40));
            Assert.That(state.Course.Centerline.Count, Is.EqualTo(count - 40));
            Assert.That(state.Course.Centerline[0], Is.EqualTo(retained));
        }

        [Test]
        public void Encode_TranslationOfCourseAndEgoDoesNotChangeObservation()
        {
            var course = PhysicsConstrainedCourseGenerator.Generate(8, CourseGenerationSettings.Default, Vector3.zero, 0f);
            var baseObservation = RelativeCourseObservation.Encode(course, Vector3.zero, Quaternion.identity, 0, 4);
            var translation = new Vector3(100f, 0f, -50f);
            var translatedCourse = PhysicsConstrainedCourseGenerator.Generate(8, CourseGenerationSettings.Default, translation, 0f);
            var translatedObservation = RelativeCourseObservation.Encode(translatedCourse, translation, Quaternion.identity, 0, 4);

            for (var i = 0; i < baseObservation.Length; i++)
            {
                Assert.That(Vector3.Distance(translatedObservation[i].LocalPosition, baseObservation[i].LocalPosition), Is.LessThan(0.0001f));
                Assert.That(translatedObservation[i].Curvature, Is.EqualTo(baseObservation[i].Curvature));
            }
        }

        [Test]
        public void GeneratedCourse_TranslateMovesEveryWaypointWithoutChangingRelativeObservation()
        {
            var course = PhysicsConstrainedCourseGenerator.Generate(8, CourseGenerationSettings.Default, Vector3.zero, 0f);
            var before = RelativeCourseObservation.Encode(course, Vector3.zero, Quaternion.identity, 0, 4);
            var offset = new Vector3(-75f, 0f, 120f);

            course.Translate(offset);
            var after = RelativeCourseObservation.Encode(course, offset, Quaternion.identity, 0, 4);

            Assert.That(course.Centerline[0], Is.EqualTo(offset));
            for (var index = 0; index < before.Length; index++)
                Assert.That(Vector3.Distance(before[index].LocalPosition, after[index].LocalPosition), Is.LessThan(0.0001f));
        }

        [Test]
        public void HoldoutReport_UsesMachineReadableFiniteValues()
        {
            var report = new HoldoutEvaluationReport(2, 0f, float.PositiveInfinity, 0.5f, 1f, 0.25f, 0.75f, 0.5f);
            var json = HoldoutEvaluationReportWriter.Serialize(report);

            StringAssert.Contains("\"episodeCount\": 2", json);
            StringAssert.Contains("\"meanCompletedLapTimeSeconds\": -1.0", json);
            StringAssert.DoesNotContain("Infinity", json);
            StringAssert.Contains("\"blindCornerHazardCollisionRate\": 0.25", json);
            StringAssert.Contains("\"opponentCollisionRate\": 0.75", json);
            StringAssert.Contains("\"timeoutRate\": 0.5", json);
        }

        static void AssertNoNonAdjacentSegmentIntersection(GeneratedCourse course)
        {
            // Only consecutive segments belong to the same topological edge.
            // A true centreline crossing must never be hidden by a broad local
            // exemption, even when it occurs in neighbouring primitives.
            const int adjacentSamples = 1;
            for (var later = 0; later < course.Centerline.Count - 1; later++)
                for (var earlier = 0; earlier < later - adjacentSamples; earlier++)
                    Assert.That(SegmentsIntersect(course.Centerline[earlier], course.Centerline[earlier + 1], course.Centerline[later], course.Centerline[later + 1]), Is.False,
                        $"Non-adjacent streaming segments {earlier} and {later} intersect.");
        }

        static bool SegmentsIntersect(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            var ab = new Vector2(b.x - a.x, b.z - a.z);
            var cd = new Vector2(d.x - c.x, d.z - c.z);
            var ac = new Vector2(c.x - a.x, c.z - a.z);
            var denominator = ab.x * cd.y - ab.y * cd.x;
            if (Mathf.Abs(denominator) < 0.000001f)
                return false;
            var t = (ac.x * cd.y - ac.y * cd.x) / denominator;
            var u = (ac.x * ab.y - ac.y * ab.x) / denominator;
            return t >= 0f && t <= 1f && u >= 0f && u <= 1f;
        }

        static void AssertNoSeparatedCorridorOverlap(GeneratedCourse course)
        {
            // This mirrors the streaming admission envelope: 1.25 m covers the
            // road, duct wall, TT-02 half-width and a safety margin.  The closest
            // The closest eight metres are one continuously swept corridor. True
            // centreline crossings are checked separately with only directly
            // connected segments exempted.
            const int connectionSamples = 80;
            const float clearance = 1.25f;
            for (var later = 0; later < course.Centerline.Count - 1; later++)
                for (var earlier = 0; earlier < later - connectionSamples; earlier++)
                    Assert.That(SegmentDistanceSquared(course.Centerline[earlier], course.Centerline[earlier + 1], course.Centerline[later], course.Centerline[later + 1]), Is.GreaterThanOrEqualTo(clearance * clearance),
                        $"Streaming corridors {earlier} and {later} violate the {clearance:F2} m clearance.");
        }

        static float SegmentDistanceSquared(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            var p = new Vector2(a.x, a.z);
            var r = new Vector2(b.x - a.x, b.z - a.z);
            var q = new Vector2(c.x, c.z);
            var s = new Vector2(d.x - c.x, d.z - c.z);
            var cross = Cross(r, s);
            var qMinusP = q - p;
            if (Mathf.Abs(cross) > 0.000001f)
            {
                var t = Cross(qMinusP, s) / cross;
                var u = Cross(qMinusP, r) / cross;
                if (t >= 0f && t <= 1f && u >= 0f && u <= 1f)
                    return 0f;
            }
            return Mathf.Min(
                Mathf.Min(PointSegmentDistanceSquared(p, q, q + s), PointSegmentDistanceSquared(p + r, q, q + s)),
                Mathf.Min(PointSegmentDistanceSquared(q, p, p + r), PointSegmentDistanceSquared(q + s, p, p + r)));
        }

        static float PointSegmentDistanceSquared(Vector2 point, Vector2 start, Vector2 end)
        {
            var delta = end - start;
            if (delta.sqrMagnitude < 0.000001f)
                return (point - start).sqrMagnitude;
            var parameter = Mathf.Clamp01(Vector2.Dot(point - start, delta) / delta.sqrMagnitude);
            return (point - (start + delta * parameter)).sqrMagnitude;
        }

        static float Cross(Vector2 left, Vector2 right) => left.x * right.y - left.y * right.x;
    }
}
