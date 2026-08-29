// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;
using System.Net.Sockets;
using PhysicalAi.Sensors;
using UnityEngine;

namespace PhysicalAi.Planning
{
    /// <summary>Single-environment JSON-lines TCP bridge for a Python RL policy process.</summary>
    public sealed class PythonRlBridge : MonoBehaviour
    {
        [SerializeField] PhysicalAiEpisodeOrchestrator _orchestrator;
        [SerializeField] string _host = "127.0.0.1";
        [SerializeField, Min(1)] int _port = 5015;
        [SerializeField] bool _connectOnStart;

        TcpClient _client;
        StreamReader _reader;
        StreamWriter _writer;
        PlanningCommand _lastCommand;
        float _pendingReward;
        bool _connected;
        Texture2D _cameraReadback;
        string _latestCameraJpeg;

        [Serializable] class Request { public string type; public float[] observation; public string cameraJpegBase64; public float reward; public bool terminal; }
        [Serializable] class Response { public float desiredSpeed; public float desiredLateralOffsetNormalized; }

        void Start()
        {
            if (_connectOnStart) Connect();
        }

        void OnEnable()
        {
            if (_orchestrator == null) return;
            _orchestrator.EpisodeFinished += OnEpisodeFinished;
            _orchestrator.CameraObservationReady += OnCameraObservation;
        }

        void OnDisable()
        {
            if (_orchestrator != null)
            {
                _orchestrator.EpisodeFinished -= OnEpisodeFinished;
                _orchestrator.CameraObservationReady -= OnCameraObservation;
                if (_orchestrator.CommandProvider == RequestCommand) _orchestrator.CommandProvider = null;
            }
            Disconnect();
        }

        public bool Connect()
        {
            Disconnect();
            try
            {
                _client = new TcpClient();
                _client.SendTimeout = 1000;
                _client.ReceiveTimeout = 1000;
                _client.Connect(_host, _port);
                var stream = _client.GetStream();
                _reader = new StreamReader(stream);
                _writer = new StreamWriter(stream) { AutoFlush = true };
                _connected = true;
                if (_orchestrator != null) _orchestrator.CommandProvider = RequestCommand;
                return true;
            }
            catch (SocketException)
            {
                Disconnect();
                return false;
            }
        }

        PlanningCommand RequestCommand(PlanningObservation observation)
        {
            if (!_connected) return _lastCommand;
            try
            {
                _writer.WriteLine(JsonUtility.ToJson(new Request { type = "step", observation = observation.Features, cameraJpegBase64 = _latestCameraJpeg, reward = _pendingReward, terminal = false }));
                var response = JsonUtility.FromJson<Response>(_reader.ReadLine());
                _pendingReward = 0f;
                _lastCommand = new PlanningCommand
                {
                    DesiredSpeed = Mathf.Max(0f, response.desiredSpeed),
                    DesiredLateralOffsetNormalized = Mathf.Clamp(response.desiredLateralOffsetNormalized, -1f, 1f)
                };
            }
            catch (IOException) { Disconnect(); }
            return _lastCommand;
        }

        void OnEpisodeFinished(bool completed, bool collision, bool offCourse)
        {
            _pendingReward = completed ? 10f : collision ? -20f : offCourse ? -10f : 0f;
            if (!_connected) return;
            try { _writer.WriteLine(JsonUtility.ToJson(new Request { type = "terminal", reward = _pendingReward, terminal = true })); }
            catch (IOException) { Disconnect(); }
        }

        void OnCameraObservation(CameraObservationFrame frame)
        {
            if (!_connected || frame.Image == null || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
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
            _latestCameraJpeg = Convert.ToBase64String(_cameraReadback.EncodeToJPG(75));
        }

        void Disconnect()
        {
            _connected = false;
            _reader?.Dispose(); _writer?.Dispose(); _client?.Dispose();
            _reader = null; _writer = null; _client = null;
        }

        void OnDestroy()
        {
            if (_cameraReadback != null) Destroy(_cameraReadback);
        }
    }
}
