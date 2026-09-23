using System.Collections;
using NUnit.Framework;
using PhysicalAi.VehicleDynamics;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PhysicalAi.Tests.PlayMode
{
    /// <summary>Real PhysX in an isolated scene; no frame timing or graphical device dependency.</summary>
    public class SingleTrackVehiclePlayModeTests
    {
        const float Dt = 0.01f;
        Scene _scene;
        PhysicsScene _physics;

        [SetUp]
        public void SetUp()
        {
            _scene = SceneManager.CreateScene("SingleTrackPhysics", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            _physics = _scene.GetPhysicsScene();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_scene.IsValid()) yield return SceneManager.UnloadSceneAsync(_scene);
        }

        CombinedSlipVehicle CreateVehicle(Vector3 position = default)
        {
            var root = new GameObject("SingleTrackTestVehicle");
            SceneManager.MoveGameObjectToScene(root, _scene);
            root.transform.position = position;
            var spec = VehicleSpecification.Default;
            root.AddComponent<BoxCollider>().size = new Vector3(spec.BodyWidth, spec.BodyHeight, spec.BodyLength);
            var vehicle = root.AddComponent<CombinedSlipVehicle>();
            vehicle.enabled = false; // Only the fixture advances this local physics scene.
            vehicle.Configure(spec);
            return vehicle;
        }

        void Step(params CombinedSlipVehicle[] vehicles)
        {
            foreach (var vehicle in vehicles) vehicle.ApplyTireForces(Dt);
            _physics.Simulate(Dt);
        }

        [Test]
        public void RestAndSteeringRemainPlanarWithoutSelfExcitation()
        {
            var vehicle = CreateVehicle(new Vector3(0, 0.1f, 0));
            var body = vehicle.GetComponent<Rigidbody>();
            for (var i = 0; i < 2000; i++)
            {
                vehicle.Steering = Mathf.Sin(i * 0.01f);
                Step(vehicle);
                Assert.That(body.linearVelocity.sqrMagnitude, Is.LessThan(1e-12f));
                Assert.That(body.angularVelocity.sqrMagnitude, Is.LessThan(1e-12f));
                Assert.That(body.position.y, Is.EqualTo(0.1f).Within(1e-7f));
            }
            Assert.That(body.useGravity, Is.False);
            Assert.That(body.constraints & CombinedSlipVehicle.PlanarMotionConstraints,
                Is.EqualTo(CombinedSlipVehicle.PlanarMotionConstraints));
        }

        [TestCase(1f)]
        [TestCase(-1f)]
        public void PositivePublicSteeringTurnsRightInForwardAndLeftInReverse(float throttle)
        {
            var vehicle = CreateVehicle();
            vehicle.Throttle = throttle * 0.3f;
            vehicle.Steering = 0.3f;
            for (var i = 0; i < 100; i++) Step(vehicle);
            var body = vehicle.GetComponent<Rigidbody>();
            Assert.That(body.angularVelocity.y * throttle, Is.GreaterThan(0.1f));
            Assert.That(vehicle.ActualSteeringAngleRadians, Is.GreaterThan(0));
            Assert.That(vehicle.CaptureState().YawRate, Is.EqualTo(-body.angularVelocity.y).Within(1e-7));
        }

        [Test]
        public void StraightAccelerationAndBrakingMatchTheCalculation()
        {
            var vehicle = CreateVehicle();
            var body = vehicle.GetComponent<Rigidbody>();
            var model = new SingleTrackVehicleModel(SingleTrackParameters.FromSpecification(vehicle.Specification));
            var state = new SingleTrackState();
            var input = new SingleTrackInput { Throttle = 0.4, SurfaceFriction = vehicle.SurfaceFriction };
            vehicle.Throttle = 0.4f;
            for (var i = 0; i < 500; i++)
            {
                if (i == 300) { input.Brake = 1; vehicle.Brake = 1; }
                state = model.Step(state, input, Dt).State;
                Step(vehicle);
                Assert.That(body.linearVelocity.z, Is.EqualTo(state.LongitudinalVelocity).Within(1e-4));
                Assert.That(body.linearVelocity.x, Is.EqualTo(0).Within(1e-6));
                Assert.That(vehicle.PeakTireUtilization, Is.LessThanOrEqualTo(1.00001f));
            }
            Assert.That(body.linearVelocity.magnitude, Is.LessThan(1e-5f));
        }

        [Test]
        public void ExternalCollisionVelocityIsReadInsteadOfRestoringPrediction()
        {
            var vehicle = CreateVehicle();
            var body = vehicle.GetComponent<Rigidbody>();
            vehicle.Throttle = 1;
            for (var i = 0; i < 100; i++) Step(vehicle);
            Assert.That(body.linearVelocity.z, Is.GreaterThan(2));

            // A collision solver, episode reset or external impulse can change these.
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            vehicle.Throttle = 0;
            Step(vehicle);
            Assert.That(body.linearVelocity.sqrMagnitude, Is.LessThan(1e-12f));
            Assert.That(body.angularVelocity.sqrMagnitude, Is.LessThan(1e-12f));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RealWallStopsTheDrivenVehicleAndItCanBackAway(bool noSlip)
        {
            var vehicle = CreateVehicle();
            vehicle.NoSlipAssistance = noSlip;
            var body = vehicle.GetComponent<Rigidbody>();
            var wall = new GameObject("CollisionWall");
            SceneManager.MoveGameObjectToScene(wall, _scene);
            wall.transform.position = new Vector3(0, 0, 1);
            wall.AddComponent<BoxCollider>().size = new Vector3(4, 1, 0.02f);
            Physics.SyncTransforms();
            vehicle.Throttle = 1;
            for (var i = 0; i < 300; i++)
            {
                Step(vehicle);
                Assert.That(body.position.z, Is.LessThan(0.82f), "The model must not teleport through the wall.");
                Assert.That(Mathf.Abs(body.position.y), Is.LessThan(1e-6f));
            }
            Assert.That(body.position.z, Is.GreaterThan(0.7f), "The vehicle must actually reach the wall.");
            Assert.That(body.linearVelocity.magnitude, Is.LessThan(0.1f));
            Assert.That(body.angularVelocity.magnitude, Is.LessThan(0.05f));
            vehicle.Throttle = -0.4f;
            for (var i = 0; i < 100; i++) Step(vehicle);
            Assert.That(body.position.z, Is.LessThan(0));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FloatingOriginPreservesTheNextPhysicsStepsAndActuatorState(bool noSlip)
        {
            var rebased = CreateVehicle(new Vector3(200, 0, 100));
            var control = CreateVehicle(new Vector3(-200, 0, -100));
            rebased.NoSlipAssistance = control.NoSlipAssistance = noSlip;
            var a = rebased.GetComponent<Rigidbody>();
            var b = control.GetComponent<Rigidbody>();
            a.rotation = b.rotation = Quaternion.Euler(0, 35, 0);
            rebased.Throttle = control.Throttle = 0.35f;
            rebased.Steering = control.Steering = 0.2f;
            for (var i = 0; i < 100; i++) Step(rebased, control);
            var steering = rebased.ActualSteeringAngleRadians;
            var offset = -a.position;
            a.position += offset;
            Physics.SyncTransforms();
            Assert.That(rebased.ActualSteeringAngleRadians, Is.EqualTo(steering));
            Assert.That(rebased.CaptureState().X, Is.EqualTo(a.position.z));
            Assert.That(rebased.CaptureState().Y, Is.EqualTo(-a.position.x));
            for (var i = 0; i < 200; i++)
            {
                Step(rebased, control);
                Assert.That((a.linearVelocity - b.linearVelocity).magnitude, Is.LessThan(0.001f));
                Assert.That((a.angularVelocity - b.angularVelocity).magnitude, Is.LessThan(0.001f));
                Assert.That(Quaternion.Angle(a.rotation, b.rotation), Is.LessThan(0.1f));
                Assert.That(rebased.ActualSteeringAngleRadians, Is.EqualTo(control.ActualSteeringAngleRadians));
            }
        }

        [TestCase(1f)]
        [TestCase(-1f)]
        public void NoSlipAidTurnsWithoutPersistentSidewaysVelocityAndStopsSpinningOnRelease(float direction)
        {
            var vehicle = CreateVehicle();
            vehicle.NoSlipAssistance = true;
            var body = vehicle.GetComponent<Rigidbody>();
            body.linearVelocity = Vector3.forward * (4f * direction);
            vehicle.Steering = 0.5f;
            for (var i = 0; i < 200; i++)
            {
                Step(vehicle);
                var local = Quaternion.Inverse(body.rotation) * body.linearVelocity;
                Assert.That(Mathf.Abs(local.x), Is.LessThan(0.09f), "Only a half-timestep heading residual is allowed, not accumulating drift.");
                Assert.That(body.position.y, Is.Zero);
                Assert.That(body.angularVelocity.y * direction, Is.GreaterThan(0));
            }
            vehicle.Steering = 0;
            for (var i = 0; i < 20; i++) Step(vehicle);
            Assert.That(body.angularVelocity.magnitude, Is.LessThan(1e-6f));
            Assert.That(Mathf.Abs((Quaternion.Inverse(body.rotation) * body.linearVelocity).x), Is.LessThan(1e-5f));
            vehicle.Brake = 1;
            for (var i = 0; i < 200; i++) Step(vehicle);
            Assert.That(body.linearVelocity.magnitude, Is.LessThan(1e-5f));
        }

        [Test]
        public void NoSlipAidDiscardsSidewaysImpulseWithoutRestoringOldSpeedOrFakingTireTelemetry()
        {
            var vehicle = CreateVehicle();
            vehicle.NoSlipAssistance = true;
            var body = vehicle.GetComponent<Rigidbody>();
            body.linearVelocity = new Vector3(3, 0, 2);
            body.angularVelocity = Vector3.up * 5;
            Step(vehicle);
            Assert.That(body.linearVelocity.x, Is.Zero.Within(1e-6));
            // Coasting bleeds a little forward speed here as it does in every other
            // mode. What this test is about is the sideways 3 m/s: it must be
            // discarded outright, never turned into forward speed the driver did
            // not ask for, so the retained speed may fall short of 2 but not exceed it.
            Assert.That(body.linearVelocity.z, Is.LessThanOrEqualTo(2f).And.GreaterThan(1.9f));
            Assert.That(body.angularVelocity.magnitude, Is.Zero.Within(1e-6));
            body.linearVelocity = Vector3.zero;
            Step(vehicle);
            Assert.That(body.linearVelocity.magnitude, Is.Zero.Within(1e-6));
            Assert.That(vehicle.VehicleModelName, Is.EqualTo("planar-no-slip-assist"));
            Assert.That(vehicle.FrontAxleForce.NormalLoad, Is.Zero);
            Assert.That(vehicle.RearAxleForce.NormalLoad, Is.Zero);
        }

        [Test]
        public void ResetClearsActuatorMemoryButLeavesPoseToEpisodeOwner()
        {
            var vehicle = CreateVehicle();
            var body = vehicle.GetComponent<Rigidbody>();
            vehicle.Steering = 1;
            vehicle.Throttle = 0.3f;
            for (var i = 0; i < 50; i++) Step(vehicle);
            var position = body.position;
            var velocity = body.linearVelocity;
            vehicle.ResetState();
            Assert.That(vehicle.Throttle, Is.Zero);
            Assert.That(vehicle.Brake, Is.Zero);
            Assert.That(vehicle.Steering, Is.Zero);
            Assert.That(vehicle.ActualSteeringAngleRadians, Is.Zero);
            Assert.That(vehicle.FrontAxleForce.Longitudinal, Is.Zero);
            Assert.That(body.position, Is.EqualTo(position));
            Assert.That(body.linearVelocity, Is.EqualTo(velocity));
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            Step(vehicle);
            Assert.That(body.linearVelocity.magnitude, Is.LessThan(1e-7f));
        }
    }
}
