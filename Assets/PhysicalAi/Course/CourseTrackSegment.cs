// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using UnityEngine;

namespace PhysicalAi.Course
{
    /// <summary>Identifies one generated road or wall collider for streaming lifecycle management.</summary>
    public sealed class CourseTrackSegment : MonoBehaviour
    {
        public int CenterlineIndex { get; private set; }
        public bool IsRoad { get; private set; }

        public void Configure(int centerlineIndex, bool isRoad)
        {
            CenterlineIndex = centerlineIndex;
            IsRoad = isRoad;
        }
    }
}
