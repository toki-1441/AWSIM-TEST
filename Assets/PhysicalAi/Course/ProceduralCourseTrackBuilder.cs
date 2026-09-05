// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using UnityEngine;

namespace PhysicalAi.Course
{
    /// <summary>Turns a generated centerline into a collider-backed road for runtime episodes.</summary>
    public sealed class ProceduralCourseTrackBuilder : MonoBehaviour
    {
        /// <summary>
        /// The only layer that may support a ray-cast wheel.  Course boundaries
        /// intentionally remain on their normal collision layer: treating a wall
        /// top as road makes an off-course wheel receive an artificial suspension
        /// impulse and can launch the vehicle.
        /// </summary>
        public const string DrivableSurfaceLayerName = "PhysicalAiGround";

        [SerializeField, Min(0.1f)] float _roadWidth = 1.2f;
        [SerializeField, Min(0.01f)] float _roadThickness = 0.05f;
        // Keep the visual asphalt decisively above the carpet floor. Coplanar HDRP
        // surfaces fight for the depth buffer and appear to flicker in motion.
        // At grazing camera angles a 2 mm separation still shares too few depth
        // precision units with the venue floor, particularly with HDRP temporal
        // anti-aliasing.  Keep the rendered asphalt a centimetre above that
        // floor.  The floor remains the single flat tire-contact surface, so this
        // visual separation cannot introduce a suspension step at a chunk join.
        const float RoadVisualLift = 0.01f;
        // Boundary boxes are per-centreline sample.  At a curved join, full-length
        // boxes overlap and their coincident faces shimmer; the overlapping
        // colliders can also make an RC-scale body receive alternating contact
        // normals.  A two-millimetre joint is far below the vehicle envelope but
        // makes neighbouring pieces disjoint.
        const float BoundaryJointGap = 0.002f;
        [SerializeField, Min(0.01f)] float _wallHeight = 0.08f;
        [SerializeField, Min(0.01f)] float _wallThickness = 0.04f;
        [SerializeField] Transform _trackRoot;

        [Header("Appearance")]
        [SerializeField] Material _roadMaterial;
        [SerializeField] Material _wallPrimaryMaterial;
        [SerializeField] Material _wallAlternateMaterial;
        [SerializeField] Material _courseLineMaterial;
        /// <summary>Segments per duct stripe. MAGP walls are built from alternating red and white duct.</summary>
        [SerializeField, Min(1)] int _wallStripeSegments = 3;

        public int SegmentCount { get; private set; }
        public LineRenderer CenterlineRenderer { get; private set; }
        MeshFilter _roadSurface;
        int _firstVisibleCenterlineIndex;

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

