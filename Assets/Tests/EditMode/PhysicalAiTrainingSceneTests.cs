using NUnit.Framework;
using PhysicalAi.Planning;
using PhysicalAi.Randomization;
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
            Assert.That(vehicle.GetComponent<Rigidbody>(), Is.Not.Null);
            Assert.That(vehicle.GetComponent<PlanningLowLevelController>(), Is.Not.Null);
            Assert.That(vehicle.GetComponent<PhysicalAiEpisodeOrchestrator>(), Is.Not.Null);
            Assert.That(vehicle.GetComponent<DomainRandomizer>(), Is.Not.Null);
            Assert.That(vehicle.GetComponent("PhysicalAi.Ros2.CombinedSlipVehicleRos2Input"), Is.Not.Null);
            Assert.That(Object.FindFirstObjectByType<Camera>(), Is.Not.Null);
        }
    }
}
