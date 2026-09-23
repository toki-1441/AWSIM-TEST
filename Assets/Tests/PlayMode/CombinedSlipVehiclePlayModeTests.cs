using System.Reflection;
using System.Collections;
using NUnit.Framework;
using PhysicalAi.Course;
using PhysicalAi.Planning;
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
        public IEnumerator TT02HighCombinedSlip_RemainsInsideAxleFrictionCircles()
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
            var evaluatedFrames = 0;
            for (var step = 0; step < 120; step++)
            {
                yield return new WaitForFixedUpdate();
                maximumUtilization = Mathf.Max(maximumUtilization, vehicle.PeakTireUtilization);
                if (vehicle.FrontAxleForce.NormalLoad > 0 && vehicle.RearAxleForce.NormalLoad > 0)
                    evaluatedFrames++;
            }

            Assert.That(evaluatedFrames, Is.GreaterThan(20));
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

            // Include startup: the planar model has no suspension drop-in transient.
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

            // No suspension/load-transfer headroom: the planar tire limit applies each step.
            var tractionLimit = specification.SurfaceFriction * Physics.gravity.magnitude;
            Assert.That(maximumStepAcceleration, Is.LessThan(tractionLimit * 1.01f),
                $"peak step acceleration {maximumStepAcceleration:F1} m/s² against a {tractionLimit:F1} m/s² traction limit");
            Object.Destroy(root);
            Object.Destroy(ground);
        }

        [UnityTest]
        public IEnumerator ManualInput_DrivesAndSteersTheVehicle()
        {
            // EditMode already covers that direct input reaches the actuators, but not
            // that it produces motion. Manual data collection depends on the whole
            // path: operator input, actuation, tire forces, body motion.
            var specification = VehicleSpecification.Default;
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.transform.position = new Vector3(0f, -0.1f, 0f);
            ground.transform.localScale = new Vector3(200f, 0.2f, 200f);

            var root = new GameObject("TT02ManualInputTest");
            root.transform.position = new Vector3(0f, specification.BodyCenterHeight, 0f);
            var body = root.AddComponent<Rigidbody>();
            body.mass = specification.Mass;
            body.centerOfMass = Vector3.up * (specification.CenterOfGravityHeight - specification.BodyCenterHeight);
            body.inertiaTensor = specification.InertiaTensor;
            body.constraints = PlanarConstraints;
            var vehicle = root.AddComponent<CombinedSlipVehicle>();
            ConfigureVehicle(vehicle, root.transform, specification);
            var planner = root.AddComponent<KeyboardTeleoperationPlanner>();
            SetField(planner, "_vehicle", vehicle);

            for (var settle = 0; settle < 60; settle++)
                yield return new WaitForFixedUpdate();

            var startPosition = body.position;
            for (var step = 0; step < 120; step++)
            {
                planner.ApplyDirectInput(1f, 0f);
                yield return new WaitForFixedUpdate();
            }
            var travelled = Vector3.Distance(body.position, startPosition);
            Assert.That(travelled, Is.GreaterThan(0.2f), $"throttle moved the vehicle {travelled:F3} m");

            var headingBeforeSteering = body.rotation.eulerAngles.y;
            for (var step = 0; step < 120; step++)
            {
                planner.ApplyDirectInput(1f, 1f);
                yield return new WaitForFixedUpdate();
            }
            var headingChange = Mathf.Abs(Mathf.DeltaAngle(headingBeforeSteering, body.rotation.eulerAngles.y));
            Assert.That(headingChange, Is.GreaterThan(5f), $"steering turned the vehicle {headingChange:F1} degrees");

            Object.Destroy(root);
            Object.Destroy(ground);
        }

        [UnityTest]
        public IEnumerator TT02AppliesPlanarConstraintsAndGeneratesNoStationaryTorque()
        {
            // An operator reported the car still rotating after it had stopped against a
            // wall, and shaking at low speed. TT02AtRest_DoesNotBounce cannot
            // see either: it freezes roll and pitch, so any spurious torque is absorbed
            // by the constraint instead of showing up.
            var specification = VehicleSpecification.Default;
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.transform.position = new Vector3(0f, -0.1f, 0f);
            ground.transform.localScale = new Vector3(20f, 0.2f, 20f);

            var root = new GameObject("TT02FreeAttitudeRest");
            root.transform.position = new Vector3(0f, specification.BodyCenterHeight, 0f);
            var body = root.AddComponent<Rigidbody>();
            body.mass = specification.Mass;
            body.centerOfMass = Vector3.up * (specification.CenterOfGravityHeight - specification.BodyCenterHeight);
            body.inertiaTensor = specification.InertiaTensor;
            var vehicle = root.AddComponent<CombinedSlipVehicle>();
            ConfigureVehicle(vehicle, root.transform, specification);

            for (var settle = 0; settle < 120; settle++)
                yield return new WaitForFixedUpdate();

            var peakAngularSpeed = 0f;
            for (var step = 0; step < 200; step++)
            {
                yield return new WaitForFixedUpdate();
                peakAngularSpeed = Mathf.Max(peakAngularSpeed, body.angularVelocity.magnitude);
            }

            // CombinedSlipVehicle holds the attitude flat, so a stationary vehicle stays
            // still even though the roll mode is too stiff to integrate explicitly at
            // 100 Hz. Without that the same setup reached 5.6 rad/s with no input.
            Assert.That(body.constraints & CombinedSlipVehicle.PlanarMotionConstraints,
                Is.EqualTo(CombinedSlipVehicle.PlanarMotionConstraints),
                "the vehicle did not apply its planar motion constraints");
            Assert.That(peakAngularSpeed, Is.LessThan(0.05f),
                $"stationary vehicle reached {peakAngularSpeed:F3} rad/s of angular velocity with no input");
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

        [UnityTest]
        public IEnumerator StreamingLifecycle_PreservesPlanarStateAndNextStepDynamics()
        {
            var specification = VehicleSpecification.Default;
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.transform.position = new Vector3(0f, -0.1f, 0f);
            ground.transform.localScale = new Vector3(1000f, 0.2f, 1000f);
            ground.layer = LayerMask.NameToLayer(ProceduralCourseTrackBuilder.DrivableSurfaceLayerName);
            var track = new GameObject("StreamingContactTrack");
            var builder = track.AddComponent<ProceduralCourseTrackBuilder>();
            var ego = new GameObject("StreamingContactEgo");
            var control = new GameObject("UnmodifiedWorldControl");
            control.transform.position = Vector3.right * 300f;
            try
            {
                var egoVehicle = CreateContactVehicle(ego, specification, ground.layer);
                var controlVehicle = CreateContactVehicle(control, specification, ground.layer);
                var egoBody = ego.GetComponent<Rigidbody>();
                var controlBody = control.GetComponent<Rigidbody>();
                var progress = ego.AddComponent<CourseProgressMonitor>();
                var orchestrator = ego.AddComponent<PhysicalAiEpisodeOrchestrator>();
                SetField(orchestrator, "_vehicle", egoVehicle);
                SetField(orchestrator, "_trackBuilder", builder);
                SetField(orchestrator, "_progressMonitor", progress);
                SetField(orchestrator, "_useStreamingTraining", true);
                // Trigger the production rebase path frequently within a short test.
                SetField(orchestrator, "_floatingOriginThreshold", 1f);
                // The fixture owns command timing and resets; world maintenance
                // still goes through the production public refresh entry point.
                orchestrator.enabled = false;
                orchestrator.SendMessage("Awake");
                Assert.That(Quaternion.Angle(egoBody.inertiaTensorRotation, Quaternion.identity), Is.LessThan(0.001f),
                    $"ego inertia axes after activation {egoBody.inertiaTensorRotation.eulerAngles}, constraints {egoBody.constraints}");
                orchestrator.ResetEpisode(12345);
                var transitions = new System.Collections.Generic.List<StreamingCourseTransition>();
                orchestrator.StreamingTransitioned += transitions.Add;

                for (var cycle = 0; cycle < 4; cycle++)
                {
                    var course = orchestrator.Course;
                    var index = course.Centerline.Count - 20;
                    var heading = Quaternion.LookRotation(course.Centerline[index + 1] - course.Centerline[index]);
                    egoBody.position = course.Centerline[index] + Vector3.up * specification.BodyCenterHeight;
                    controlBody.position = new Vector3(300f, specification.BodyCenterHeight, 0f);
                    egoBody.rotation = controlBody.rotation = heading;
                    egoBody.constraints = controlBody.constraints = PlanarConstraints;
                    egoBody.linearVelocity = controlBody.linearVelocity = Vector3.zero;
                    egoBody.angularVelocity = controlBody.angularVelocity = Vector3.zero;
                    egoVehicle.ResetState();
                    controlVehicle.ResetState();
                    egoVehicle.Throttle = controlVehicle.Throttle = 0.4f;
                    egoVehicle.Steering = controlVehicle.Steering = 0.1f;
                    Physics.SyncTransforms();
                    for (var step = 0; step < 3; step++)
                    {
                        yield return new WaitForFixedUpdate();
                    }
                    egoBody.constraints = controlBody.constraints = PlanarConstraints;
                    Assert.That((int)egoBody.constraints, Is.EqualTo(84));
                    Assert.That(orchestrator.RefreshStreamingCourseForCurrentPosition(), Is.True);

                    // Advance actual physics after world maintenance, comparing against an
                    // identical vehicle/command stream away from walls on the unchanged plane.
                    for (var step = 0; step < 8; step++)
                    {
                        yield return new WaitForFixedUpdate();
                        Assert.That((egoBody.linearVelocity - controlBody.linearVelocity).magnitude,
                            Is.LessThan(0.002f), $"cycle {cycle}, step {step}: velocity diverged after streaming");
                        Assert.That((egoBody.angularVelocity - controlBody.angularVelocity).magnitude,
                            Is.LessThan(0.002f), $"cycle {cycle}, step {step}: yaw diverged after streaming");
                        Assert.That(egoVehicle.ActualSteeringAngleRadians,
                            Is.EqualTo(controlVehicle.ActualSteeringAngleRadians));
                        Assert.That(egoVehicle.PeakTireUtilization, Is.LessThanOrEqualTo(1.00001f));
                    }
                }
                foreach (var kind in new[] { StreamingCourseTransitionKind.AppendAndRetire,
                    StreamingCourseTransitionKind.HistoryTrim, StreamingCourseTransitionKind.FloatingOriginRebase })
                    Assert.That(transitions.Exists(transition => transition.Kind == kind), Is.True, kind.ToString());
                foreach (var transition in transitions)
                {
                    Assert.That(transition.ActualSteeringAngleDelta, Is.Zero);
                    Assert.That(transition.MaximumAxleForceDelta, Is.Zero);
                    Assert.That(transition.DynamicWeightDelta, Is.Zero);
                    Assert.That(transition.ControlInputDelta, Is.EqualTo(Vector3.zero));
                }
            }
            finally
            {
                Object.Destroy(ego);
                Object.Destroy(control);
                Object.Destroy(track);
                Object.Destroy(ground);
            }
        }

        static CombinedSlipVehicle CreateContactVehicle(GameObject root, VehicleSpecification specification, int groundLayer)
        {
            root.transform.position += Vector3.up * specification.BodyCenterHeight;
            var body = root.AddComponent<Rigidbody>();
            body.mass = specification.Mass;
            body.centerOfMass = Vector3.up * (specification.CenterOfGravityHeight - specification.BodyCenterHeight);
            // Match the generated scene's principal axes. Rotation constraints
            // use inertia space; an automatically rotated tensor can permit pitch
            // and roll even when the body has FreezeRotationX/Z configured.
            body.constraints = PlanarConstraints;
            root.AddComponent<BoxCollider>().size = new Vector3(specification.BodyWidth, specification.BodyHeight, specification.BodyLength);
            body.inertiaTensor = specification.InertiaTensor;
            body.inertiaTensorRotation = Quaternion.identity;
            var vehicle = root.AddComponent<CombinedSlipVehicle>();
            ConfigureVehicle(vehicle, root.transform, specification);
            return vehicle;
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
            vehicle.Configure(specification);
        }

        static void SetField(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }
}
