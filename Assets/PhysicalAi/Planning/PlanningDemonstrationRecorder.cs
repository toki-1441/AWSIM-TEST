// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;
using PhysicalAi.Sensors;
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
        public int CameraFrameIndex = -1;
        public string CameraJpegBase64;
    }

    /// <summary>Line-delimited JSON dataset writer for human teleoperation BC demonstrations.</summary>
    public sealed class PlanningDemonstrationRecorder : MonoBehaviour
    {
        [SerializeField] string _datasetDirectory = "PhysicalAiDemonstrations";
        [SerializeField] string _filePrefix = "teleop";
        [SerializeField] bool _recordingEnabled;

        StreamWriter _writer;
        int _episodeId;
        int _step;
        Texture2D _cameraReadback;
        int _pendingCameraFrameIndex = -1;
        string _pendingCameraJpeg;

        public string CurrentPath { get; private set; }
        public bool RecordingEnabled => _recordingEnabled;

        public void SetRecordingEnabled(bool enabled)
        {
            if (_recordingEnabled == enabled) return;
            _recordingEnabled = enabled;
            if (!enabled) EndEpisode(false);
        }

        public void BeginEpisode(int episodeId)
        {
            EndEpisode(false);
            if (!_recordingEnabled)
                return;
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
                return;

            WriteFrame(new PlanningDemonstrationFrame
            {
                EpisodeId = _episodeId,
                Step = _step++,
                SimulationTime = Time.time,
                Observation = observation.Features,
                Command = command,
                Reward = reward,
                CameraFrameIndex = _pendingCameraFrameIndex,
                CameraJpegBase64 = _pendingCameraJpeg,
                Terminal = false
            });
            _pendingCameraFrameIndex = -1;
            _pendingCameraJpeg = null;
        }

        public void QueueCameraFrame(CameraObservationFrame frame)
        {
            if (_writer == null || frame.Image == null || frame.FrameIndex == _pendingCameraFrameIndex || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                return;
            var previous = RenderTexture.active;
            RenderTexture.active = frame.Image;
            if (_cameraReadback == null || _cameraReadback.width != frame.Image.width || _cameraReadback.height != frame.Image.height)
            {
                if (_cameraReadback != null) Destroy(_cameraReadback);
                _cameraReadback = new Texture2D(frame.Image.width, frame.Image.height, TextureFormat.RGB24, false);
            }
            _cameraReadback.ReadPixels(new Rect(0, 0, frame.Image.width, frame.Image.height), 0, 0);
            _cameraReadback.Apply(false);
            RenderTexture.active = previous;
            _pendingCameraFrameIndex = frame.FrameIndex;
            _pendingCameraJpeg = Convert.ToBase64String(_cameraReadback.EncodeToJPG(75));
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

        public void EndEpisode(PlanningObservation observation, PlanningCommand command, float terminalReward)
        {
            if (_writer == null)
                return;
            WriteFrame(new PlanningDemonstrationFrame
            {
                EpisodeId = _episodeId,
                Step = _step,
                SimulationTime = Time.time,
                Observation = observation.Features,
                Command = command,
                Reward = terminalReward,
                Terminal = true
            });
            _writer.Dispose();
            _writer = null;
        }

        void OnDisable() => EndEpisode(false);

        void OnDestroy()
        {
            if (_cameraReadback != null) Destroy(_cameraReadback);
        }

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
