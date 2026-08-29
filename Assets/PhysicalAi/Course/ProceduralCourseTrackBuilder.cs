// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using UnityEngine;

namespace PhysicalAi.Course
{
    /// <summary>Turns a generated centerline into a collider-backed road for runtime episodes.</summary>
    public sealed class ProceduralCourseTrackBuilder : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] float _roadWidth = 1f;
        [SerializeField, Min(0.01f)] float _roadThickness = 0.05f;
        [SerializeField, Min(0.01f)] float _wallHeight = 0.08f;
        [SerializeField, Min(0.01f)] float _wallThickness = 0.04f;
        [SerializeField] Transform _trackRoot;

        public int SegmentCount { get; private set; }
        public LineRenderer CenterlineRenderer { get; private set; }

        public void Build(GeneratedCourse course)
        {
            Clear();
            Append(course, 1);
        }

        /// <summary>Adds newly appended centerline segments without rebuilding the existing road behind the vehicle.</summary>
        public void Append(GeneratedCourse course, int firstCenterlineIndex)
        {
            if (course == null || course.Centerline.Count < 2)
                return;

            var root = _trackRoot == null ? transform : _trackRoot;
            UpdateCenterline(root, course);
            for (var index = Mathf.Max(1, firstCenterlineIndex); index < course.Centerline.Count; index++)
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
                segment.AddComponent<CourseTrackSegment>().Configure(index, true);
                CreateBoundary(root, $"BoundaryLeft-{SegmentCount:D4}", (start + end) * 0.5f + Vector3.Cross(Vector3.up, direction.normalized) * (_roadWidth * 0.5f) + Vector3.up * (_wallHeight * 0.5f), direction, length).Configure(index, false);
                CreateBoundary(root, $"BoundaryRight-{SegmentCount:D4}", (start + end) * 0.5f - Vector3.Cross(Vector3.up, direction.normalized) * (_roadWidth * 0.5f) + Vector3.up * (_wallHeight * 0.5f), direction, length).Configure(index, false);
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
            CenterlineRenderer = null;
        }

        /// <summary>Removes collider segments sufficiently behind the ego waypoint while preserving a safety buffer.</summary>
        public void DiscardBefore(int nearestCenterlineIndex, int retainedWaypointsBehind)
        {
            var root = _trackRoot == null ? transform : _trackRoot;
            var cutoff = nearestCenterlineIndex - Mathf.Max(0, retainedWaypointsBehind);
            foreach (var segment in root.GetComponentsInChildren<CourseTrackSegment>())
            {
                if (segment.CenterlineIndex >= cutoff)
                    continue;
                if (segment.IsRoad)
                    SegmentCount--;
                if (Application.isPlaying)
                    Destroy(segment.gameObject);
                else
                    DestroyImmediate(segment.gameObject);
            }
            SegmentCount = Mathf.Max(0, SegmentCount);
        }

        void UpdateCenterline(Transform root, GeneratedCourse course)
        {
            if (CenterlineRenderer == null)
            {
                var lineObject = new GameObject("CourseCenterline");
                lineObject.transform.SetParent(root, false);
                CenterlineRenderer = lineObject.AddComponent<LineRenderer>();
                CenterlineRenderer.useWorldSpace = true;
                CenterlineRenderer.startWidth = 0.025f;
                CenterlineRenderer.endWidth = 0.025f;
                CenterlineRenderer.numCornerVertices = 2;
                CenterlineRenderer.material = new Material(Shader.Find("Sprites/Default"));
                CenterlineRenderer.startColor = Color.yellow;
                CenterlineRenderer.endColor = Color.yellow;
            }
            var line = CenterlineRenderer;
            line.positionCount = course.Centerline.Count;
            for (var index = 0; index < course.Centerline.Count; index++)
                line.SetPosition(index, course.Centerline[index] + Vector3.up * 0.003f);
            CenterlineRenderer = line;
        }

        CourseTrackSegment CreateBoundary(Transform root, string name, Vector3 position, Vector3 direction, float length)
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = name;
            wall.transform.SetParent(root, false);
            wall.transform.position = position;
            wall.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            wall.transform.localScale = new Vector3(_wallThickness, _wallHeight, length + _wallThickness);
            wall.AddComponent<CourseBoundary>();
            return wall.AddComponent<CourseTrackSegment>();
        }
    }
}
