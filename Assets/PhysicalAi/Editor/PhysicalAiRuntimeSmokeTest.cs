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

            Debug.Log($"Physical AI smoke test passed: {track.SegmentCount} road segments generated.");
        }
    }
}
