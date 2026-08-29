// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using System;
using UnityEngine;

namespace PhysicalAi.Sensors
{
    /// <summary>Discovers swappable vehicle sensors while retaining a camera as the primary perception source.</summary>
    public sealed class SensorRig : MonoBehaviour
    {
        [SerializeField] Camera _primaryCamera;
        [SerializeField] SensorMount[] _mounts = Array.Empty<SensorMount>();

        public Camera PrimaryCamera => _primaryCamera;
        public SensorMount[] Mounts => _mounts;

        public SensorMount FindMount(SensorModality modality)
        {
            foreach (var mount in _mounts)
                if (mount != null && mount.Modality == modality)
                    return mount;
            return null;
        }
    }
}
