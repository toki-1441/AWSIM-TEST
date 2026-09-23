// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;
using UnityEngine;

namespace PhysicalAi.Planning
{
    /// <summary>Operator-visible recording lifecycle; no hidden reset is required to open a file.</summary>
    public sealed class OperatorSessionHud : MonoBehaviour
    {
        KeyboardTeleoperationPlanner _driver;
        PlanningDemonstrationRecorder _recorder;
        PhysicalAiEpisodeOrchestrator _episode;
        string _tag = "";
        string _message = "";
        string _mapPath = "";
        public bool IsEditingText { get; private set; }
        public static bool IsTextInputControl(string name) => name == "OperatorEventTag" || name == "OperatorMapPath";
        void Awake()
        {
            _driver = GetComponent<KeyboardTeleoperationPlanner>();
            _recorder = GetComponent<PlanningDemonstrationRecorder>();
            _episode = GetComponent<PhysicalAiEpisodeOrchestrator>();
            _mapPath = "Build/maps/course-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmss") + ".json";
        }
        void OnGUI()
        {
            if (_driver == null || !_driver.IsActive || _episode == null || _recorder == null) return;
            var area = new Rect(12, 12, Mathf.Min(470, Screen.width - 24), 350);
            if (Event.current.type == EventType.MouseDown && !area.Contains(Event.current.mousePosition)) GUI.FocusControl(null);
            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.Label($"{_episode.MapMode} | seed {_episode.EpisodeSeed} | {_episode.VehicleModelName}");
            GUILayout.Label("W/S: accelerate/brake  A/D: steer  F8: record  R: restart");
            GUILayout.Label(_episode.EpisodeEnded ? "Episode ended — press R to restart." : $"Progress: {_episode.ProgressMeters:F1} m");
            GUILayout.BeginHorizontal();
            GUI.enabled = !_episode.EpisodeEnded;
            if (GUILayout.Button(_recorder.RecordingEnabled ? "Stop recording" : "Start recording"))
            { GUI.FocusControl(null); _driver.SetRecordingEnabled(!_recorder.RecordingEnabled); }
            GUI.enabled = true;
            if (GUILayout.Button("Restart (R)")) { GUI.FocusControl(null); _driver.Respawn(); }
            if (GUILayout.Button(Time.timeScale == 0 ? "Resume" : "Pause")) { GUI.FocusControl(null); Time.timeScale = Time.timeScale == 0 ? 1 : 0; }
            GUILayout.EndHorizontal();
            GUILayout.Label($"{(_recorder.RecordingEnabled ? "RECORDING" : "Stopped")} | records: {_recorder.FramesWritten}");
            GUILayout.Label(_recorder.CurrentPath ?? _recorder.OutputDirectory);
            GUILayout.BeginHorizontal();
            GUI.SetNextControlName("OperatorEventTag");
            _tag = GUILayout.TextField(_tag);
            GUI.enabled = _recorder.RecordingEnabled;
            if (GUILayout.Button("Add event tag", GUILayout.Width(120))) { GUI.FocusControl(null); _recorder.AddAnnotation(_tag); _tag = ""; }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.Label("Save current road geometry as a reusable fixed map:");
            GUI.SetNextControlName("OperatorMapPath");
            _mapPath = GUILayout.TextField(_mapPath);
            if (GUILayout.Button("Save map (new file)"))
            {
                GUI.FocusControl(null);
                try { _episode.CaptureMap().SaveNew(_mapPath); _message = "Saved map: " + Path.GetFullPath(_mapPath); }
                catch (Exception error) { _message = error.Message; }
            }
            GUILayout.Label(_recorder.LastError ?? _message);
            GUILayout.EndArea();
            IsEditingText = IsTextInputControl(GUI.GetNameOfFocusedControl());
            DrawSensorPreviews(gameObject);
        }

        public static void DrawSensorPreviews(GameObject vehicle)
        {
            var camera = vehicle.GetComponentInChildren<PhysicalAi.Sensors.CameraObservationSensor>();
            var episode = vehicle.GetComponent<PhysicalAiEpisodeOrchestrator>();
            if (camera != null && camera.DepthPreview != null)
            {
                GUI.Label(new Rect(Screen.width - 280, 235, 270, 24), "Metric depth (invalid = purple)");
                GUI.DrawTexture(new Rect(Screen.width - 280, 260, 260, 146), camera.DepthPreview, ScaleMode.ScaleToFit);
            }
            if (episode != null && episode.CurrentBev != null)
            {
                GUI.Label(new Rect(Screen.width - 280, 410, 270, 24), "BEV ground truth (forward = up)");
                GUI.DrawTexture(new Rect(Screen.width - 280, 435, 230, 230), episode.CurrentBev.Preview, ScaleMode.ScaleToFit);
            }
        }
        void OnDestroy() { Time.timeScale = 1; }
    }
}
