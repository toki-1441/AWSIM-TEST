// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using System;
using PhysicalAi.Configuration;
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
        SimulatorLaunchSettings _settings = new SimulatorLaunchSettings();
        string _configPath = "Build/config/operator.json";
        string _message = "";
        readonly NumericInputBuffer _numericInputs = new NumericInputBuffer();

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

        public static void Launch(SimulatorLaunchSettings settings)
        {
            settings.Validate();
            if (!string.IsNullOrEmpty(settings.mapPath)) PhysicalAi.Course.CourseMapDocument.Load(settings.mapPath);
            PhysicalAiLaunchArguments.Override(settings.ToArguments());
            SceneManager.LoadScene(TrainingSceneName, LoadSceneMode.Single);
        }

        public static void ShowError(string error) => Create()._message = error;

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
            var width = Mathf.Min(780f, Screen.width - 24f);
            var height = Mathf.Min(780f, Screen.height - 24f);
            var area = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.Label("Physical AI — choose how to start", GUI.skin.label);
            GUILayout.BeginHorizontal();
            _configPath = GUILayout.TextField(_configPath);
            if (GUILayout.Button("Load config", GUILayout.Width(110)))
            {
                try
                {
                    _settings = SimulatorLaunchSettings.Load(_configPath);
                    _numericInputs.Clear();
                    _mode = PhysicalAiModeCatalog.Find(_settings.mode == "manual" ? "manual-drive" : _settings.mode == "rl" ? "rl-rollout" : "evaluation");
                    _variant = null;
                    _message = "Configuration loaded.";
                }
                catch (Exception error) { _message = error.Message; }
            }
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(_message)) GUILayout.Label(_message);
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
                    if (mode.Id == "autoware-demo")
                    {
                        LaunchAndDismiss(mode, null);
                        return;
                    }
                    _mode = mode;
                    _variant = null;
                    _settings.mode = mode.Id == "manual-drive" ? "manual" : mode.Id == "rl-rollout" ? "rl" : "evaluation";
                }
            }
        }

        void DrawVariantList()
        {
            _numericInputs.BeginDraw();
            GUILayout.Label(_mode.Title, GUI.skin.label);
            _settings.scenario = Choose("Scenario", _settings.scenario, "nominal", "blind-corner", "head-to-head");
            _settings.mapMode = Choose("Map", _settings.mapMode, "endless", "fixed");
            _settings.vehicleModel = Choose("Vehicle", _settings.vehicleModel, "assisted", "dynamic");
            _settings.seed = IntField("Seed", _settings.seed);
            if (_settings.mapMode == "fixed")
            {
                GUILayout.Label("Map file (blank = generate a closed course from the seed):");
                _settings.mapPath = GUILayout.TextField(_settings.mapPath);
            }
            else _settings.mapPath = "";
            GUILayout.Label("Dataset output directory:");
            _settings.outputDirectory = GUILayout.TextField(_settings.outputDirectory);
            _settings.recordOnStart = GUILayout.Toggle(_settings.recordOnStart, "Record on start");
            _settings.rgb = GUILayout.Toggle(_settings.rgb, "Record RGB camera");
            _settings.depth = GUILayout.Toggle(_settings.depth, "Metric camera depth + validity mask");
            _settings.bev = GUILayout.Toggle(_settings.bev, "BEV ground truth + object annotations");
            _settings.imageWidth = IntField("Image width", _settings.imageWidth);
            _settings.imageHeight = IntField("Image height", _settings.imageHeight);
            _settings.captureHz = IntField("Capture Hz", _settings.captureHz);
            GUILayout.Label("Fixed camera mount (metres / degrees; positive pitch looks down):");
            _settings.cameraHeightMeters = FloatField("Height above ground", _settings.cameraHeightMeters);
            _settings.cameraForwardMeters = FloatField("Forward offset", _settings.cameraForwardMeters);
            _settings.cameraLateralMeters = FloatField("Right offset", _settings.cameraLateralMeters);
            _settings.cameraPitchDegrees = FloatField("Pitch", _settings.cameraPitchDegrees);
            _settings.cameraYawDegrees = FloatField("Yaw", _settings.cameraYawDegrees);
            _settings.cameraRollDegrees = FloatField("Roll", _settings.cameraRollDegrees);
            if (_settings.depth) _settings.depthMaxMeters = FloatField("Depth range (m)", _settings.depthMaxMeters);
            if (_settings.bev)
            {
                _settings.bevSize = IntField("BEV grid size", _settings.bevSize);
                _settings.bevMetersPerPixel = FloatField("BEV metres/pixel", _settings.bevMetersPerPixel);
            }
            if (_settings.mode != "manual")
            {
                _settings.episodeCount = IntField("Episodes", _settings.episodeCount);
                _settings.maximumEpisodeSeconds = FloatField("Seconds per episode", _settings.maximumEpisodeSeconds);
            }
            if (_settings.mode == "rl")
            {
                _settings.rlPort = IntField("Python bridge port", _settings.rlPort);
                _settings.rlManaged = GUILayout.Toggle(_settings.rlManaged, "Manage the Python trainer from this player");
                if (_settings.rlManaged)
                {
                    _settings.rlAutoStart = GUILayout.Toggle(_settings.rlAutoStart, "Start training when the scene opens");
                    GUILayout.Label("Python executable (environment with torch, numpy and Pillow):");
                    _settings.rlPython = GUILayout.TextField(_settings.rlPython);
                    GUILayout.Label("Trainer script:");
                    _settings.rlTrainerScript = GUILayout.TextField(_settings.rlTrainerScript);
                    GUILayout.Label("Training output directory (each start creates a new run):");
                    _settings.rlOutputDirectory = GUILayout.TextField(_settings.rlOutputDirectory);
                    GUILayout.Label("Resume checkpoint (blank = new training):");
                    _settings.rlResumeCheckpoint = GUILayout.TextField(_settings.rlResumeCheckpoint);
                    _settings.rlDevice = Choose("Trainer device", _settings.rlDevice, "cpu", "cuda", "auto");
                }
                else GUILayout.Label("External server mode: start the Python policy server first; connection errors stop the simulation.");
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save config (replace)"))
            {
                try { _numericInputs.Validate(); _settings.Save(_configPath); _message = "Saved. CLI: Build/AWSIM.x86_64 -physicalAiConfig \"" + _configPath + "\""; }
                catch (Exception error) { _message = error.Message; }
            }
            if (GUILayout.Button("Start", GUILayout.Height(32f)))
            {
                try { _numericInputs.Validate(); Launch(_settings); Destroy(gameObject); }
                catch (Exception error) { _message = error.Message; }
            }
            GUILayout.EndHorizontal();

            if (GUILayout.Button("Back", GUILayout.Height(24f)))
            {
                _mode = null;
                _variant = null;
            }
        }

        static string Choose(string label, string selected, params string[] choices)
        {
            GUILayout.Label(label);
            return choices[GUILayout.SelectionGrid(Mathf.Max(0, Array.IndexOf(choices, selected)), choices, choices.Length)];
        }

        int IntField(string label, int value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(155));
            var text = GUILayout.TextField(_numericInputs.Text(label, value));
            GUILayout.EndHorizontal();
            return _numericInputs.Integer(label, text, value);
        }

        float FloatField(string label, float value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(155));
            var text = GUILayout.TextField(_numericInputs.Text(label, value));
            GUILayout.EndHorizontal();
            return _numericInputs.Float(label, text, value);
        }
    }
}
