using NUnit.Framework;
using PhysicalAi.Course;
using PhysicalAi.Planning;
using PhysicalAi.Randomization;
using PhysicalAi.Sensors;
using PhysicalAi.Evaluation;
using PhysicalAi.Calibration;
using PhysicalAi.Racing;
using PhysicalAi.Safety;
using PhysicalAi.VehicleDynamics;
using UnityEditor;
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
            var bodyCollider = vehicle.GetComponent<BoxCollider>();
            Assert.That(bodyCollider, Is.Not.Null);
            Assert.That(bodyCollider.size.x, Is.EqualTo(0.19f).Within(0.0001f));
            Assert.That(bodyCollider.size.y, Is.EqualTo(0.11f).Within(0.0001f));
            Assert.That(bodyCollider.size.z, Is.EqualTo(0.385f).Within(0.0001f));
            Assert.That(vehicle.GetComponent<Rigidbody>(), Is.Not.Null);
            Assert.That(vehicle.GetComponent<PlanningLowLevelController>(), Is.Not.Null);
            Assert.That(vehicle.GetComponent<BehaviorCloningPolicy>(), Is.Not.Null);
            var orchestrator = vehicle.GetComponent<PhysicalAiEpisodeOrchestrator>();
            Assert.That(orchestrator, Is.Not.Null);
            Assert.That(vehicle.GetComponent<HoldoutEvaluationRunner>(), Is.Not.Null);
            Assert.That(vehicle.GetComponent<VehicleDynamicsTelemetryRecorder>(), Is.Not.Null);
            Assert.That(vehicle.GetComponent<HeadToHeadScenario>(), Is.Not.Null);
            Assert.That(vehicle.GetComponent<BlindCornerHazardSpawner>(), Is.Not.Null);
            Assert.That(vehicle.GetComponent<TelemetryCalibrationApplier>(), Is.Not.Null);
            Assert.That(Object.FindFirstObjectByType<OpponentCourseFollower>(), Is.Not.Null);
            Assert.That(vehicle.GetComponent<DomainRandomizer>(), Is.Not.Null);
            var sensorRig = vehicle.GetComponent<SensorRig>();
            Assert.That(sensorRig, Is.Not.Null);
            Assert.That(sensorRig.PrimaryCamera, Is.Not.Null);
            Assert.That(sensorRig.PrimaryCamera.GetComponent<CameraObservationSensor>(), Is.Not.Null);
            Assert.That(sensorRig.FindMount(SensorModality.Camera), Is.Not.Null);
            Assert.That(sensorRig.FindMount(SensorModality.PlanarLidar), Is.Not.Null);
            Assert.That(sensorRig.FindMount(SensorModality.TransformFrame), Is.Not.Null);
            Assert.That(Object.FindFirstObjectByType<PlanarLidarSensor>(), Is.Not.Null);
            Assert.That(Object.FindFirstObjectByType<PlanarLidarSensor>().transform.position.y, Is.LessThan(0.08f));
            Assert.That(Object.FindFirstObjectByType<TransformFrameSensor>(), Is.Not.Null);
            var tfSensor = Object.FindFirstObjectByType<TransformFrameSensor>();
            var tfSerialized = new SerializedObject(tfSensor);
            Assert.That(tfSerialized.FindProperty("_parentFrame").stringValue, Is.EqualTo("odom"));
            Assert.That(tfSerialized.FindProperty("_poseSource").objectReferenceValue, Is.EqualTo(vehicle.transform));
            Assert.That(tfSensor.GetComponent("PhysicalAi.Ros2.TransformFrameRos2Publisher"), Is.Not.Null);
            Assert.That(vehicle.GetComponent("PhysicalAi.Ros2.CombinedSlipVehicleRos2Input"), Is.Not.Null);
            Assert.That(new SerializedObject(orchestrator).FindProperty("_observationWaypointStride").intValue, Is.EqualTo(5));
            Assert.That(Object.FindFirstObjectByType<Camera>(), Is.Not.Null);
        }

        [Test]
        public void TrainingScene_KeepsADisplayCameraSeparateFromTheObservationCamera()
        {
            EditorSceneManager.OpenScene(ScenePath);

            // The observation camera is bound to a render texture, and a camera with a
            // target texture never draws to the display. An interactive run therefore
            // needs a second camera that carries no observation sensor.
            var cameras = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
            var displayCameras = System.Array.FindAll(cameras, c => c.GetComponent<CameraObservationSensor>() == null);

            Assert.That(displayCameras, Is.Not.Empty);
            Assert.That(System.Array.Exists(cameras, c => c.GetComponent<CameraObservationSensor>() != null), Is.True);
            foreach (var camera in cameras)
            {
                // The test assembly deliberately has no HDRP reference.  The
                // generated scene still has to carry HDRP camera data, so inspect
                // its public clear-mode property without coupling the test DAG.
                System.Type hdrpCameraType = null;
                foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    hdrpCameraType = assembly.GetType("UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData");
                    if (hdrpCameraType != null) break;
                }
                Assert.That(hdrpCameraType, Is.Not.Null);
                var hdrpCamera = camera.GetComponent(hdrpCameraType);
                Assert.That(hdrpCamera, Is.Not.Null);
                var mode = hdrpCamera.GetType().GetField("clearColorMode").GetValue(hdrpCamera).ToString();
                Assert.That(mode, Is.EqualTo("Color"));
            }
        }

        [Test]
        public void TrainingScene_WheelRaycastsUseOnlyTheDrivableSurfaceLayer()
        {
            EditorSceneManager.OpenScene(ScenePath);

            var drivableLayer = LayerMask.NameToLayer(ProceduralCourseTrackBuilder.DrivableSurfaceLayerName);
            Assert.That(drivableLayer, Is.GreaterThanOrEqualTo(0));
            var ground = GameObject.Find("Ground");
            Assert.That(ground, Is.Not.Null);
            Assert.That(ground.layer, Is.EqualTo(drivableLayer));

            var vehicle = Object.FindFirstObjectByType<CombinedSlipVehicle>();
            var groundMask = new SerializedObject(vehicle).FindProperty("_groundMask").intValue;
            Assert.That(groundMask, Is.EqualTo(1 << drivableLayer));
        }

        [Test]
        public void TrainingScene_PreloadsMultipleStreamingChunksForForwardRendering()
        {
            EditorSceneManager.OpenScene(ScenePath);

            var orchestrator = Object.FindFirstObjectByType<PhysicalAiEpisodeOrchestrator>();
            var chunks = new SerializedObject(orchestrator).FindProperty("_initialStreamingChunkCount").intValue;
            Assert.That(chunks, Is.EqualTo(3));
        }

        [Test]
        public void TrainingScene_EnablesManualPlanningForInteractiveRuns()
        {
            EditorSceneManager.OpenScene(ScenePath);

            var teleoperation = Object.FindFirstObjectByType<KeyboardTeleoperationPlanner>();
            Assert.That(teleoperation, Is.Not.Null);
            var serialized = new SerializedObject(teleoperation);
            Assert.That(serialized.FindProperty("_activeOnStart").boolValue, Is.True);
            Assert.That(serialized.FindProperty("_vehicle").objectReferenceValue, Is.Not.Null);
        }

        [Test]
        public void TrainingScene_LogsStreamingTransitionContinuityForManualDrives()
        {
            EditorSceneManager.OpenScene(ScenePath);

            var logger = Object.FindFirstObjectByType<StreamingCourseIntegrityLogger>();
            Assert.That(logger, Is.Not.Null);
            var serialized = new SerializedObject(logger);
            Assert.That(serialized.FindProperty("_orchestrator").objectReferenceValue, Is.Not.Null);
        }

        [Test]
        public void TrainingScene_ShowsThePerceptionCameraInAnInset()
        {
            EditorSceneManager.OpenScene(ScenePath);

            var inset = GameObject.Find("SensorCameraInset");
            Assert.That(inset, Is.Not.Null);
            Assert.That(inset.GetComponent<SensorCameraInset>(), Is.Not.Null);
            var image = inset.transform.Find("CameraImage");
            Assert.That(image, Is.Not.Null);
            Assert.That(image.GetComponent("RawImage"), Is.Not.Null);
        }

        [Test]
        public void ManualPlanner_AppliesStatelessDirectActuation()
        {
            var root = new GameObject("ManualInputTest");
            root.AddComponent<Rigidbody>();
            var vehicle = root.AddComponent<CombinedSlipVehicle>();
            var planner = root.AddComponent<KeyboardTeleoperationPlanner>();
            var serialized = new SerializedObject(planner);
            serialized.FindProperty("_vehicle").objectReferenceValue = vehicle;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            planner.ApplyDirectInput(1f, -1f);
            Assert.That(vehicle.Throttle, Is.EqualTo(1f));
            Assert.That(vehicle.Brake, Is.Zero);
            Assert.That(vehicle.Steering, Is.EqualTo(-1f));
            planner.ApplyDirectInput(0f, 0f);
            Assert.That(vehicle.Throttle, Is.Zero);
            Assert.That(vehicle.Brake, Is.Zero);
            Assert.That(vehicle.Steering, Is.Zero);
            Object.DestroyImmediate(root);
        }

        [Test]
        public void ManualPlanner_YieldsActuationToAutomatedEvaluationAndRlRuns()
        {
            Assert.That(KeyboardTeleoperationPlanner.ShouldActivateAtStart(true, false, System.Array.Empty<string>()), Is.True);
            Assert.That(KeyboardTeleoperationPlanner.ShouldActivateAtStart(true, true, System.Array.Empty<string>()), Is.False);
            Assert.That(KeyboardTeleoperationPlanner.ShouldActivateAtStart(true, false,
                new[] { "-physicalAiEvaluation", "holdout" }), Is.False);
            Assert.That(KeyboardTeleoperationPlanner.ShouldActivateAtStart(true, false,
                new[] { "-physicalAiRlPort", "5015" }), Is.False);
            Assert.That(KeyboardTeleoperationPlanner.ShouldActivateAtStart(true, false,
                new[] { "-physicalAiReferenceDriver" }), Is.False);
        }

        [Test]
        public void LowLevelController_AppliesAnticipatoryHeadingGain()
        {
            var root = new GameObject("LowLevelHeadingGainTest");
            root.AddComponent<Rigidbody>();
            var vehicle = root.AddComponent<CombinedSlipVehicle>();
            var controller = root.AddComponent<PlanningLowLevelController>();
            var serialized = new SerializedObject(controller);
            serialized.FindProperty("_vehicle").objectReferenceValue = vehicle;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            controller.Apply(new PlanningCommand(), currentSpeed: 0f, headingErrorRadians: 0.2f);

            Assert.That(vehicle.Steering, Is.EqualTo(0.5f).Within(0.0001f));
            Object.DestroyImmediate(root);
        }
    }
}
