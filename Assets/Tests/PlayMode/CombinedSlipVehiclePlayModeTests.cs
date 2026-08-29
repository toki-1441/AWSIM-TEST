using System.Reflection;
using System.Collections;
using NUnit.Framework;
using PhysicalAi.VehicleDynamics;
using UnityEngine;
using UnityEngine.TestTools;

namespace PhysicalAi.Tests.PlayMode
{
    public class CombinedSlipVehiclePlayModeTests
    {
        [UnityTest]
        public IEnumerator TT02HighCombinedSlip_RemainsInsideFrictionCircleWhileGrounded()
        {
            var specification = VehicleSpecification.Default;
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.transform.position = new Vector3(0f, -0.1f, 0f);
            ground.transform.localScale = new Vector3(20f, 0.2f, 80f);

            var root = new GameObject("TT02DynamicsTest");
            root.transform.position = new Vector3(0f, 0.049f, 0f);
            var body = root.AddComponent<Rigidbody>();
            body.mass = specification.Mass;
            body.centerOfMass = Vector3.up * specification.CenterOfGravityHeight;
            body.inertiaTensor = specification.InertiaTensor;
            var vehicle = root.AddComponent<CombinedSlipVehicle>();
            ConfigureVehicle(vehicle, root.transform, specification);
            vehicle.Throttle = 1f;
            vehicle.Steering = 0.8f;

            var maximumUtilization = 0f;
            var groundedFrames = 0;
            for (var step = 0; step < 120; step++)
            {
                yield return new WaitForFixedUpdate();
                maximumUtilization = Mathf.Max(maximumUtilization, vehicle.PeakTireUtilization);
                if (vehicle.GroundedWheelCount > 0)
                    groundedFrames++;
            }

            Assert.That(groundedFrames, Is.GreaterThan(20));
            Assert.That(maximumUtilization, Is.GreaterThan(0.1f));
            Assert.That(maximumUtilization, Is.LessThanOrEqualTo(1.0001f));
            Assert.That(float.IsNaN(body.linearVelocity.sqrMagnitude), Is.False);
            Object.Destroy(root);
            Object.Destroy(ground);
        }

        static void ConfigureVehicle(CombinedSlipVehicle vehicle, Transform root, VehicleSpecification specification)
        {
            SetField(vehicle, "_surfaceFriction", specification.SurfaceFriction);
            SetField(vehicle, "_maximumDriveTorque", specification.MaximumDriveTorquePerWheel);
            SetField(vehicle, "_maximumBrakeTorque", specification.MaximumBrakeTorquePerWheel);
            SetField(vehicle, "_maximumSteeringAngle", specification.MaximumSteeringAngleDegrees);
            var wheels = new CombinedSlipVehicle.Wheel[4];
            for (var index = 0; index < wheels.Length; index++)
            {
                var mount = new GameObject($"Wheel{index}").transform;
                mount.SetParent(root, false);
                mount.localPosition = new Vector3(index % 2 == 0 ? -specification.TrackWidth * 0.5f : specification.TrackWidth * 0.5f, 0f, index < 2 ? specification.DistanceToFrontAxle : -specification.DistanceToRearAxle);
                var wheel = new CombinedSlipVehicle.Wheel();
                SetField(wheel, "_suspensionMount", mount);
                SetField(wheel, "_steerable", index < 2);
                SetField(wheel, "_radius", specification.TireRadius);
                SetField(wheel, "_suspensionRestLength", specification.SuspensionRestLength);
                SetField(wheel, "_springRate", specification.SpringRate);
                SetField(wheel, "_damperRate", specification.DamperRate);
                SetField(wheel, "_rotationalInertia", specification.WheelRotationalInertia);
                wheels[index] = wheel;
            }
            SetField(vehicle, "_wheels", wheels);
        }

        static void SetField(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }
}
