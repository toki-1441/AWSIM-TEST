// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using System;
using UnityEngine;

namespace PhysicalAi.Safety
{
    /// <summary>Converts camera-visible distance into a stopping-speed bound.</summary>
    public static class BlindCornerSafety
    {
        public static float MaximumSafeSpeed(float visibleDistance, float reactionTime, float maximumDeceleration)
        {
            var distance = Mathf.Max(0f, visibleDistance);
            var reaction = Mathf.Max(0f, reactionTime);
            var deceleration = Mathf.Max(0.001f, maximumDeceleration);
            // d = v * reactionTime + v^2 / (2a)
            return Mathf.Max(0f, -deceleration * reaction + Mathf.Sqrt(deceleration * deceleration * reaction * reaction + 2f * deceleration * distance));
        }

        public static bool IsVisible(Camera camera, Vector3 point, LayerMask occluderMask)
        {
            if (camera == null)
                return false;

            var viewport = camera.WorldToViewportPoint(point);
            if (viewport.z <= 0f || viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f)
                return false;

            var origin = camera.transform.position;
            var direction = point - origin;
            return !Physics.Raycast(origin, direction.normalized, direction.magnitude, occluderMask, QueryTriggerInteraction.Ignore);
        }
    }

    /// <summary>
    /// Spawns one hazard at a camera-hidden candidate location. A deterministic
    /// seed makes hazard episodes reproducible for offline BC and RL evaluation.
    /// </summary>
    public sealed class BlindCornerHazardSpawner : MonoBehaviour
    {
        [SerializeField] Camera _egoCamera;
        [SerializeField] GameObject[] _hazardPrefabs = Array.Empty<GameObject>();
        [SerializeField] Transform[] _candidateSpawns = Array.Empty<Transform>();
        [SerializeField, Range(0f, 1f)] float _spawnProbability = 0.5f;
        [SerializeField] int _seed = 1;
        [SerializeField] LayerMask _occluderMask = ~0;
        [SerializeField] bool _spawnOnStart = true;

        public GameObject ActiveHazard { get; private set; }

        void Start()
        {
            if (_spawnOnStart)
                SpawnEpisodeHazard(_seed);
        }

        public bool SpawnEpisodeHazard(int seed)
        {
            ClearHazard();
            if (_hazardPrefabs.Length == 0 || _candidateSpawns.Length == 0)
                return false;

            var random = new System.Random(seed);
            if (random.NextDouble() > _spawnProbability)
                return false;

            var hidden = new System.Collections.Generic.List<Transform>();
            foreach (var candidate in _candidateSpawns)
            {
                if (candidate != null && !BlindCornerSafety.IsVisible(_egoCamera, candidate.position, _occluderMask))
                    hidden.Add(candidate);
            }
            if (hidden.Count == 0)
                return false;

            var spawn = hidden[random.Next(hidden.Count)];
            var prefab = _hazardPrefabs[random.Next(_hazardPrefabs.Length)];
            ActiveHazard = Instantiate(prefab, spawn.position, spawn.rotation);
            return true;
        }

        public void ClearHazard()
        {
            if (ActiveHazard != null)
                Destroy(ActiveHazard);
            ActiveHazard = null;
        }
    }
}
