// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using System;
using PhysicalAi.VehicleDynamics;
using UnityEngine;

namespace PhysicalAi.Randomization
{
    [Serializable]
    public struct DomainRandomizationSettings
    {
        [Min(0.01f)] public float MinimumFriction;
        [Min(0.01f)] public float MaximumFriction;
        [Min(0f)] public float MaximumMountPositionOffset;
        [Min(0f)] public float MaximumMountRotationDegrees;

        public static DomainRandomizationSettings Default => new DomainRandomizationSettings
        {
            MinimumFriction = 0.6f,
            MaximumFriction = 1.2f,
            MaximumMountPositionOffset = 0.03f,
            MaximumMountRotationDegrees = 3f
        };
    }

    public readonly struct DomainSample
    {
        public readonly float Friction;
        public readonly Vector3 MountPositionOffset;
        public readonly Vector3 MountRotationOffsetDegrees;

        public DomainSample(float friction, Vector3 mountPositionOffset, Vector3 mountRotationOffsetDegrees)
        {
            Friction = friction;
            MountPositionOffset = mountPositionOffset;
            MountRotationOffsetDegrees = mountRotationOffsetDegrees;
        }
    }

    public static class DomainRandomizationSampler
    {
        public static DomainSample Sample(int seed, DomainRandomizationSettings settings)
        {
            if (settings.MaximumFriction < settings.MinimumFriction)
                throw new ArgumentException("Maximum friction must be at least minimum friction.", nameof(settings));
            var random = new System.Random(seed);
            float Uniform(float magnitude) => Mathf.Lerp(-magnitude, magnitude, (float)random.NextDouble());
            return new DomainSample(
                Mathf.Lerp(settings.MinimumFriction, settings.MaximumFriction, (float)random.NextDouble()),
                new Vector3(Uniform(settings.MaximumMountPositionOffset), Uniform(settings.MaximumMountPositionOffset), Uniform(settings.MaximumMountPositionOffset)),
                new Vector3(Uniform(settings.MaximumMountRotationDegrees), Uniform(settings.MaximumMountRotationDegrees), Uniform(settings.MaximumMountRotationDegrees)));
        }
    }

    /// <summary>Applies a reproducible road and sensor-mount randomization at every episode reset.</summary>
    public sealed class DomainRandomizer : MonoBehaviour
    {
        [SerializeField] CombinedSlipVehicle _vehicle;
        [SerializeField] Transform[] _sensorMounts = Array.Empty<Transform>();
        [SerializeField] DomainRandomizationSettings _settings = default;

        Vector3[] _basePositions;
        Quaternion[] _baseRotations;
        float _frictionScale = 1f;

        void Awake()
        {
            if (_settings.MinimumFriction <= 0f)
                _settings = DomainRandomizationSettings.Default;
            _basePositions = new Vector3[_sensorMounts.Length];
            _baseRotations = new Quaternion[_sensorMounts.Length];
            for (var i = 0; i < _sensorMounts.Length; i++)
            {
                if (_sensorMounts[i] == null)
                    continue;
                _basePositions[i] = _sensorMounts[i].localPosition;
                _baseRotations[i] = _sensorMounts[i].localRotation;
            }
        }

        public DomainSample ApplyEpisodeSeed(int seed)
        {
            var sample = DomainRandomizationSampler.Sample(seed, _settings);
            if (_vehicle != null)
                _vehicle.SurfaceFriction = sample.Friction * _frictionScale;
            for (var i = 0; i < _sensorMounts.Length; i++)
            {
                if (_sensorMounts[i] == null)
                    continue;
                _sensorMounts[i].localPosition = _basePositions[i] + sample.MountPositionOffset;
                _sensorMounts[i].localRotation = _baseRotations[i] * Quaternion.Euler(sample.MountRotationOffsetDegrees);
            }
            return sample;
        }

        /// <summary>Centers subsequent randomized road-friction samples on a telemetry-calibrated vehicle.</summary>
        public void SetFrictionScale(float scale) => _frictionScale = Mathf.Max(0.01f, scale);
    }
}
