// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace PhysicalAi.Configuration
{
    /// <summary>Portable configuration. Relative paths use the launch working directory.</summary>
    [Serializable]
    public sealed class SimulatorLaunchSettings
    {
        public int schemaVersion = 1;
        public string mode = "manual";
        public string scenario = "nominal";
        public string mapMode = "endless";
        public string mapPath = "";
        public int seed = 1;
        public string vehicleModel = "assisted";
        public string outputDirectory = "Build/datasets";
        public bool recordOnStart;
        public bool rgb = true;
        public bool depth;
        public bool bev;
        public float depthMaxMeters = 30;
        public int bevSize = 128;
        public float bevMetersPerPixel = 0.1f;
        public int imageWidth = 320;
        public int imageHeight = 180;
        public int captureHz = 15;
        public float cameraHeightMeters = 0.15f;
        public float cameraForwardMeters = 0.077f;
        public float cameraLateralMeters = 0f;
        public float cameraPitchDegrees = 22f;
        public float cameraYawDegrees = 0f;
        public float cameraRollDegrees = 0f;
        public int rlPort = 5015;
        public bool rlManaged = true;
        public bool rlAutoStart = true;
        public string rlPython = "python3";
        public string rlTrainerScript = "scripts/train_vision_rl.py";
        public string rlOutputDirectory = "Build/rl";
        public string rlResumeCheckpoint = "";
        public string rlDevice = "cpu";
        public int episodeCount = 8;
        public float maximumEpisodeSeconds = 120;

        /// <summary>Validate resolved CLI values before any scene components can clamp or ignore them.</summary>
        public static SimulatorLaunchSettings ValidateCurrentArguments()
        {
            var settings = new SimulatorLaunchSettings();
            void Bind(string option, string fieldName)
            {
                if (Array.IndexOf(RuntimeArguments.Current, option) < 0) return;
                var field = typeof(SimulatorLaunchSettings).GetField(fieldName);
                object value;
                if (field.FieldType == typeof(int)) value = RuntimeArguments.GetInt(option, 0);
                else if (field.FieldType == typeof(float)) value = RuntimeArguments.GetFloat(option, 0);
                else if (field.FieldType == typeof(bool))
                {
                    var args = RuntimeArguments.Current;
                    var index = Array.LastIndexOf(args, option);
                    var bareSwitch = option == "-physicalAiRecordOnStart";
                    if (bareSwitch && (index + 1 == args.Length || args[index + 1].StartsWith("-"))) value = true;
                    else
                    {
                        var text = RuntimeArguments.GetValue(option);
                        if (text != "true" && text != "false") throw new ArgumentException(option + " must be true or false.");
                        value = text == "true";
                    }
                }
                else value = RuntimeArguments.GetValue(option);
                field.SetValue(settings, value);
            }
            Bind("-physicalAiMode", nameof(mode));
            Bind("-physicalAiDemonstrationScenario", nameof(scenario));
            Bind("-physicalAiMapMode", nameof(mapMode));
            Bind("-physicalAiMapPath", nameof(mapPath));
            Bind("-physicalAiSeed", nameof(seed));
            Bind("-physicalAiVehicleModel", nameof(vehicleModel));
            Bind("-physicalAiDemonstrationDirectory", nameof(outputDirectory));
            Bind("-physicalAiRecordOnStart", nameof(recordOnStart));
            Bind("-physicalAiRecordRgb", nameof(rgb));
            Bind("-physicalAiDepth", nameof(depth));
            Bind("-physicalAiBev", nameof(bev));
            Bind("-physicalAiDepthMaxMeters", nameof(depthMaxMeters));
            Bind("-physicalAiBevSize", nameof(bevSize));
            Bind("-physicalAiBevMetersPerPixel", nameof(bevMetersPerPixel));
            Bind("-physicalAiCameraWidth", nameof(imageWidth));
            Bind("-physicalAiCameraHeight", nameof(imageHeight));
            Bind("-physicalAiCameraHz", nameof(captureHz));
            Bind("-physicalAiCameraHeightMeters", nameof(cameraHeightMeters));
            Bind("-physicalAiCameraForwardMeters", nameof(cameraForwardMeters));
            Bind("-physicalAiCameraLateralMeters", nameof(cameraLateralMeters));
            Bind("-physicalAiCameraPitchDegrees", nameof(cameraPitchDegrees));
            Bind("-physicalAiCameraYawDegrees", nameof(cameraYawDegrees));
            Bind("-physicalAiCameraRollDegrees", nameof(cameraRollDegrees));
            Bind("-physicalAiRlPort", nameof(rlPort));
            Bind("-physicalAiRlManaged", nameof(rlManaged));
            Bind("-physicalAiRlAutoStart", nameof(rlAutoStart));
            Bind("-physicalAiRlPython", nameof(rlPython));
            Bind("-physicalAiRlTrainerScript", nameof(rlTrainerScript));
            Bind("-physicalAiRlOutputDirectory", nameof(rlOutputDirectory));
            Bind("-physicalAiRlResume", nameof(rlResumeCheckpoint));
            Bind("-physicalAiRlDevice", nameof(rlDevice));
            Bind("-physicalAiEpisodeCount", nameof(episodeCount));
            Bind("-physicalAiMaximumEpisodeSeconds", nameof(maximumEpisodeSeconds));
            settings.Validate();
            return settings;
        }

        public void Validate()
        {
            if (schemaVersion != 1) throw new ArgumentException("Unsupported launch configuration version.");
            OneOf(mode, "mode", "manual", "rl", "evaluation");
            OneOf(scenario, "scenario", "nominal", "blind-corner", "head-to-head");
            OneOf(mapMode, "map mode", "fixed", "endless");
            OneOf(vehicleModel, "vehicle model", "assisted", "dynamic");
            if (seed < 0) throw new ArgumentException("Seed must be nonnegative.");
            if (!float.IsFinite(cameraHeightMeters) || cameraHeightMeters <= 0 ||
                !float.IsFinite(cameraForwardMeters) || !float.IsFinite(cameraLateralMeters) ||
                !float.IsFinite(cameraPitchDegrees) || !float.IsFinite(cameraYawDegrees) || !float.IsFinite(cameraRollDegrees))
                throw new ArgumentException("Camera mount values must be finite, with positive height above the ground.");
            if (string.IsNullOrWhiteSpace(outputDirectory)) throw new ArgumentException("Choose an output directory.");
            if (!string.IsNullOrEmpty(mapPath) && mapMode != "fixed") throw new ArgumentException("A map file requires fixed-map mode.");
            if (imageWidth < 16 || imageWidth > 1920 || imageHeight < 16 || imageHeight > 1080 || captureHz < 1 || captureHz > 60)
                throw new ArgumentException("Image size must be 16..1920 by 16..1080, capture rate 1..60 Hz.");
            if (!float.IsFinite(depthMaxMeters) || depthMaxMeters <= 0 || !float.IsFinite(bevMetersPerPixel) ||
                bevMetersPerPixel <= 0 || bevSize < 16 || bevSize > 512)
                throw new ArgumentException("Invalid depth range or BEV grid settings.");
            if (rlPort < 1 || rlPort > 65535 || episodeCount < 1 || episodeCount > 999 || float.IsNaN(maximumEpisodeSeconds) ||
                float.IsInfinity(maximumEpisodeSeconds) || maximumEpisodeSeconds <= 0)
                throw new ArgumentException("Invalid RL port, episode count or time limit.");
            OneOf(rlDevice, "trainer device", "cpu", "cuda", "auto");
            if (mode == "rl" && rlManaged && (string.IsNullOrWhiteSpace(rlPython) ||
                string.IsNullOrWhiteSpace(rlTrainerScript) || string.IsNullOrWhiteSpace(rlOutputDirectory)))
                throw new ArgumentException("Choose a Python executable, trainer script and RL output directory.");
        }

        public string[] ToArguments()
        {
            Validate();
            var args = new List<string>();
            void Add(string flag, object value) { args.Add(flag); args.Add(Convert.ToString(value, CultureInfo.InvariantCulture)); }
            Add("-physicalAiMode", mode);
            Add("-physicalAiMapMode", mapMode);
            Add("-physicalAiSeed", seed);
            if (!string.IsNullOrEmpty(mapPath)) Add("-physicalAiMapPath", mapPath);
            Add("-physicalAiVehicleModel", vehicleModel);
            Add("-physicalAiDemonstrationDirectory", outputDirectory);
            Add("-physicalAiDemonstrationScenario", scenario);
            Add("-physicalAiHazardProbability", scenario == "blind-corner" ? 1 : 0);
            if (scenario == "head-to-head") args.Add("-physicalAiHeadToHead");
            if (recordOnStart) args.Add("-physicalAiRecordOnStart");
            Add("-physicalAiRecordRgb", rgb ? "true" : "false");
            Add("-physicalAiDepth", depth ? "true" : "false");
            Add("-physicalAiBev", bev ? "true" : "false");
            Add("-physicalAiDepthMaxMeters", depthMaxMeters);
            Add("-physicalAiBevSize", bevSize);
            Add("-physicalAiBevMetersPerPixel", bevMetersPerPixel);
            Add("-physicalAiCameraWidth", imageWidth);
            Add("-physicalAiCameraHeight", imageHeight);
            Add("-physicalAiCameraHz", captureHz);
            Add("-physicalAiCameraHeightMeters", cameraHeightMeters);
            Add("-physicalAiCameraForwardMeters", cameraForwardMeters);
            Add("-physicalAiCameraLateralMeters", cameraLateralMeters);
            Add("-physicalAiCameraPitchDegrees", cameraPitchDegrees);
            Add("-physicalAiCameraYawDegrees", cameraYawDegrees);
            Add("-physicalAiCameraRollDegrees", cameraRollDegrees);
            Add("-physicalAiMaximumEpisodeSeconds", maximumEpisodeSeconds);
            Add("-physicalAiEpisodeCount", episodeCount);
            if (mode == "rl")
            {
                Add("-physicalAiRlPort", rlPort);
                Add("-physicalAiRlManaged", rlManaged ? "true" : "false");
                Add("-physicalAiRlAutoStart", rlAutoStart ? "true" : "false");
                Add("-physicalAiRlPython", rlPython);
                Add("-physicalAiRlTrainerScript", rlTrainerScript);
                Add("-physicalAiRlOutputDirectory", rlOutputDirectory);
                Add("-physicalAiRlDevice", rlDevice);
                if (!string.IsNullOrEmpty(rlResumeCheckpoint)) Add("-physicalAiRlResume", rlResumeCheckpoint);
            }
            if (mode != "manual") Add("-physicalAiEvaluation", "rollout");
            return args.ToArray();
        }
        public void Save(string path)
        {
            Validate();
            var fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, JsonUtility.ToJson(this, true));
        }
        public static SimulatorLaunchSettings Load(string path)
        {
            var result = JsonUtility.FromJson<SimulatorLaunchSettings>(File.ReadAllText(path));
            if (result == null) throw new ArgumentException("Empty launch configuration.");
            result.Validate();
            return result;
        }
        static void OneOf(string value, string name, params string[] choices)
        {
            if (Array.IndexOf(choices, value) < 0) throw new ArgumentException($"Unknown {name}: {value}.");
        }
    }
}