                // Rendering one independent cube per sample left either coplanar
                // overlap (flicker) or, after shrinking, visible seams. Markers keep
                // streaming lifecycle accounting while one continuous strip mesh
                // renders the entire currently visible corridor.
                var segment = new GameObject($"RoadSegment-{SegmentCount:D4}");
                segment.name = $"RoadSegment-{SegmentCount:D4}";
                segment.layer = LayerMask.NameToLayer("Ignore Raycast");
                segment.transform.SetParent(root, false);
                segment.AddComponent<CourseTrackSegment>().Configure(index, true);
                var wallMaterial = SegmentCount / _wallStripeSegments % 2 == 0 ? _wallPrimaryMaterial : _wallAlternateMaterial;
                CreateBoundary(root, $"BoundaryLeft-{SegmentCount:D4}", (start + end) * 0.5f + Vector3.Cross(Vector3.up, direction.normalized) * (_roadWidth * 0.5f) + Vector3.up * (_wallHeight * 0.5f), direction, length, wallMaterial).Configure(index, false);
                CreateBoundary(root, $"BoundaryRight-{SegmentCount:D4}", (start + end) * 0.5f - Vector3.Cross(Vector3.up, direction.normalized) * (_roadWidth * 0.5f) + Vector3.up * (_wallHeight * 0.5f), direction, length, wallMaterial).Configure(index, false);
                SegmentCount++;
            }
            UpdateRoadSurface(root, course);
        }

        public void Clear()
        {
            var root = _trackRoot == null ? transform : _trackRoot;
            for (var index = root.childCount - 1; index >= 0; index--)
            {
                var child = root.GetChild(index).gameObject;
                if (Application.isPlaying)
                {
                    // Destroy is deferred until the end of the frame. An evaluation
                    // runner resets its next vehicle in the completion callback,
                    // so leaving old wall colliders active for that frame creates an
                    // immediate collision with the freshly generated course.
                    child.SetActive(false);
                    Destroy(child);
                }
                else
                    DestroyImmediate(child);
            }
            SegmentCount = 0;
            CenterlineRenderer = null;
            _roadSurface = null;
            _firstVisibleCenterlineIndex = 0;
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
                {
                    // The retained mesh is rebuilt in the same frame; disable a
                    // discarded wall immediately rather than letting its deferred
                    // destruction produce a one-frame collision impulse.
                    segment.gameObject.SetActive(false);
                    Destroy(segment.gameObject);
                }
                else
                    DestroyImmediate(segment.gameObject);
            }
            SegmentCount = Mathf.Max(0, SegmentCount);
            _firstVisibleCenterlineIndex = Mathf.Max(_firstVisibleCenterlineIndex, Mathf.Max(0, cutoff - 1));
            if (_roadSurface != null)
                UpdateRoadSurface(root, null);
        }

        /// <summary>Moves retained physical track geometry and its world-space visual centreline.</summary>
        public void Translate(Vector3 offset)
        {
            var root = _trackRoot == null ? transform : _trackRoot;
            root.position += offset;
            if (CenterlineRenderer == null)
                return;
            for (var index = 0; index < CenterlineRenderer.positionCount; index++)
                CenterlineRenderer.SetPosition(index, CenterlineRenderer.GetPosition(index) + offset);
        }

        public void RebaseWaypoints(int removedWaypointCount, GeneratedCourse course)
        {
            if (removedWaypointCount <= 0) return;
            var root = _trackRoot == null ? transform : _trackRoot;
            foreach (var segment in root.GetComponentsInChildren<CourseTrackSegment>()) segment.Rebase(removedWaypointCount);
            _firstVisibleCenterlineIndex = Mathf.Max(0, _firstVisibleCenterlineIndex - removedWaypointCount);
            UpdateCenterline(root, course);
            UpdateRoadSurface(root, course);
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
                // The line carries the regulation floor-marking colour in its material.
                // A built-in-pipeline shader such as Sprites/Default renders as HDRP's
                // magenta error fallback, so an assigned asset is used when available.
                CenterlineRenderer.sharedMaterial = _courseLineMaterial != null ? _courseLineMaterial : CreateFallbackLineMaterial();
                CenterlineRenderer.startColor = Color.white;
                CenterlineRenderer.endColor = Color.white;
            }
            var line = CenterlineRenderer;
            line.positionCount = course.Centerline.Count;
            for (var index = 0; index < course.Centerline.Count; index++)
                line.SetPosition(index, course.Centerline[index] + Vector3.up * (RoadVisualLift + 0.003f));
            CenterlineRenderer = line;
        }

        CourseTrackSegment CreateBoundary(Transform root, string name, Vector3 position, Vector3 direction, float length, Material material)
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = name;
            wall.transform.SetParent(root, false);
            wall.transform.position = position;
            wall.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            // Adjacent ducts meet at a corner. Shorten each one slightly so their
            // end faces never occupy the same plane or volume.
            wall.transform.localScale = new Vector3(_wallThickness, _wallHeight,
                Mathf.Max(0.01f, length - BoundaryJointGap));
            wall.AddComponent<CourseBoundary>();
            Paint(wall, material);
            return wall.AddComponent<CourseTrackSegment>();
        }

        static Material CreateFallbackLineMaterial()
        {
            var shader = Shader.Find("HDRP/Unlit") ?? Shader.Find("Sprites/Default");
            if (shader == null)
                return null;
            var material = new Material(shader) { name = "CourseLineFallback" };
            if (material.HasProperty("_UnlitColor"))
                material.SetColor("_UnlitColor", Color.yellow);
            return material;
        }

        static void Paint(GameObject target, Material material)
        {
            if (material == null)
                return;
            var renderer = target.GetComponent<MeshRenderer>();
            if (renderer != null)
                renderer.sharedMaterial = material;
        }

        void UpdateRoadSurface(Transform root, GeneratedCourse suppliedCourse)
        {
            if (suppliedCourse != null)
                _renderedCourse = suppliedCourse;
            if (_renderedCourse == null || _renderedCourse.Centerline.Count < 2)
                return;
            if (_roadSurface == null)
            {
                var surface = new GameObject("RoadSurface");
                surface.transform.SetParent(root, false);
                _roadSurface = surface.AddComponent<MeshFilter>();
                var renderer = surface.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = _roadMaterial;
                _roadSurface.sharedMesh = new Mesh { name = "PhysicalAiRoadSurface" };
            }

            var start = Mathf.Clamp(_firstVisibleCenterlineIndex, 0, _renderedCourse.Centerline.Count - 2);
            var count = _renderedCourse.Centerline.Count - start;
            var vertices = new Vector3[count * 2];
            var triangles = new int[(count - 1) * 6];
            for (var local = 0; local < count; local++)
            {
                var index = start + local;
                var before = _renderedCourse.Centerline[Mathf.Max(start, index - 1)];
                var after = _renderedCourse.Centerline[Mathf.Min(_renderedCourse.Centerline.Count - 1, index + 1)];
                var tangent = (after - before).normalized;
                var sideways = Vector3.Cross(Vector3.up, tangent) * (_roadWidth * 0.5f);
                // The course is authoritative in world coordinates while the road
                // mesh is a child of the movable track root.  Convert every vertex
                // to that local frame; otherwise a floating-origin rebase moves the
                // root and the rebuilt mesh a second time.
                var center = root.InverseTransformPoint(_renderedCourse.Centerline[index] + Vector3.up * RoadVisualLift);
                vertices[local * 2] = center + sideways;
                vertices[local * 2 + 1] = center - sideways;
                if (local == count - 1) continue;
                var triangle = local * 6;
                triangles[triangle] = local * 2;
                triangles[triangle + 1] = local * 2 + 1;
                triangles[triangle + 2] = local * 2 + 2;
                triangles[triangle + 3] = local * 2 + 1;
                triangles[triangle + 4] = local * 2 + 3;
                triangles[triangle + 5] = local * 2 + 2;
            }
            var mesh = _roadSurface.sharedMesh;
            mesh.Clear();
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        GeneratedCourse _renderedCourse;
    }
}
