// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using PhysicalAi.Planning;
using PhysicalAi.Calibration;
using PhysicalAi.Ros2;
using PhysicalAi.Randomization;
using PhysicalAi.Sensors;
using PhysicalAi.Course;
using PhysicalAi.Evaluation;
using PhysicalAi.Racing;
using PhysicalAi.Safety;
using PhysicalAi.VehicleDynamics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PhysicalAi.Editor
{
    public static class PhysicalAiTrainingSceneCreator
    {
        const string ScenePath = "Assets/PhysicalAi/Scenes/PhysicalAiTraining.unity";

        [MenuItem("Physical AI/Create Training Scene")]
        public static void CreateOrReplace()
        {
            var specification = VehicleSpecification.Default;
            specification.Validate();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(200f, 0.2f, 200f);
            ground.transform.position = new Vector3(0f, -0.1f, 0f);

            var vehicle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            vehicle.name = "PhysicalAiVehicle";
            // The transform origin sits at the centre of gravity in plan view, so
            // wheel mounts can be placed directly at the specified axle distances.
            vehicle.transform.position = new Vector3(0f, specification.BodyCenterHeight, 0f);
            vehicle.transform.localScale = new Vector3(specification.BodyWidth, specification.BodyHeight, specification.BodyLength);
            var rigidbody = vehicle.AddComponent<Rigidbody>();
            rigidbody.mass = specification.Mass;
            rigidbody.centerOfMass = new Vector3(0f, specification.CenterOfGravityHeight - specification.BodyCenterHeight, 0f);
            rigidbody.inertiaTensor = specification.InertiaTensor;
            rigidbody.inertiaTensorRotation = Quaternion.identity;
            var dynamics = vehicle.AddComponent<CombinedSlipVehicle>();
            var controller = vehicle.AddComponent<PlanningLowLevelController>();
            var behaviorCloningPolicy = vehicle.AddComponent<BehaviorCloningPolicy>();
            var ros2Input = vehicle.AddComponent<CombinedSlipVehicleRos2Input>();
            var randomizer = vehicle.AddComponent<DomainRandomizer>();
            var telemetryCalibration = vehicle.AddComponent<TelemetryCalibrationApplier>();
            var sensorRig = vehicle.AddComponent<SensorRig>();
            var headToHead = vehicle.AddComponent<HeadToHeadScenario>();
            var hazardSpawner = vehicle.AddComponent<BlindCornerHazardSpawner>();

            var mounts = new Transform[4];
            for (var i = 0; i < mounts.Length; i++)
            {
                var mount = new GameObject($"WheelMount{i + 1}");
                mount.transform.SetParent(vehicle.transform, false);
                var halfTrack = specification.TrackWidth * 0.5f;
                mount.transform.localPosition = new Vector3(
                    i % 2 == 0 ? -halfTrack : halfTrack,
                    -specification.BodyHeight * 0.5f,
                    i < 2 ? specification.DistanceToFrontAxle : -specification.DistanceToRearAxle);
                mounts[i] = mount.transform;
            }
            ConfigureVehicle(dynamics, specification);
            ConfigureWheels(dynamics, mounts, specification);
            SetReference(controller, "_vehicle", dynamics);
            SetReference(ros2Input, "_vehicle", dynamics);

            // Both sensors sit on the top plate, the camera ahead of the LiDAR.
            var camera = CreateSensorMount<Camera>(vehicle.transform, "EgoCamera", SensorModality.Camera, "camera_front",
                new Vector3(0f, specification.BodyHeight * 0.7f, specification.BodyLength * 0.3f));
            var lidar = CreateSensorMount<PlanarLidarSensor>(vehicle.transform, "EgoLidar2D", SensorModality.PlanarLidar, "lidar_2d",
                new Vector3(0f, specification.BodyHeight * 0.9f, specification.BodyLength * 0.12f));
            var tf = CreateSensorMount<TransformFrameSensor>(vehicle.transform, "TfBaseLink", SensorModality.TransformFrame, "base_link", Vector3.zero);
            SetReference(tf, "_mount", tf.GetComponent<SensorMount>());
            ConfigureSensorRig(sensorRig, camera, camera.GetComponent<SensorMount>(), lidar.GetComponent<SensorMount>(), tf.GetComponent<SensorMount>());
            ConfigureSensorMounts(randomizer, dynamics, camera.transform, lidar.transform, tf.transform);

            var orchestrator = vehicle.AddComponent<PhysicalAiEpisodeOrchestrator>();
            var holdoutEvaluator = vehicle.AddComponent<HoldoutEvaluationRunner>();
            var track = new GameObject("ProceduralTrack").AddComponent<ProceduralCourseTrackBuilder>();
            var progress = vehicle.AddComponent<CourseProgressMonitor>();
            SetReference(orchestrator, "_vehicle", dynamics);
            SetReference(orchestrator, "_lowLevelController", controller);
            SetReference(orchestrator, "_behaviorCloningPolicy", behaviorCloningPolicy);
            SetReference(orchestrator, "_hazardSpawner", hazardSpawner);
            SetReference(orchestrator, "_telemetryCalibrationApplier", telemetryCalibration);
            SetReference(orchestrator, "_egoCamera", camera);
            SetReference(orchestrator, "_domainRandomizer", randomizer);
            SetReference(orchestrator, "_trackBuilder", track);
            SetReference(orchestrator, "_progressMonitor", progress);
            SetReference(orchestrator, "_headToHeadScenario", headToHead);
            SetReference(holdoutEvaluator, "_orchestrator", orchestrator);
            ConfigureHeadToHead(headToHead, rigidbody, CreateOpponentVehicle(specification));
            SetReference(hazardSpawner, "_egoCamera", camera);
            SetReference(telemetryCalibration, "_vehicle", dynamics);
            SetReference(telemetryCalibration, "_domainRandomizer", randomizer);

            System.IO.Directory.CreateDirectory("Assets/PhysicalAi/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
        }

        static void ConfigureVehicle(CombinedSlipVehicle vehicle, VehicleSpecification specification)
        {
            var serialized = new SerializedObject(vehicle);
            serialized.FindProperty("_surfaceFriction").floatValue = specification.SurfaceFriction;
            serialized.FindProperty("_maximumDriveTorque").floatValue = specification.MaximumDriveTorquePerWheel;
            serialized.FindProperty("_maximumBrakeTorque").floatValue = specification.MaximumBrakeTorquePerWheel;
            serialized.FindProperty("_maximumSteeringAngle").floatValue = specification.MaximumSteeringAngleDegrees;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void ConfigureWheels(CombinedSlipVehicle vehicle, Transform[] mounts, VehicleSpecification specification)
        {
            var serialized = new SerializedObject(vehicle);
            var wheels = serialized.FindProperty("_wheels");
            wheels.arraySize = mounts.Length;
            for (var i = 0; i < mounts.Length; i++)
            {
                var wheel = wheels.GetArrayElementAtIndex(i);
                wheel.FindPropertyRelative("_suspensionMount").objectReferenceValue = mounts[i];
                wheel.FindPropertyRelative("_steerable").boolValue = i < 2;
                wheel.FindPropertyRelative("_driven").boolValue = true;
                wheel.FindPropertyRelative("_radius").floatValue = specification.TireRadius;
                wheel.FindPropertyRelative("_suspensionRestLength").floatValue = specification.SuspensionRestLength;
                wheel.FindPropertyRelative("_springRate").floatValue = specification.SpringRate;
                wheel.FindPropertyRelative("_damperRate").floatValue = specification.DamperRate;
                wheel.FindPropertyRelative("_rotationalInertia").floatValue = specification.WheelRotationalInertia;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetReference(Object target, string propertyName, Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(propertyName).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static T CreateSensorMount<T>(Transform parent, string name, SensorModality modality, string frameId, Vector3 localPosition) where T : Component
        {
            var sensor = new GameObject(name);
            sensor.transform.SetParent(parent, false);
            var mount = sensor.AddComponent<SensorMount>();
            mount.Configure(modality, frameId, localPosition, Quaternion.identity);
            return sensor.AddComponent<T>();
        }

        static void ConfigureSensorRig(SensorRig rig, Camera camera, params SensorMount[] mounts)
        {
            var serialized = new SerializedObject(rig);
            serialized.FindProperty("_primaryCamera").objectReferenceValue = camera;
            var array = serialized.FindProperty("_mounts");
            array.arraySize = mounts.Length;
            for (var i = 0; i < mounts.Length; i++)
                array.GetArrayElementAtIndex(i).objectReferenceValue = mounts[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void ConfigureSensorMounts(DomainRandomizer randomizer, CombinedSlipVehicle vehicle, params Transform[] sensorMounts)
        {
            SetReference(randomizer, "_vehicle", vehicle);
            var serialized = new SerializedObject(randomizer);
            var mounts = serialized.FindProperty("_sensorMounts");
            mounts.arraySize = sensorMounts.Length;
            for (var i = 0; i < sensorMounts.Length; i++)
                mounts.GetArrayElementAtIndex(i).objectReferenceValue = sensorMounts[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static OpponentCourseFollower CreateOpponentVehicle(VehicleSpecification specification)
        {
            var opponent = GameObject.CreatePrimitive(PrimitiveType.Cube);
            opponent.name = "RaceOpponent";
            opponent.transform.position = new Vector3(0f, specification.BodyCenterHeight, specification.BodyLength * 3f);
            opponent.transform.localScale = new Vector3(specification.BodyWidth, specification.BodyHeight, specification.BodyLength);
            opponent.AddComponent<Rigidbody>();
            return opponent.AddComponent<OpponentCourseFollower>();
        }

        static void ConfigureHeadToHead(HeadToHeadScenario scenario, Rigidbody egoBody, params OpponentCourseFollower[] opponents)
        {
            var serialized = new SerializedObject(scenario);
            serialized.FindProperty("_egoBody").objectReferenceValue = egoBody;
            var array = serialized.FindProperty("_opponents");
            array.arraySize = opponents.Length;
            for (var i = 0; i < opponents.Length; i++)
                array.GetArrayElementAtIndex(i).objectReferenceValue = opponents[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
