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
            MaximumMountPositionOffset = 0f,
            MaximumMountRotationDegrees = 0f
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
            return new DomainSample(
                Mathf.Lerp(settings.MinimumFriction, settings.MaximumFriction, (float)random.NextDouble()),
                Vector3.zero, Vector3.zero);
        }
    }

    /// <summary>Randomizes road friction only. Sensor mounting poses are fixed configuration, never episode noise.</summary>
    public sealed class DomainRandomizer : MonoBehaviour
    {
        [SerializeField] CombinedSlipVehicle _vehicle;
        [SerializeField] Transform[] _sensorMounts = Array.Empty<Transform>();
        [SerializeField] DomainRandomizationSettings _settings = default;

        float _frictionScale = 1f;
        public DomainRandomizationSettings Settings => _settings;
        public float FrictionScale => _frictionScale;

        void Awake()
        {
            if (_settings.MinimumFriction <= 0f)
                _settings = DomainRandomizationSettings.Default;
            // Ignore legacy serialized nonzero mount-noise settings as well as new defaults.
            _settings.MaximumMountPositionOffset = 0;
            _settings.MaximumMountRotationDegrees = 0;
        }

        public DomainSample ApplyEpisodeSeed(int seed)
        {
            var sample = DomainRandomizationSampler.Sample(seed, _settings);
            if (_vehicle != null)
                _vehicle.SurfaceFriction = sample.Friction * _frictionScale;
            return sample;
        }

        /// <summary>Centers subsequent randomized road-friction samples on a telemetry-calibrated vehicle.</summary>
        public void SetFrictionScale(float scale) => _frictionScale = Mathf.Max(0.01f, scale);
    }
}
