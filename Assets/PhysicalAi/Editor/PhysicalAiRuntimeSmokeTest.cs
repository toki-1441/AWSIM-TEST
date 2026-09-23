// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using PhysicalAi.Course;
using PhysicalAi.Planning;
using PhysicalAi.Randomization;
using PhysicalAi.Sensors;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PhysicalAi.Editor
{
    /// <summary>Batch-mode smoke test for the generated training scene's episode reset path.</summary>
    public static class PhysicalAiRuntimeSmokeTest
    {
        const string ScenePath = "Assets/PhysicalAi/Scenes/PhysicalAiTraining.unity";

        public static void Run()
        {
            EditorSceneManager.OpenScene(ScenePath);
            var randomizer = Object.FindFirstObjectByType<DomainRandomizer>();
            var orchestrator = Object.FindFirstObjectByType<PhysicalAiEpisodeOrchestrator>();
            var track = Object.FindFirstObjectByType<ProceduralCourseTrackBuilder>();
            var integrityLogger = Object.FindFirstObjectByType<StreamingCourseIntegrityLogger>();
            if (randomizer == null || orchestrator == null || track == null || integrityLogger == null)
                throw new System.InvalidOperationException("Physical AI training scene is not fully wired.");

            randomizer.SendMessage("Awake", SendMessageOptions.DontRequireReceiver);
            orchestrator.SendMessage("Awake", SendMessageOptions.DontRequireReceiver);
            integrityLogger.SendMessage("OnEnable", SendMessageOptions.DontRequireReceiver);
            var transitions = new List<StreamingCourseTransition>();
            orchestrator.StreamingTransitioned += transitions.Add;
            orchestrator.ResetEpisode(12345);
            if (orchestrator.Course == null || orchestrator.Course.Centerline.Count < 2 || track.SegmentCount < 1)
                throw new System.InvalidOperationException("Episode reset did not generate a collider-backed course.");

            var initialWaypointCount = orchestrator.Course.Centerline.Count;
            var initialSegmentCount = track.SegmentCount;
            var progress = Object.FindFirstObjectByType<CourseProgressMonitor>();
            var body = orchestrator.GetComponent<Rigidbody>();
            if (body == null)
                throw new System.InvalidOperationException("Training vehicle has no rigidbody.");
            body.position = orchestrator.Course.Centerline[initialWaypointCount - 2] + Vector3.up * 0.1f;
            var extended = orchestrator.RefreshStreamingCourseForCurrentPosition();
            var remainingWaypoints = progress == null ? 0 : orchestrator.Course.Centerline.Count - 1 - progress.NearestWaypointIndex;
            if (!extended || remainingWaypoints < 120 || track.SegmentCount < remainingWaypoints)
                throw new System.InvalidOperationException($"Streaming training road did not restore its forward horizon (extended {extended}, waypoints {initialWaypointCount}->{orchestrator.Course.Centerline.Count}, segments {initialSegmentCount}->{track.SegmentCount}, remaining {remainingWaypoints}).");

            // Exercise more than one lifecycle transition.  A streaming road can
            // look correct on its first append yet lose track geometry or waypoint
            // indices when passed chunks are discarded and its bounded history is
            // trimmed.  Each cycle places a stationary ego near the retained tail;
            // the production refresh path must append enough curved road ahead
            // before it can be reached again.
            for (var cycle = 0; cycle < 3; cycle++)
            {
                var courseBeforeCycle = orchestrator.Course;
                body.position = courseBeforeCycle.Centerline[courseBeforeCycle.Centerline.Count - 2] + Vector3.up * 0.1f;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                Physics.SyncTransforms();
                if (!orchestrator.RefreshStreamingCourseForCurrentPosition())
                    throw new System.InvalidOperationException($"Streaming training road did not append on lifecycle cycle {cycle}.");
                var forwardWaypoints = orchestrator.Course.Centerline.Count - 1 - progress.NearestWaypointIndex;
                if (forwardWaypoints < 120 || track.SegmentCount < forwardWaypoints)
                    throw new System.InvalidOperationException($"Streaming lifecycle cycle {cycle} lost its rendered/collider horizon (forward {forwardWaypoints}, segments {track.SegmentCount}).");
                if (body.linearVelocity.sqrMagnitude > 0.000001f || body.angularVelocity.sqrMagnitude > 0.000001f)
                    throw new System.InvalidOperationException($"Streaming lifecycle cycle {cycle} injected velocity into a stationary vehicle.");
            }

            // Earlier lifecycle cycles may already have rebased the course, so a
            // displacement relative to its first waypoint is not necessarily
            // beyond the world-origin threshold.  Put the body at an absolute
            // distant coordinate to exercise the production floating-origin path.
            body.position = new Vector3(100f, 0.1f, 0f);
            Physics.SyncTransforms();
            var transformFrame = Object.FindFirstObjectByType<TransformFrameSensor>();
            var odomPositionBeforeRebase = transformFrame == null ? Vector3.zero : transformFrame.Sample().LocalPosition;
            orchestrator.RefreshStreamingCourseForCurrentPosition();
            if (new Vector2(body.position.x, body.position.z).sqrMagnitude > 0.001f)
                throw new System.InvalidOperationException("Floating origin did not return the vehicle to the origin.");
            if (transformFrame != null && Vector3.Distance(transformFrame.Sample().LocalPosition, odomPositionBeforeRebase) > 0.0001f)
                throw new System.InvalidOperationException($"Floating origin changed the odometry TF position ({odomPositionBeforeRebase} -> {transformFrame.Sample().LocalPosition}).");
            if (!transitions.Exists(transition => transition.Kind == StreamingCourseTransitionKind.AppendAndRetire) ||
                !transitions.Exists(transition => transition.Kind == StreamingCourseTransitionKind.FloatingOriginRebase))
                throw new System.InvalidOperationException("Streaming smoke test did not instrument append and floating-origin transitions.");
            foreach (var transition in transitions)
            {
                if (transition.UnexpectedPositionDelta.sqrMagnitude > 0.00000001f ||
                    transition.RotationDeltaDegrees > 0.0001f ||
                    transition.LinearVelocityDelta.sqrMagnitude > 0.00000001f ||
                    transition.AngularVelocityDelta.sqrMagnitude > 0.00000001f ||
                    transition.ControlInputDelta.sqrMagnitude > 0.000000000001f ||
                    transition.ActualSteeringAngleDelta > 0.000001f ||
                    transition.MaximumAxleForceDelta > 0.000001f ||
                    transition.DynamicWeightDelta > 0.000001f)
                    throw new System.InvalidOperationException(
                        $"{transition.Kind} changed vehicle state: unexpected position {transition.UnexpectedPositionDelta}, " +
                        $"rotation {transition.RotationDeltaDegrees:F5}°, velocity {transition.LinearVelocityDelta}, angular {transition.AngularVelocityDelta}, " +
                        $"source {transition.CommandSource}, control {transition.ControlInputBefore}->{transition.ControlInputAfter}, " +
                        $"model planar-single-track, actual steering {transition.ActualSteeringAngleBefore:F6}->{transition.ActualSteeringAngleAfter:F6} rad, " +
                        $"axle force delta {transition.MaximumAxleForceDelta:F6} N, " +
                        $"dynamic weight delta {transition.DynamicWeightDelta:F6}.");
                if (transition.RetainedWaypointCount < 2 || transition.ForwardWaypointCount < 0 || transition.ChunkCount < 1)
                    throw new System.InvalidOperationException(
                        $"{transition.Kind} omitted streaming horizon evidence (retained {transition.RetainedWaypointCount}, " +
                        $"forward {transition.ForwardWaypointCount}, chunks {transition.ChunkCount}).");
                if (transition.Kind == StreamingCourseTransitionKind.AppendAndRetire &&
                    transition.AppendedCornerDirection == 0)
                    throw new System.InvalidOperationException("An appended streaming chunk did not report a curved primitive.");
            }
            if (integrityLogger.TransitionCount != transitions.Count || integrityLogger.DiscontinuityCount != 0)
                throw new System.InvalidOperationException(
                    $"Streaming integrity logger recorded {integrityLogger.TransitionCount} transitions and " +
                    $"{integrityLogger.DiscontinuityCount} discontinuities; expected {transitions.Count} continuous transitions.");

            // The fixed holdout course has no streaming state, but its progress
            // monitor still owns completion/off-course evaluation. Verify it is
            // updated through the same runtime call path rather than reporting a
            // permanent zero-progress timeout.
            orchestrator.ResetHoldoutEpisode(1009);
            body.position = orchestrator.Course.Centerline[20] + Vector3.up * 0.1f;
            Physics.SyncTransforms();
            orchestrator.RefreshStreamingCourseForCurrentPosition();
            if (orchestrator.ProgressMeters <= 0f)
                throw new System.InvalidOperationException("Finite holdout progress was not updated.");

            Debug.Log($"Physical AI smoke test passed: {track.SegmentCount} road segments generated after streaming extension and floating-origin shift.");
        }
    }
}
