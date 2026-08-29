// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using PhysicalAi.Course;
using PhysicalAi.Planning;
using PhysicalAi.Randomization;
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
            if (randomizer == null || orchestrator == null || track == null)
                throw new System.InvalidOperationException("Physical AI training scene is not fully wired.");

            randomizer.SendMessage("Awake", SendMessageOptions.DontRequireReceiver);
            orchestrator.SendMessage("Awake", SendMessageOptions.DontRequireReceiver);
            orchestrator.ResetEpisode(12345);
            if (orchestrator.Course == null || orchestrator.Course.Centerline.Count < 2 || track.SegmentCount < 1)
                throw new System.InvalidOperationException("Episode reset did not generate a collider-backed course.");

            var initialWaypointCount = orchestrator.Course.Centerline.Count;
            var initialSegmentCount = track.SegmentCount;
            var body = orchestrator.GetComponent<Rigidbody>();
            if (body == null)
                throw new System.InvalidOperationException("Training vehicle has no rigidbody.");
            body.position = orchestrator.Course.Centerline[initialWaypointCount - 2] + Vector3.up * 0.1f;
            orchestrator.SendMessage("FixedUpdate", SendMessageOptions.DontRequireReceiver);
            if (orchestrator.Course.Centerline.Count <= initialWaypointCount || track.SegmentCount <= initialSegmentCount)
                throw new System.InvalidOperationException("Streaming training road did not append a new chunk near its end.");

            var firstWaypoint = orchestrator.Course.Centerline[0];
            body.position = firstWaypoint + new Vector3(60f, 0.1f, 0f);
            orchestrator.SendMessage("FixedUpdate", SendMessageOptions.DontRequireReceiver);
            if (new Vector2(body.position.x, body.position.z).sqrMagnitude > 0.001f)
                throw new System.InvalidOperationException("Floating origin did not return the vehicle to the origin.");

            Debug.Log($"Physical AI smoke test passed: {track.SegmentCount} road segments generated after streaming extension and floating-origin shift.");
        }
    }
}
