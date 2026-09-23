// Copyright 2026
// SPDX-License-Identifier: Apache-2.0
using System.Collections;
using System.Linq;
using NUnit.Framework;
using PhysicalAi.Planning;
using PhysicalAi.Sensors;
using UnityEngine;
using UnityEngine.TestTools;

namespace PhysicalAi.Tests.PlayMode
{
    public class MetricDepthPlayModeTests
    {
        [UnityTest]
        public IEnumerator CameraDepthIsOpticalAxisMetresAndSkyIsExplicitlyInvalid()
        {
            PhysicalAiLaunchArguments.Override(new[] { "-physicalAiDepth", "true", "-physicalAiDepthMaxMeters", "10",
                "-physicalAiCameraWidth", "64", "-physicalAiCameraHeight", "48" });
            var root = new GameObject("MetricDepthTestCamera");
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Material material = null;
            try
            {
                wall.layer = 31;
                wall.transform.position = new Vector3(0, 0, 2.5f);
                wall.transform.localScale = new Vector3(20, 20, 1);
                material = new Material(Shader.Find("HDRP/Lit"));
                wall.GetComponent<Renderer>().sharedMaterial = material;
                var camera = root.AddComponent<Camera>();
                camera.cullingMask = 1 << 31;
                camera.nearClipPlane = 0.1f; camera.farClipPlane = 30;
                camera.enabled = false;
                var sensor = root.AddComponent<CameraObservationSensor>();
                Assert.That(camera.targetTexture, Is.SameAs(sensor.LatestImage), "Manifest calibration must use the sensor target even before first capture.");
                Assert.That(camera.aspect, Is.EqualTo(64f / 48).Within(1e-6));
                yield return null;
                Assert.That(sensor.Capture(), Is.True);
                var frame = sensor.CurrentFrame();
                Assert.That(frame.Depth, Is.Not.Null, sensor.DepthError);
                Assert.That(frame.Position, Is.EqualTo(root.transform.position));
                Assert.That(frame.Rotation, Is.EqualTo(root.transform.rotation));
                Assert.That(frame.Depth.Metres[24 * 64 + 32], Is.EqualTo(2f).Within(0.02f));
                Assert.That(frame.Depth.Metres[2 * 64 + 2], Is.EqualTo(2f).Within(0.02f), "Depth must be z, not Euclidean ray range.");
                Assert.That(frame.Depth.Valid.All(value => value == 1), Is.True);
                wall.transform.localScale = new Vector3(20, 1, 1);
                wall.transform.position = new Vector3(0, 1.2f, 2.5f);
                Assert.That(sensor.Capture(), Is.True);
                frame = sensor.CurrentFrame();
                Assert.That(frame.Depth.Valid[2 * 64 + 32], Is.EqualTo(1), "The top image rows must see the high wall.");
                Assert.That(frame.Depth.Valid[46 * 64 + 32], Is.Zero, "The bottom rows must remain sky; row orientation must be documented correctly.");
                wall.SetActive(false);
                Assert.That(sensor.Capture(), Is.True);
                frame = sensor.CurrentFrame();
                Assert.That(frame.Depth, Is.Not.Null);
                Assert.That(frame.Depth.Valid.All(value => value == 0), Is.True);
                Assert.That(frame.Depth.Metres.All(value => value == 0), Is.True);
            }
            finally
            {
                Object.Destroy(root); Object.Destroy(wall); if (material != null) Object.Destroy(material);
                PhysicalAiLaunchArguments.ClearOverride();
            }
            yield return null;
        }
    }
}
