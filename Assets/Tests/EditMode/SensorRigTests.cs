using NUnit.Framework;
using PhysicalAi.Sensors;
using UnityEditor;
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

        [Test]
        public void TransformFrameSensor_ReportsDynamicOdomToBaseLinkPose()
        {
            var vehicle = new GameObject("vehicle");
            vehicle.transform.SetPositionAndRotation(new Vector3(12f, 0.3f, -4f), Quaternion.Euler(0f, 35f, 0f));
            var body = vehicle.AddComponent<Rigidbody>();
            body.isKinematic = true;
            var sensorObject = new GameObject("base_link");
            sensorObject.transform.SetParent(vehicle.transform, false);
            var mount = sensorObject.AddComponent<SensorMount>();
            mount.Configure(SensorModality.TransformFrame, "base_link", Vector3.zero, Quaternion.identity);
            var sensor = sensorObject.AddComponent<TransformFrameSensor>();
            var serialized = new SerializedObject(sensor);
            serialized.FindProperty("_parentFrame").stringValue = "odom";
            serialized.FindProperty("_mount").objectReferenceValue = mount;
            serialized.FindProperty("_poseSource").objectReferenceValue = vehicle.transform;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var sample = sensor.Sample();

            Assert.That(sample.ParentFrame, Is.EqualTo("odom"));
            Assert.That(sample.ChildFrame, Is.EqualTo("base_link"));
            Assert.That(sample.LocalPosition, Is.EqualTo(vehicle.transform.position));
            Assert.That(sample.LocalRotation, Is.EqualTo(vehicle.transform.rotation));

            var floatingOriginOffset = new Vector3(-50f, 0f, 25f);
            sensor.ApplyFloatingOriginOffset(floatingOriginOffset);
            body.position += floatingOriginOffset;
            var rebased = sensor.Sample();
            Assert.That(rebased.LocalPosition, Is.EqualTo(sample.LocalPosition),
                "Floating-origin motion must not reset the odometry TF position.");
            sensor.ResetFloatingOrigin();
            Assert.That(sensor.Sample().LocalPosition, Is.EqualTo(body.position));
            Object.DestroyImmediate(vehicle);
        }
    }
}
