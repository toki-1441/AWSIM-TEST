// Copyright 2026
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using PhysicalAi.Course;
using PhysicalAi.Racing;
using PhysicalAi.Safety;
using UnityEngine;

namespace PhysicalAi.Planning
{
    [Serializable]
    public class SemanticBoxAnnotation
    {
        public int instanceId; // Unity object ID; stable only during this object's lifetime in this run.
        public int classId;
        public string objectName;
        public Vector3 centerEgo;
        public Vector3 sizeMeters;
        public Quaternion rotationEgo;
    }
    [Serializable]
    public class BevAnnotations
    {
        public int schemaVersion = 1;
        public int frameIndex;
        public float simulationTime;
        public string source = "simulator-ground-truth-not-camera-visible";
        public int size;
        public float metersPerPixel;
        public Vector3 egoPosition;
        public Quaternion egoRotation;
        public SemanticBoxAnnotation[] objects;
    }
    public sealed class BevObservation
    {
        public byte[] Labels;
        public Texture2D Preview;
        public BevAnnotations Annotations;
    }

    /// <summary>Rasterizes actual rendered-road triangles and actual collision boxes, in ego coordinates.</summary>
    public sealed class BevAnnotationCapture : IDisposable
    {
        public static readonly string[] Classes = { "background", "road", "boundary", "obstacle", "other-vehicle", "ego-vehicle" };
        static readonly Color32[] Colors = { new Color32(25,25,30,255), new Color32(100,100,100,255),
            new Color32(240,210,40,255), new Color32(240,70,60,255), new Color32(40,160,250,255), new Color32(50,230,120,255) };
        readonly int _size;
        readonly float _resolution;
        readonly byte[] _labels;
        readonly Texture2D _preview;
        Quaternion _inverse;
        Vector3 _origin;

        public BevAnnotationCapture(int size, float metersPerPixel)
        {
            if (size < 16 || size > 512 || !float.IsFinite(metersPerPixel) || metersPerPixel <= 0)
                throw new ArgumentException("Invalid BEV size/resolution.");
            _size = size; _resolution = metersPerPixel;
            _labels = new byte[size * size];
            _preview = new Texture2D(size, size, TextureFormat.RGBA32, false);
        }

        public BevObservation Capture(ProceduralCourseTrackBuilder track, Transform ego, int frame, float time)
        {
            Array.Clear(_labels, 0, _labels.Length);
            _origin = ego.position; _inverse = Quaternion.Inverse(ego.rotation);
            var surface = track == null ? null : track.RoadSurface;
            if (surface != null && surface.sharedMesh != null)
            {
                var vertices = surface.sharedMesh.vertices;
                var triangles = surface.sharedMesh.triangles;
                var projected = new Vector2[vertices.Length];
                for (var i = 0; i < vertices.Length; i++) projected[i] = Project(surface.transform.TransformPoint(vertices[i]));
                for (var i = 0; i < triangles.Length; i += 3)
                    Triangle(projected[triangles[i]], projected[triangles[i + 1]], projected[triangles[i + 2]], 1);
            }
            var annotations = new List<SemanticBoxAnnotation>();
            if (track != null)
                foreach (var boundary in track.GetComponentsInChildren<CourseBoundary>())
                    Box(boundary.GetComponent<BoxCollider>(), 2, annotations);
            foreach (var hazard in UnityEngine.Object.FindObjectsByType<EpisodeHazard>(FindObjectsSortMode.None))
                foreach (var box in hazard.GetComponentsInChildren<BoxCollider>()) Box(box, 3, annotations);
            foreach (var opponent in UnityEngine.Object.FindObjectsByType<OpponentCourseFollower>(FindObjectsSortMode.None))
                foreach (var box in opponent.GetComponentsInChildren<BoxCollider>()) Box(box, 4, annotations);
            Box(ego.GetComponent<BoxCollider>(), 5, annotations);
            var colors = new Color32[_labels.Length];
            for (var y = 0; y < _size; y++)
                for (var x = 0; x < _size; x++) colors[(_size - 1 - y) * _size + x] = Colors[_labels[y * _size + x]];
            _preview.SetPixels32(colors); _preview.Apply(false);
            return new BevObservation { Labels = _labels, Preview = _preview, Annotations = new BevAnnotations
            {
                frameIndex = frame, simulationTime = time, size = _size, metersPerPixel = _resolution,
                egoPosition = _origin, egoRotation = ego.rotation, objects = annotations.ToArray()
            }};
        }

        Vector2 Project(Vector3 world)
        {
            var local = _inverse * (world - _origin);
            return new Vector2(local.x / _resolution + _size * 0.5f, _size * 0.5f - local.z / _resolution);
        }
        void Box(BoxCollider box, byte label, List<SemanticBoxAnnotation> annotations)
        {
            if (box == null || !box.enabled || box.isTrigger) return;
            var center = _inverse * (box.transform.TransformPoint(box.center) - _origin);
            var radius = box.bounds.extents.magnitude;
            if (Mathf.Abs(center.x) - radius > _size * _resolution * 0.5f || Mathf.Abs(center.z) - radius > _size * _resolution * 0.5f) return;
            var half = box.size * 0.5f;
            var corners = new[] { new Vector3(-half.x, 0, -half.z), new Vector3(half.x, 0, -half.z),
                new Vector3(half.x, 0, half.z), new Vector3(-half.x, 0, half.z) };
            var p = new Vector2[4];
            for (var i = 0; i < p.Length; i++) p[i] = Project(box.transform.TransformPoint(box.center + corners[i]));
            Triangle(p[0], p[1], p[2], label); Triangle(p[0], p[2], p[3], label);
            annotations.Add(new SemanticBoxAnnotation { instanceId = box.GetInstanceID(), classId = label,
                objectName = box.gameObject.name, centerEgo = center, sizeMeters = Vector3.Scale(box.size, box.transform.lossyScale),
                rotationEgo = _inverse * box.transform.rotation });
        }

        void Triangle(Vector2 a, Vector2 b, Vector2 c, byte value)
        {
            var minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))));
            var maxX = Mathf.Min(_size - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))));
            var minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))));
            var maxY = Mathf.Min(_size - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))));
            if (Mathf.Abs(Cross(b - a, c - a)) < 1e-8f) return;
            for (var y = minY; y <= maxY; y++) for (var x = minX; x <= maxX; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                var u = Cross(b - a, p - a); var v = Cross(c - b, p - b); var w = Cross(a - c, p - c);
                if ((u >= 0 && v >= 0 && w >= 0) || (u <= 0 && v <= 0 && w <= 0)) _labels[y * _size + x] = value;
            }
        }
        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        public void Dispose() => UnityEngine.Object.Destroy(_preview);
    }
}
