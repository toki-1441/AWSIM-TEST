using NUnit.Framework;
using PhysicalAi.Sensors;
using UnityEngine;

namespace PhysicalAi.Tests.EditMode
{
    public class SensorRigTests
    {
        [Test]
        public void SensorMount_StoresFrameIdentityAndConfiguresLocalPose()
        {
            var parent = new GameObject("vehicle");
            var child = new GameObject("camera");
            child.transform.SetParent(parent.transform, false);
            var mount = child.AddComponent<SensorMount>();

            mount.Configure(SensorModality.Camera, "camera_front", new Vector3(0f, 0.25f, 0.6f), Quaternion.Euler(1f, 2f, 3f));

            Assert.That(mount.Modality, Is.EqualTo(SensorModality.Camera));
            Assert.That(mount.FrameId, Is.EqualTo("camera_front"));
            Assert.That(mount.transform.localPosition, Is.EqualTo(new Vector3(0f, 0.25f, 0.6f)));
            Assert.That(mount.transform.localRotation, Is.EqualTo(Quaternion.Euler(1f, 2f, 3f)));
            Object.DestroyImmediate(parent);
        }

        [Test]
        public void PlanarLidar_ReturnsMaximumRangeWhenNoColliderIsVisible()
        {
            var sensor = new GameObject("lidar").AddComponent<PlanarLidarSensor>();

            var ranges = sensor.ScanRanges();

            Assert.That(ranges, Has.Length.EqualTo(181));
            Assert.That(ranges[0], Is.EqualTo(30f));
            Object.DestroyImmediate(sensor.gameObject);
        }
    }
}
