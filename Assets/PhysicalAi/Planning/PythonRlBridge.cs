// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;
using System.Net.Sockets;
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

        [Serializable] class Request { public string type; public float[] observation; public float reward; public bool terminal; }
        [Serializable] class Response { public float desiredSpeed; public float desiredLateralOffsetNormalized; }

        void Start()
        {
            if (_connectOnStart) Connect();
        }

        void OnEnable()
        {
            if (_orchestrator == null) return;
            _orchestrator.EpisodeFinished += OnEpisodeFinished;
            _orchestrator.CommandProvider = RequestCommand;
        }

        void OnDisable()
        {
            if (_orchestrator != null)
            {
                _orchestrator.EpisodeFinished -= OnEpisodeFinished;
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
                _writer.WriteLine(JsonUtility.ToJson(new Request { type = "step", observation = observation.Features, reward = _pendingReward, terminal = false }));
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

        void Disconnect()
        {
            _connected = false;
            _reader?.Dispose(); _writer?.Dispose(); _client?.Dispose();
            _reader = null; _writer = null; _client = null;
        }
    }
}
