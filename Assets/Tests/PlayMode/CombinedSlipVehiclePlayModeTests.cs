using System.Reflection;
using System.Collections;
using NUnit.Framework;
using PhysicalAi.Course;
using PhysicalAi.VehicleDynamics;
using PhysicalAi.Sensors;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Rendering;

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
            root.transform.position = new Vector3(0f, specification.BodyCenterHeight, 0f);
            var body = root.AddComponent<Rigidbody>();
            body.mass = specification.Mass;
            body.centerOfMass = Vector3.up * (specification.CenterOfGravityHeight - specification.BodyCenterHeight);
            body.inertiaTensor = specification.InertiaTensor;
            body.constraints = PlanarConstraints;
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

        [UnityTest]
        public IEnumerator TT02UnderFullThrottle_KeepsStepAccelerationPhysical()
        {
            var specification = VehicleSpecification.Default;
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.transform.position = new Vector3(0f, -0.1f, 0f);
            ground.transform.localScale = new Vector3(20f, 0.2f, 200f);

            var root = new GameObject("TT02StabilityTest");
            root.transform.position = new Vector3(0f, specification.BodyCenterHeight, 0f);
            var body = root.AddComponent<Rigidbody>();
            body.mass = specification.Mass;
            body.centerOfMass = Vector3.up * (specification.CenterOfGravityHeight - specification.BodyCenterHeight);
            body.inertiaTensor = specification.InertiaTensor;
            body.constraints = PlanarConstraints;
            var vehicle = root.AddComponent<CombinedSlipVehicle>();
            ConfigureVehicle(vehicle, root.transform, specification);
            vehicle.Throttle = 1f;

            // Let the suspension settle before measuring, so the drop-in transient is
            // not mistaken for a driving oscillation.
            for (var settle = 0; settle < 60; settle++)
                yield return new WaitForFixedUpdate();

            // Horizontal only: vertical hop would otherwise be read as surging.
            var previousSpeed = HorizontalSpeed(body);
            var maximumStepAcceleration = 0f;
            for (var step = 0; step < 200; step++)
            {
                yield return new WaitForFixedUpdate();
                var speed = HorizontalSpeed(body);
                maximumStepAcceleration = Mathf.Max(maximumStepAcceleration, Mathf.Abs(speed - previousSpeed) / Time.fixedDeltaTime);
                previousSpeed = speed;
            }

            // The tires cannot push the body harder than the friction available to them.
            // Headroom is allowed for load transfer and suspension transients; before the
            // wheel spin update was made implicit this reached roughly 60 m/s².
            var tractionLimit = specification.SurfaceFriction * Physics.gravity.magnitude;
            Assert.That(maximumStepAcceleration, Is.LessThan(tractionLimit * 3f),
                $"peak step acceleration {maximumStepAcceleration:F1} m/s² against a {tractionLimit:F1} m/s² traction limit");
            Object.Destroy(root);
            Object.Destroy(ground);
        }

        [UnityTest]
        public IEnumerator TT02AtRest_DoesNotPumpItsSuspension()
        {
            var specification = VehicleSpecification.Default;
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.transform.position = new Vector3(0f, -0.1f, 0f);
            ground.transform.localScale = new Vector3(20f, 0.2f, 80f);

            var root = new GameObject("TT02RestTest");
            root.transform.position = new Vector3(0f, specification.BodyCenterHeight, 0f);
            var body = root.AddComponent<Rigidbody>();
            body.mass = specification.Mass;
            body.centerOfMass = Vector3.up * (specification.CenterOfGravityHeight - specification.BodyCenterHeight);
            body.inertiaTensor = specification.InertiaTensor;
            body.constraints = PlanarConstraints;
            var vehicle = root.AddComponent<CombinedSlipVehicle>();
            ConfigureVehicle(vehicle, root.transform, specification);

            for (var settle = 0; settle < 200; settle++)
                yield return new WaitForFixedUpdate();

            var lowestHeight = body.position.y;
            var highestHeight = body.position.y;
            var maximumVerticalSpeed = 0f;
            for (var step = 0; step < 300; step++)
            {
                yield return new WaitForFixedUpdate();
                lowestHeight = Mathf.Min(lowestHeight, body.position.y);
                highestHeight = Mathf.Max(highestHeight, body.position.y);
                maximumVerticalSpeed = Mathf.Max(maximumVerticalSpeed, Mathf.Abs(body.linearVelocity.y));
            }

            Assert.That(highestHeight - lowestHeight, Is.LessThan(0.002f),
                $"rest ride-height excursion {highestHeight - lowestHeight:F4} m, maximum vertical speed {maximumVerticalSpeed:F4} m/s");
            Assert.That(maximumVerticalSpeed, Is.LessThan(0.03f),
                $"rest vertical speed {maximumVerticalSpeed:F3} m/s");
            Object.Destroy(root);
            Object.Destroy(ground);
        }

        [UnityTest]
        public IEnumerator CourseBoundaries_BlockThePhysicalVehicleBody()
        {
            var trackRoot = new GameObject("BoundaryCollisionTrack");
            var builder = trackRoot.AddComponent<ProceduralCourseTrackBuilder>();
            var points = new System.Collections.Generic.List<Vector3>();
            var curvature = new System.Collections.Generic.List<float>();
            var safeSpeed = new System.Collections.Generic.List<float>();
            for (var z = -4f; z <= 4f; z += 0.1f)
            {
                points.Add(new Vector3(0f, 0f, z));
                curvature.Add(0f);
                safeSpeed.Add(float.PositiveInfinity);
            }
            builder.Build(new GeneratedCourse(points, curvature, safeSpeed));

            var specification = VehicleSpecification.Default;
            var vehicle = new GameObject("BoundaryCollisionVehicle");
            vehicle.transform.position = new Vector3(0f, specification.BodyCenterHeight, 0f);
            var body = vehicle.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.mass = specification.Mass;
            body.constraints = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotation;
            var collider = vehicle.AddComponent<BoxCollider>();
            collider.size = new Vector3(specification.BodyWidth, specification.BodyHeight, specification.BodyLength);
            Physics.SyncTransforms();
            body.linearVelocity = Vector3.left * 4f;

            for (var step = 0; step < 45; step++)
                yield return new WaitForFixedUpdate();

            // The wall centre is x=-0.6 m.  The body half-width is 0.095 m,
            // so even allowing PhysX contact tolerance it must remain inside the
            // corridor rather than pass through the generated duct boundary.
            Assert.That(body.position.x, Is.GreaterThan(-0.63f));
            Assert.That(body.linearVelocity.x, Is.GreaterThan(-0.1f));
            Object.Destroy(vehicle);
            Object.Destroy(trackRoot);
        }

        [UnityTest]
        public IEnumerator ClearingTrack_DisablesOldBoundaryCollidersBeforeDeferredDestroy()
        {
            var trackRoot = new GameObject("TrackResetCollisionTest");
            var builder = trackRoot.AddComponent<ProceduralCourseTrackBuilder>();
            var points = new System.Collections.Generic.List<Vector3>
            {
                Vector3.zero, Vector3.forward
            };
            var course = new GeneratedCourse(points,
                new System.Collections.Generic.List<float> { 0f, 0f },
                new System.Collections.Generic.List<float> { float.PositiveInfinity, float.PositiveInfinity });
            builder.Build(course);
            var boundary = trackRoot.GetComponentInChildren<CourseBoundary>();
            Assert.That(boundary, Is.Not.Null);

            builder.Clear();

            Assert.That(boundary.gameObject.activeSelf, Is.False,
                "A new episode can reset in this frame, before Destroy runs at frame end.");
            yield return null;
            Object.Destroy(trackRoot);
        }

        static float HorizontalSpeed(Rigidbody body)
        {
            var velocity = body.linearVelocity;
            return new Vector2(velocity.x, velocity.z).magnitude;
        }

        static readonly RigidbodyConstraints PlanarConstraints = RigidbodyConstraints.FreezePositionY |
                                                                 RigidbodyConstraints.FreezeRotationX |
                                                                 RigidbodyConstraints.FreezeRotationZ;

        [UnityTest]
        public IEnumerator CameraObservation_CapturesRenderedFrame()
        {
            var root = new GameObject("CameraObservationTest");
            root.transform.position = new Vector3(0f, 0f, -2f);
            root.AddComponent<Camera>();
            var sensor = root.AddComponent<CameraObservationSensor>();
            var visible = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visible.transform.position = Vector3.forward * 2f;

            yield return null;
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Assert.That(sensor.Capture(), Is.False);
                Assert.That(sensor.FrameIndex, Is.Zero);
            }
            else
            {
                Assert.That(sensor.Capture(), Is.True);
                Assert.That(sensor.CurrentFrame().Image.IsCreated(), Is.True);
                Assert.That(sensor.FrameIndex, Is.EqualTo(1));
            }
            Object.Destroy(root);
            Object.Destroy(visible);
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
                mount.localPosition = new Vector3(index % 2 == 0 ? -specification.TrackWidth * 0.5f : specification.TrackWidth * 0.5f, -specification.BodyHeight * 0.5f, index < 2 ? specification.DistanceToFrontAxle : -specification.DistanceToRearAxle);
                var wheel = new CombinedSlipVehicle.Wheel();
                SetField(wheel, "_suspensionMount", mount);
                SetField(wheel, "_steerable", index < 2);
                SetField(wheel, "_radius", specification.TireRadius);
                SetField(wheel, "_suspensionRestLength", specification.SuspensionRestLength);
                SetField(wheel, "_springRate", specification.SpringRate);
                SetField(wheel, "_damperRate", specification.DamperRate);
                SetField(wheel, "_maximumNormalForce", specification.MaximumNormalForcePerWheel);
                SetField(wheel, "_rotationalInertia", specification.WheelRotationalInertia);
                wheels[index] = wheel;
            }
            SetField(vehicle, "_wheels", wheels);
        }

        static void SetField(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }
}
