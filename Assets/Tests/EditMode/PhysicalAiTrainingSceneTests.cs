using NUnit.Framework;
using PhysicalAi.Planning;
using PhysicalAi.Randomization;
using PhysicalAi.Sensors;
using PhysicalAi.Evaluation;
using PhysicalAi.Calibration;
using PhysicalAi.Racing;
using PhysicalAi.Safety;
using PhysicalAi.VehicleDynamics;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PhysicalAi.Tests.EditMode
{
    public class PhysicalAiTrainingSceneTests
    {
        const string ScenePath = "Assets/PhysicalAi/Scenes/PhysicalAiTraining.unity";

        [Test]
        public void TrainingScene_WiresVehicleCameraOrchestratorRandomizationAndRos2Input()
        {
            EditorSceneManager.OpenScene(ScenePath);

            var vehicle = Object.FindFirstObjectByType<CombinedSlipVehicle>();
            Assert.That(vehicle, Is.Not.Null);
            Assert.That(vehicle.transform.lossyScale, Is.EqualTo(Vector3.one));
            Assert.That(vehicle.GetComponent<Collider>(), Is.Null);
            Assert.That(vehicle.GetComponent<Rigidbody>(), Is.Not.Null);
            Assert.That(vehicle.GetComponent<PlanningLowLevelController>(), Is.Not.Null);
            Assert.That(vehicle.GetComponent<BehaviorCloningPolicy>(), Is.Not.Null);
            Assert.That(vehicle.GetComponent<PhysicalAiEpisodeOrchestrator>(), Is.Not.Null);
            Assert.That(vehicle.GetComponent<HoldoutEvaluationRunner>(), Is.Not.Null);
            Assert.That(vehicle.GetComponent<HeadToHeadScenario>(), Is.Not.Null);
            Assert.That(vehicle.GetComponent<BlindCornerHazardSpawner>(), Is.Not.Null);
            Assert.That(vehicle.GetComponent<TelemetryCalibrationApplier>(), Is.Not.Null);
            Assert.That(Object.FindFirstObjectByType<OpponentCourseFollower>(), Is.Not.Null);
            Assert.That(vehicle.GetComponent<DomainRandomizer>(), Is.Not.Null);
            var sensorRig = vehicle.GetComponent<SensorRig>();
            Assert.That(sensorRig, Is.Not.Null);
            Assert.That(sensorRig.PrimaryCamera, Is.Not.Null);
            Assert.That(sensorRig.FindMount(SensorModality.Camera), Is.Not.Null);
            Assert.That(sensorRig.FindMount(SensorModality.PlanarLidar), Is.Not.Null);
            Assert.That(sensorRig.FindMount(SensorModality.TransformFrame), Is.Not.Null);
            Assert.That(Object.FindFirstObjectByType<PlanarLidarSensor>(), Is.Not.Null);
            Assert.That(Object.FindFirstObjectByType<PlanarLidarSensor>().transform.position.y, Is.LessThan(0.08f));
            Assert.That(Object.FindFirstObjectByType<TransformFrameSensor>(), Is.Not.Null);
            Assert.That(vehicle.GetComponent("PhysicalAi.Ros2.CombinedSlipVehicleRos2Input"), Is.Not.Null);
            Assert.That(Object.FindFirstObjectByType<Camera>(), Is.Not.Null);
        }
    }
}
