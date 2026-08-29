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
            if (course == null || course.Centerline.Count < 2)
                return;

            var root = _trackRoot == null ? transform : _trackRoot;
            CreateCenterline(root, course);
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
                CreateBoundary(root, $"BoundaryLeft-{SegmentCount:D4}", (start + end) * 0.5f + Vector3.Cross(Vector3.up, direction.normalized) * (_roadWidth * 0.5f) + Vector3.up * (_wallHeight * 0.5f), direction, length);
                CreateBoundary(root, $"BoundaryRight-{SegmentCount:D4}", (start + end) * 0.5f - Vector3.Cross(Vector3.up, direction.normalized) * (_roadWidth * 0.5f) + Vector3.up * (_wallHeight * 0.5f), direction, length);
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

        void CreateCenterline(Transform root, GeneratedCourse course)
        {
            var lineObject = new GameObject("CourseCenterline");
            lineObject.transform.SetParent(root, false);
            var line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = course.Centerline.Count;
            line.startWidth = 0.025f;
            line.endWidth = 0.025f;
            line.numCornerVertices = 2;
            line.material = new Material(Shader.Find("Sprites/Default"));
            line.startColor = Color.yellow;
            line.endColor = Color.yellow;
            for (var index = 0; index < course.Centerline.Count; index++)
                line.SetPosition(index, course.Centerline[index] + Vector3.up * 0.003f);
            CenterlineRenderer = line;
        }

        void CreateBoundary(Transform root, string name, Vector3 position, Vector3 direction, float length)
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = name;
            wall.transform.SetParent(root, false);
            wall.transform.position = position;
            wall.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            wall.transform.localScale = new Vector3(_wallThickness, _wallHeight, length + _wallThickness);
            wall.AddComponent<CourseBoundary>();
        }
    }
}
