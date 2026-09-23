// Copyright 2026
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace PhysicalAi.Planning
{
    /// <summary>Owns only the Python process it starts. All Unity/GUI work stays on the main thread.</summary>
    public sealed class OperatorRlSession : MonoBehaviour
    {
        [Serializable] class ReadyMessage { public string type; public int port; public int completedEpisodes; }
        [Serializable] class LaunchRecord { public string[] arguments; public string resumeFrom; public string python; public string trainer; }
        readonly ConcurrentQueue<string> _lines = new ConcurrentQueue<string>();
        Process _process;
        StreamWriter _log;
        PythonRlBridge _bridge;
        float _startedAt;
        string _resumePath = "";
        string _lastLine = "";
        string _state = "Stopped";
        public string State => _state;
        public string LastError { get; private set; }
        public string RunDirectory { get; private set; }
        public string CheckpointPath => RunDirectory == null ? null : Path.Combine(RunDirectory, "checkpoint.pt");
        public bool IsRunning => _state == "Starting" || _state == "Running";

        void Start()
        {
            _bridge = GetComponent<PythonRlBridge>();
            _bridge.TrainingCompleted += Complete;
            _resumePath = PhysicalAiLaunchArguments.GetValue("-physicalAiRlResume") ?? "";
            Time.timeScale = 0;
            if (PhysicalAiLaunchArguments.GetValue("-physicalAiRlAutoStart") != "false") StartTraining(_resumePath);
        }

        public void StartTraining(string resumeFrom = null)
        {
            if (IsRunning) return;
            Time.timeScale = 0;
            LastError = null;
            _lastLine = "";
            try
            {
                if (PhysicalAiLaunchArguments.HasFlag("-physicalAiImageBlackout"))
                    throw new ArgumentException("Managed visual RL requires camera images; use the explicit external ablation workflow for image blackout.");
                var trainer = Path.GetFullPath(PhysicalAiLaunchArguments.GetValue("-physicalAiRlTrainerScript") ?? "scripts/train_vision_rl.py");
                var python = PhysicalAiLaunchArguments.GetValue("-physicalAiRlPython") ?? "python3";
                if (!File.Exists(trainer)) throw new FileNotFoundException("Trainer script not found. Select its path in launch settings.", trainer);
                if (!string.IsNullOrWhiteSpace(resumeFrom))
                {
                    resumeFrom = Path.GetFullPath(resumeFrom);
                    if (!File.Exists(resumeFrom)) throw new FileNotFoundException("Resume checkpoint not found.", resumeFrom);
                }
                var output = Path.GetFullPath(PhysicalAiLaunchArguments.GetValue("-physicalAiRlOutputDirectory") ?? "Build/rl");
                RunDirectory = Path.Combine(output, "rl-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                Directory.CreateDirectory(RunDirectory);
                _log = new StreamWriter(new FileStream(Path.Combine(RunDirectory, "trainer.log"), FileMode.CreateNew, FileAccess.Write)) { AutoFlush = true };
                File.WriteAllText(Path.Combine(RunDirectory, "launch.json"), JsonUtility.ToJson(new LaunchRecord
                { arguments = PhysicalAiLaunchArguments.Current, resumeFrom = resumeFrom, python = python, trainer = trainer }, true));
                var args = new List<string> { "-u", trainer, "--port", PhysicalAiLaunchArguments.GetInt("-physicalAiRlPort", 5015).ToString(),
                    "--device", PhysicalAiLaunchArguments.GetValue("-physicalAiRlDevice") ?? "cpu", "--require-contract",
                    "--seed", PhysicalAiLaunchArguments.GetInt("-physicalAiSeed", 1).ToString(),
                    "--output", CheckpointPath, "--metrics", Path.Combine(RunDirectory, "metrics.jsonl") };
                if (!string.IsNullOrWhiteSpace(resumeFrom)) { args.Add("--resume"); args.Add(resumeFrom); }
                _process = new Process { StartInfo = new ProcessStartInfo
                {
                    FileName = python, Arguments = QuoteArguments(args), WorkingDirectory = Environment.CurrentDirectory,
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
                }};
                _process.OutputDataReceived += (_, line) => { if (line.Data != null) _lines.Enqueue(line.Data); };
                _process.ErrorDataReceived += (_, line) => { if (line.Data != null) _lines.Enqueue("stderr: " + line.Data); };
                _process.Start();
                _process.BeginOutputReadLine(); _process.BeginErrorReadLine();
                _state = "Starting";
                _startedAt = Time.realtimeSinceStartup;
            }
            catch (Exception error) { Fail(error.Message); }
        }

        // ProcessStartInfo does not invoke a shell. Quote each argument independently for Mono's parser.
        public static string QuoteArguments(IEnumerable<string> arguments)
        {
            var quoted = new List<string>();
            foreach (var argument in arguments) quoted.Add("\"" + argument.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"");
            return string.Join(" ", quoted);
        }

        void Update()
        {
            while (_lines.TryDequeue(out var line))
            {
                _lastLine = line;
                _log?.WriteLine(line);
                if (_state != "Starting" || !line.StartsWith("{")) continue;
                try
                {
                    var ready = JsonUtility.FromJson<ReadyMessage>(line);
                    if (ready == null || ready.type != "ready") continue;
                    if (ready.port != PhysicalAiLaunchArguments.GetInt("-physicalAiRlPort", 5015)) throw new IOException("Trainer started on an unexpected port.");
                    if (!_bridge.Connect()) throw new IOException(_bridge.LastError);
                    File.WriteAllText(Path.Combine(RunDirectory, "environment.json"), JsonUtility.ToJson(_bridge.EnvironmentContract(), true));
                    _bridge.StartTrainingEpisodes();
                    _state = "Running";
                    Time.timeScale = 1;
                }
                catch (Exception error) { Fail(error.Message); }
            }
            if (!IsRunning) return;
            if (_process == null || _process.HasExited) Fail("Trainer exited. " + _lastLine);
            else if (_state == "Starting" && Time.realtimeSinceStartup - _startedAt > 45) Fail("Trainer startup timed out; inspect trainer.log and the selected Python environment.");
            else if (_state == "Running" && !_bridge.IsConnected) Fail(_bridge.LastError ?? "RL connection lost.");
        }

        public void StopTraining()
        {
            if (!IsRunning) return;
            _bridge.StopTraining();
            GetComponent<PlanningDemonstrationRecorder>()?.SuspendTrainingSegment();
            StopProcess();
            _state = _bridge.LastError == null ? "Stopped" : "Error";
            LastError = _bridge.LastError;
            if (File.Exists(CheckpointPath)) _resumePath = CheckpointPath;
        }
        void Complete()
        {
            StopTraining();
            if (LastError == null)
            {
                if (!File.Exists(CheckpointPath)) Fail("Training ended without a checkpoint. No successful learning run is being claimed.");
                else _state = "Completed";
            }
        }
        void Fail(string message)
        {
            Time.timeScale = 0;
            LastError = message;
            _state = "Error";
            _bridge?.StopTraining();
            GetComponent<PlanningDemonstrationRecorder>()?.SuspendTrainingSegment();
            _log?.WriteLine("SESSION ERROR: " + message);
            StopProcess();
            UnityEngine.Debug.LogWarning("Physical AI training stopped: " + message);
            if (PhysicalAiLaunchArguments.HasFlag("-physicalAiQuitAfterEvaluation")) Application.Quit(2);
        }
        void StopProcess()
        {
            if (_process != null)
            {
                try { if (!_process.HasExited) { _process.Kill(); _process.WaitForExit(500); } }
                catch (InvalidOperationException) { }
                _process.Dispose(); _process = null;
            }
            while (_lines.TryDequeue(out var line)) _log?.WriteLine(line);
            _log?.Dispose(); _log = null;
        }
        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(12, 12, Mathf.Min(690, Screen.width - 24), 320), GUI.skin.box);
            GUILayout.Label($"Visual RL — {_state} | completed episodes: {_bridge?.CompletedEpisodes ?? 0}");
            GUILayout.Label("RGB + planning vector → speed/lateral target. Depth/BEV remain separate labels.");
            GUILayout.Label("Stop saves an episode-boundary checkpoint; resume restores optimizer, statistics and RNG.");
            GUILayout.BeginHorizontal();
            GUI.enabled = !IsRunning;
            if (GUILayout.Button("Start new training")) StartTraining();
            GUI.enabled = IsRunning;
            if (GUILayout.Button("Stop and save")) StopTraining();
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.Label("Resume checkpoint (a new output run is created):");
            _resumePath = GUILayout.TextField(_resumePath);
            GUI.enabled = !IsRunning && !string.IsNullOrWhiteSpace(_resumePath);
            if (GUILayout.Button("Resume training")) StartTraining(_resumePath);
            GUI.enabled = true;
            GUILayout.Label(RunDirectory ?? "No training run yet.");
            GUILayout.Label(File.Exists(CheckpointPath) ? "Checkpoint saved: " + CheckpointPath : "No completed checkpoint yet.");
            GUILayout.Label(LastError ?? _lastLine);
            GUILayout.EndArea();
            OperatorSessionHud.DrawSensorPreviews(gameObject);
        }
        void OnApplicationQuit() { if (IsRunning) StopTraining(); }
        void OnDisable()
        {
            if (_bridge != null) _bridge.TrainingCompleted -= Complete;
            StopProcess();
            Time.timeScale = 1;
        }
    }
}
