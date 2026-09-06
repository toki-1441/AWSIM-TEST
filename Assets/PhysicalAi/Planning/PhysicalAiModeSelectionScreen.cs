// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using UnityEngine;
using UnityEngine.SceneManagement;

namespace PhysicalAi.Planning
{
    /// <summary>
    /// The screen an operator sees when the player starts without arguments.
    /// It is drawn with IMGUI so that it needs no prefab, scene asset or UI
    /// framework, matching the convention that the Physical AI scene is generated
    /// from code. Its content comes entirely from <see cref="PhysicalAiModeCatalog"/>.
    /// </summary>
    public sealed class PhysicalAiModeSelectionScreen : MonoBehaviour
    {
        public const string TrainingSceneName = "PhysicalAiTraining";
        public const string AutowareDemoSceneName = "AutowareSimulationDemo";

        PhysicalAiMode _mode;
        PhysicalAiModeVariant _variant;
        Vector2 _scroll;

        /// <summary>Creates the screen in the current scene.</summary>
        public static PhysicalAiModeSelectionScreen Create()
        {
            var host = new GameObject("PhysicalAiModeSelection");
            DontDestroyOnLoad(host);
            return host.AddComponent<PhysicalAiModeSelectionScreen>();
        }

        /// <summary>Applies a selection and loads its scene. Public so tests can drive it without the GUI.</summary>
        public static void Launch(PhysicalAiMode mode, PhysicalAiModeVariant variant)
        {
            var arguments = PhysicalAiModeCatalog.Resolve(mode, variant);
            PhysicalAiLaunchArguments.Override(arguments);
            SceneManager.LoadScene(
                mode.Id == "autoware-demo" ? AutowareDemoSceneName : TrainingSceneName,
                LoadSceneMode.Single);
        }

        /// <summary>
        /// The host survives the scene load so the selection can outlive the bootstrap
        /// scene, which means it has to remove itself once a choice has been made.
        /// Otherwise the menu keeps drawing over the simulation the operator selected.
        /// </summary>
        void LaunchAndDismiss(PhysicalAiMode mode, PhysicalAiModeVariant variant)
        {
            Launch(mode, variant);
            Destroy(gameObject);
        }

        void OnGUI()
        {
            const float width = 640f;
            const float height = 520f;
            var area = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.Label("Physical AI — choose how to start", GUI.skin.label);
            GUILayout.Space(8f);
            _scroll = GUILayout.BeginScrollView(_scroll);

            if (_mode == null)
                DrawModeList();
            else
                DrawVariantList();

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void DrawModeList()
        {
            foreach (var mode in PhysicalAiModeCatalog.Modes)
            {
                if (GUILayout.Button($"{mode.Title}\n{mode.Description}", GUILayout.Height(54f)))
                {
                    if (mode.Variants.Count == 0)
                    {
                        LaunchAndDismiss(mode, null);
                        return;
                    }
                    _mode = mode;
                    _variant = null;
                }
            }
        }

        void DrawVariantList()
        {
            GUILayout.Label(_mode.Title, GUI.skin.label);
            foreach (var variant in _mode.Variants)
            {
                if (!GUILayout.Button(variant.Title, GUILayout.Height(32f)))
                    continue;
                _variant = variant;
            }

            GUILayout.Space(8f);
            if (_variant != null)
            {
                // Showing the equivalent command line keeps an interactively explored
                // configuration reproducible by an automated run.
                GUILayout.Label("Equivalent command line:");
                GUILayout.TextField(PhysicalAiModeCatalog.DescribeCommandLine(_mode, _variant));
                if (GUILayout.Button("Start", GUILayout.Height(32f)))
                {
                    LaunchAndDismiss(_mode, _variant);
                    return;
                }
            }

            if (GUILayout.Button("Back", GUILayout.Height(24f)))
            {
                _mode = null;
                _variant = null;
            }
        }
    }
}
