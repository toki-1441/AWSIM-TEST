// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;
using UnityEngine;

namespace PhysicalAi.Planning
{
    [Serializable]
    public class PlanningDemonstrationFrame
    {
        public int EpisodeId;
        public int Step;
        public float SimulationTime;
        public float[] Observation;
        public PlanningCommand Command;
        public float Reward;
        public bool Terminal;
    }

    /// <summary>Line-delimited JSON dataset writer for human teleoperation BC demonstrations.</summary>
    public sealed class PlanningDemonstrationRecorder : MonoBehaviour
    {
        [SerializeField] string _datasetDirectory = "PhysicalAiDemonstrations";
        [SerializeField] string _filePrefix = "teleop";

        StreamWriter _writer;
        int _episodeId;
        int _step;

        public string CurrentPath { get; private set; }

        public void BeginEpisode(int episodeId)
        {
            EndEpisode(false);
            _episodeId = episodeId;
            _step = 0;
            var directory = Path.Combine(Application.persistentDataPath, _datasetDirectory);
            Directory.CreateDirectory(directory);
            var fileName = $"{Sanitize(_filePrefix)}-episode-{episodeId:D6}-{DateTime.UtcNow:yyyyMMddTHHmmssfff}.jsonl";
            CurrentPath = Path.Combine(directory, fileName);
            _writer = new StreamWriter(CurrentPath, false);
        }

        public void Record(PlanningObservation observation, PlanningCommand command, float reward)
        {
            if (_writer == null)
                throw new InvalidOperationException("Call BeginEpisode before recording a demonstration.");

            WriteFrame(new PlanningDemonstrationFrame
            {
                EpisodeId = _episodeId,
                Step = _step++,
                SimulationTime = Time.time,
                Observation = observation.Features,
                Command = command,
                Reward = reward,
                Terminal = false
            });
        }

        public void EndEpisode(bool terminal = true)
        {
            if (_writer == null)
                return;
            if (terminal)
                WriteFrame(new PlanningDemonstrationFrame { EpisodeId = _episodeId, Step = _step, SimulationTime = Time.time, Terminal = true });
            _writer.Dispose();
            _writer = null;
        }

        void OnDisable() => EndEpisode(false);

        void WriteFrame(PlanningDemonstrationFrame frame)
        {
            _writer.WriteLine(JsonUtility.ToJson(frame));
            _writer.Flush();
        }

        static string Sanitize(string value)
        {
            foreach (var invalid in Path.GetInvalidFileNameChars())
                value = value.Replace(invalid, '_');
            return string.IsNullOrWhiteSpace(value) ? "teleop" : value;
        }
    }
}
