// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace PhysicalAi.Course
{
    /// <summary>Full geometry snapshot, not a seed whose meaning changes with a generator revision.</summary>
    [Serializable]
    public sealed class CourseMapDocument
    {
        [Serializable] public struct Sample
        {
            public Vector3 position;
            public float curvature;
            // -1 means unlimited. JSON must never contain Infinity/NaN.
            public float maximumSafeSpeed;
        }
        public int schemaVersion = 1;
        public bool closedLoop;
        public float halfWidth = 0.6f;
        public Sample[] samples;
        public string ContentHash => Hash(JsonUtility.ToJson(this));

        /// <summary>Start on the first retained road sample, preserving the snapshot's coordinate frame.</summary>
        public Pose StartPose(float vehicleHeight)
        {
            Validate();
            if (!Finite(vehicleHeight)) throw new ArgumentException("Vehicle height must be finite.");
            var position = samples[0].position;
            position.y = vehicleHeight;
            for (var i = 1; i < samples.Length; i++)
            {
                var direction = samples[i].position - samples[0].position;
                direction.y = 0;
                if (direction.sqrMagnitude > 0.000001f)
                    return new Pose(position, Quaternion.LookRotation(direction, Vector3.up));
            }
            throw new ArgumentException("Map must contain a nonzero road segment.");
        }

        public static CourseMapDocument Capture(GeneratedCourse course, Vector3 origin, float headingDegrees, float halfWidth)
        {
            if (course == null) throw new ArgumentNullException(nameof(course));
            var map = new CourseMapDocument { closedLoop = course.IsClosedLoop, halfWidth = halfWidth, samples = new Sample[course.Centerline.Count] };
            var inverse = Quaternion.Euler(0, -headingDegrees, 0);
            for (var i = 0; i < map.samples.Length; i++)
                map.samples[i] = new Sample { position = inverse * (course.Centerline[i] - origin), curvature = course.Curvature[i],
                    maximumSafeSpeed = float.IsPositiveInfinity(course.MaximumSafeSpeed[i]) ? -1 : course.MaximumSafeSpeed[i] };
            map.Validate();
            return map;
        }

        public GeneratedCourse Instantiate(Vector3 origin, float headingDegrees)
        {
            Validate();
            var points = new List<Vector3>();
            var curvature = new List<float>();
            var speeds = new List<float>();
            var rotation = Quaternion.Euler(0, headingDegrees, 0);
            foreach (var sample in samples)
            {
                points.Add(origin + rotation * sample.position);
                curvature.Add(sample.curvature);
                speeds.Add(sample.maximumSafeSpeed == -1 ? float.PositiveInfinity : sample.maximumSafeSpeed);
            }
            return new GeneratedCourse(points, curvature, speeds, closedLoop);
        }

        public void Validate()
        {
            if (schemaVersion != 1 || samples == null || samples.Length < 2 || samples.Length > 100000 || !Finite(halfWidth) || halfWidth <= 0)
                throw new ArgumentException("Invalid or unsupported course map.");
            foreach (var s in samples)
                if (!Finite(s.position.x) || !Finite(s.position.y) || !Finite(s.position.z) || Mathf.Abs(s.position.y) > 0.001f ||
                    !Finite(s.curvature) || !Finite(s.maximumSafeSpeed) || (s.maximumSafeSpeed < 0 && s.maximumSafeSpeed != -1))
                    throw new ArgumentException("Map samples must be finite planar geometry with nonnegative speeds (-1 = unlimited).");
            if (closedLoop && Vector3.Distance(samples[0].position, samples[samples.Length - 1].position) > 0.001f)
                throw new ArgumentException("Closed map endpoints do not meet.");
        }

        public void SaveNew(string path)
        {
            Validate();
            var fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            using var file = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write);
            using var writer = new StreamWriter(file);
            writer.Write(JsonUtility.ToJson(this, true));
        }
        public static CourseMapDocument Load(string path)
        {
            var result = JsonUtility.FromJson<CourseMapDocument>(File.ReadAllText(path));
            if (result == null) throw new ArgumentException("Empty map file.");
            result.Validate();
            return result;
        }
        static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        static string Hash(string value)
        {
            using var algorithm = SHA256.Create();
            return BitConverter.ToString(algorithm.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
        }
    }
}
