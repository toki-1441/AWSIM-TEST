// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using UnityEngine;

namespace PhysicalAi.Course
{
    /// <summary>Turns a generated centerline into a collider-backed road for runtime episodes.</summary>
    public sealed class ProceduralCourseTrackBuilder : MonoBehaviour
    {
        [SerializeField, Min(0.5f)] float _roadWidth = 4f;
        [SerializeField, Min(0.05f)] float _roadThickness = 0.2f;
        [SerializeField] Transform _trackRoot;

        public int SegmentCount { get; private set; }

        public void Build(GeneratedCourse course)
        {
            Clear();
            if (course == null || course.Centerline.Count < 2)
                return;

            var root = _trackRoot == null ? transform : _trackRoot;
            for (var index = 1; index < course.Centerline.Count; index++)
            {
                var start = course.Centerline[index - 1];
                var end = course.Centerline[index];
                var direction = end - start;
                var length = direction.magnitude;
                if (length < 0.001f)
                    continue;

                var segment = GameObject.CreatePrimitive(PrimitiveType.Cube);
                segment.name = $"RoadSegment-{SegmentCount:D4}";
                segment.transform.SetParent(root, false);
                segment.transform.position = (start + end) * 0.5f - Vector3.up * (_roadThickness * 0.5f);
                segment.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
                segment.transform.localScale = new Vector3(_roadWidth, _roadThickness, length + 0.05f);
                SegmentCount++;
            }
        }

        public void Clear()
        {
            var root = _trackRoot == null ? transform : _trackRoot;
            for (var index = root.childCount - 1; index >= 0; index--)
            {
                var child = root.GetChild(index).gameObject;
                if (Application.isPlaying)
                    Destroy(child);
                else
                    DestroyImmediate(child);
            }
            SegmentCount = 0;
        }
    }
}
