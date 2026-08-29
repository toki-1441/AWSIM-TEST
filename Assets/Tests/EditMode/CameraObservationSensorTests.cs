using NUnit.Framework;
using PhysicalAi.Sensors;
using UnityEngine;

namespace PhysicalAi.Tests.EditMode
{
    public class CameraObservationSensorTests
    {
        [Test]
        public void CurrentFrame_ExposesRenderedImageContract()
        {
            var root = new GameObject("CameraObservation");
            root.AddComponent<Camera>();
            var sensor = root.AddComponent<CameraObservationSensor>();

            var frame = sensor.CurrentFrame();

            Assert.That(frame.Image, Is.Not.Null);
            Assert.That(frame.Image.width, Is.EqualTo(160));
            Assert.That(frame.Image.height, Is.EqualTo(90));
            Object.DestroyImmediate(root);
        }
    }
}
