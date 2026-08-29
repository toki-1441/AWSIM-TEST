// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using System;
using PhysicalAi.Course;
using PhysicalAi.VehicleDynamics;
using UnityEngine;

namespace PhysicalAi.Safety
{
    /// <summary>Spawns deterministic, camera-hidden hazards beyond sharp course corners.</summary>
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
                if (candidate != null && !BlindCornerSafety.IsVisible(_egoCamera, candidate.position, _occluderMask)) hidden.Add(candidate);
            if (hidden.Count == 0) return false;
            var spawn = hidden[random.Next(hidden.Count)];
            ActiveHazard = Instantiate(_hazardPrefabs[random.Next(_hazardPrefabs.Length)], spawn.position, spawn.rotation);
            return true;
        }

        public bool SpawnCourseHazard(GeneratedCourse course, int seed)
        {
            ClearHazard();
            if (course == null || _egoCamera == null) return false;
            var random = new System.Random(seed);
            if (random.NextDouble() > _spawnProbability) return false;
            var hiddenCorners = new System.Collections.Generic.List<Vector3>();
            for (var index = 2; index < course.Centerline.Count; index++)
                if (Mathf.Abs(course.Curvature[index]) >= 0.02f && !BlindCornerSafety.IsVisible(_egoCamera, course.Centerline[index] + Vector3.up * VehicleSpecification.Default.BodyCenterHeight, _occluderMask))
                    hiddenCorners.Add(course.Centerline[index] + Vector3.up * VehicleSpecification.Default.BodyCenterHeight);
            if (hiddenCorners.Count == 0) return false;
            ActiveHazard = _hazardPrefabs.Length == 0 ? GameObject.CreatePrimitive(PrimitiveType.Cube) : Instantiate(_hazardPrefabs[random.Next(_hazardPrefabs.Length)]);
            ActiveHazard.name = "BlindCornerHazard";
            ActiveHazard.transform.SetPositionAndRotation(hiddenCorners[random.Next(hiddenCorners.Count)], Quaternion.identity);
            if (_hazardPrefabs.Length == 0) ActiveHazard.transform.localScale = new Vector3(0.12f, 0.12f, 0.16f);
            if (ActiveHazard.GetComponent<EpisodeHazard>() == null) ActiveHazard.AddComponent<EpisodeHazard>();
            return true;
        }

        public void ClearHazard()
        {
            if (ActiveHazard != null) Destroy(ActiveHazard);
            ActiveHazard = null;
        }
    }
}
