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
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace PhysicalAi.Editor
{
    public static class PhysicalAiTrainingSceneCreator
    {
        const string ScenePath = "Assets/PhysicalAi/Scenes/PhysicalAiTraining.unity";
        const string VolumeProfilePath = "Assets/PhysicalAi/Rendering/PhysicalAiSceneVolume.asset";

        [MenuItem("Physical AI/Create Training Scene")]
        public static void CreateOrReplace()
        {
            var specification = VehicleSpecification.Default;
            specification.Validate();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // An empty HDRP scene has no light and no volume, so a camera renders
            // nothing usable. Perception is the point of this scene, so lighting is
            // part of its definition rather than a decoration.
            CreateSceneLighting();
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(200f, 0.2f, 200f);
            ground.transform.position = new Vector3(0f, -0.1f, 0f);
            PhysicalAiVisualAssets.ApplyMaterial(ground, PhysicalAiVisualAssets.Carpet());

            var vehicle = new GameObject("PhysicalAiVehicle");
            vehicle.name = "PhysicalAiVehicle";
            // The transform origin sits at the centre of gravity in plan view, so
            // wheel mounts can be placed directly at the specified axle distances.
            vehicle.transform.position = new Vector3(0f, specification.BodyCenterHeight, 0f);
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
            var calibrationReportWriter = vehicle.AddComponent<TelemetryCalibrationReportWriter>();
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
            // Built after the mounts so the donor wheels can be re-parented onto them.
            CreateVehicleBodyVisual(vehicle.transform, specification, mounts);
            ConfigureVehicle(dynamics, specification);
            ConfigureWheels(dynamics, mounts, specification);
            SetReference(controller, "_vehicle", dynamics);
            SetReference(ros2Input, "_vehicle", dynamics);

            // Camera remains high and forward; the auxiliary planar LiDAR is mounted
            // below the 0.08 m course wall top so it can observe the boundaries.
            var camera = CreateSensorMount<Camera>(vehicle.transform, "EgoCamera", SensorModality.Camera, "camera_front",
                new Vector3(0f, specification.BodyHeight * 0.7f, specification.BodyLength * 0.3f));
            // A 0.385 m vehicle needs a near plane far closer than Unity's 0.3 m default,
            // which would clip away everything within a stopping distance of the car.
            camera.nearClipPlane = 0.02f;
            camera.farClipPlane = 120f;
            camera.gameObject.AddComponent<HDAdditionalCameraData>();
            var cameraObservation = camera.gameObject.AddComponent<CameraObservationSensor>();
            var lidar = CreateSensorMount<PlanarLidarSensor>(vehicle.transform, "EgoLidar2D", SensorModality.PlanarLidar, "lidar_2d",
                new Vector3(0f, 0.06f - specification.BodyCenterHeight, specification.BodyLength * 0.12f));
            var tf = CreateSensorMount<TransformFrameSensor>(vehicle.transform, "TfBaseLink", SensorModality.TransformFrame, "base_link", Vector3.zero);
            SetReference(tf, "_mount", tf.GetComponent<SensorMount>());
            ConfigureSensorRig(sensorRig, camera, camera.GetComponent<SensorMount>(), lidar.GetComponent<SensorMount>(), tf.GetComponent<SensorMount>());
            ConfigureSensorMounts(randomizer, dynamics, camera.transform, lidar.transform, tf.transform);

            var orchestrator = vehicle.AddComponent<PhysicalAiEpisodeOrchestrator>();
            var pythonRlBridge = vehicle.AddComponent<PythonRlBridge>();
            var demonstrationRecorder = vehicle.AddComponent<PlanningDemonstrationRecorder>();
            var teleoperation = vehicle.AddComponent<KeyboardTeleoperationPlanner>();
            var holdoutEvaluator = vehicle.AddComponent<HoldoutEvaluationRunner>();
            var holdoutReportWriter = vehicle.AddComponent<HoldoutEvaluationReportWriter>();
            var track = new GameObject("ProceduralTrack").AddComponent<ProceduralCourseTrackBuilder>();
            ConfigureTrackAppearance(track);
            var progress = vehicle.AddComponent<CourseProgressMonitor>();
            SetReference(orchestrator, "_vehicle", dynamics);
            SetReference(pythonRlBridge, "_orchestrator", orchestrator);
            SetReference(orchestrator, "_lowLevelController", controller);
            SetReference(orchestrator, "_demonstrationRecorder", demonstrationRecorder);
            SetReference(orchestrator, "_behaviorCloningPolicy", behaviorCloningPolicy);
            SetReference(orchestrator, "_hazardSpawner", hazardSpawner);
            SetReference(orchestrator, "_telemetryCalibrationApplier", telemetryCalibration);
            SetReference(orchestrator, "_egoCamera", camera);
            SetReference(orchestrator, "_cameraObservationSensor", cameraObservation);
            SetBoolean(orchestrator, "_useStreamingTraining", true);
            SetReference(orchestrator, "_domainRandomizer", randomizer);
            SetReference(orchestrator, "_trackBuilder", track);
            SetReference(orchestrator, "_progressMonitor", progress);
            SetReference(orchestrator, "_headToHeadScenario", headToHead);
            SetInteger(orchestrator, "_observationWaypointStride", 5);
            SetReference(holdoutEvaluator, "_orchestrator", orchestrator);
            SetReference(teleoperation, "_orchestrator", orchestrator);
            SetReference(teleoperation, "_recorder", demonstrationRecorder);
            SetReference(holdoutReportWriter, "_runner", holdoutEvaluator);
            ConfigureHeadToHead(headToHead, rigidbody, CreateOpponentVehicle(specification));
            SetReference(hazardSpawner, "_egoCamera", camera);
            SetReference(telemetryCalibration, "_vehicle", dynamics);
            SetReference(telemetryCalibration, "_domainRandomizer", randomizer);
            SetReference(calibrationReportWriter, "_applier", telemetryCalibration);

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

        static void CreateVehicleBodyVisual(Transform vehicleRoot, VehicleSpecification specification, Transform[] wheelMounts)
        {
            if (PhysicalAiVisualAssets.CreateVehicleVisual(vehicleRoot, specification, PhysicalAiVisualAssets.EgoBody(), wheelMounts) != null)
                return;

            // The donor mesh is optional; a scene without it still trains, it just
            // gives the camera a featureless box instead of a car.
            Debug.LogWarning($"Physical AI visuals: '{PhysicalAiVisualAssets.VehicleModelPath}' is unavailable; using a primitive body.");
            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "VehicleBodyVisual";
            visual.transform.SetParent(vehicleRoot, false);
            visual.transform.localScale = new Vector3(specification.BodyWidth, specification.BodyHeight, specification.BodyLength);
            Object.DestroyImmediate(visual.GetComponent<Collider>());
            PhysicalAiVisualAssets.ApplyMaterial(visual, PhysicalAiVisualAssets.EgoBody());
        }

        /// <summary>
        /// A directional key light plus a global volume with a gradient sky and a
        /// fixed exposure. Exposure is deliberately fixed: HDRP's default automatic
        /// exposure adapts over frames, which would make the same scene produce
        /// different observations from one policy step to the next.
        /// </summary>
        static void CreateSceneLighting()
        {
            var lightObject = new GameObject("DirectionalLight");
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var light = lightObject.AddComponent<Light>();
            var hdLight = lightObject.AddComponent<HDAdditionalLightData>();
            HDAdditionalLightData.InitDefaultHDAdditionalLightData(hdLight);
            light.type = LightType.Directional;
            light.lightUnit = LightUnit.Lux;
            light.intensity = 15000f;
            light.useColorTemperature = true;
            light.colorTemperature = 5200f;
            light.shadows = LightShadows.Soft;

            var volumeObject = new GameObject("SceneVolume");
            var volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 1f;
            volume.sharedProfile = CreateVolumeProfile();
        }

        static VolumeProfile CreateVolumeProfile()
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(VolumeProfilePath));
            // Recreated rather than edited in place: volume overrides are sub-assets,
            // and rewriting the list would leave the previous ones orphaned in the file.
            AssetDatabase.DeleteAsset(VolumeProfilePath);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, VolumeProfilePath);

            var environment = profile.Add<VisualEnvironment>(true);
            environment.skyType.overrideState = true;
            environment.skyType.value = (int)SkyType.Gradient;
            environment.skyAmbientMode.overrideState = true;
            environment.skyAmbientMode.value = SkyAmbientMode.Dynamic;

            // An indoor venue: bright ceiling, neutral walls, dark carpet bounce.
            var sky = profile.Add<GradientSky>(true);
            sky.top.overrideState = true;
            sky.top.value = new Color(0.62f, 0.64f, 0.68f);
            sky.middle.overrideState = true;
            sky.middle.value = new Color(0.42f, 0.44f, 0.47f);
            sky.bottom.overrideState = true;
            sky.bottom.value = new Color(0.16f, 0.16f, 0.17f);
            sky.gradientDiffusion.overrideState = true;
            sky.gradientDiffusion.value = 1f;

            var exposure = profile.Add<Exposure>(true);
            exposure.mode.overrideState = true;
            exposure.mode.value = ExposureMode.Fixed;
            exposure.fixedExposure.overrideState = true;
            // 9 EV keeps the dark carpet and course walls legible in the ego
            // camera while retaining headroom for the 15 klux directional key.
            exposure.fixedExposure.value = 9f;

            // Volume overrides only persist when they are stored inside the profile asset.
            foreach (var component in profile.components)
            {
                component.hideFlags = HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(component, profile);
            }
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        static void ConfigureTrackAppearance(ProceduralCourseTrackBuilder track)
        {
            var serialized = new SerializedObject(track);
            serialized.FindProperty("_roadMaterial").objectReferenceValue = PhysicalAiVisualAssets.Road();
            serialized.FindProperty("_wallPrimaryMaterial").objectReferenceValue = PhysicalAiVisualAssets.WallPrimary();
            serialized.FindProperty("_wallAlternateMaterial").objectReferenceValue = PhysicalAiVisualAssets.WallAlternate();
            serialized.FindProperty("_courseLineMaterial").objectReferenceValue = PhysicalAiVisualAssets.CourseLine();
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

        static void SetBoolean(Object target, string propertyName, bool value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(propertyName).boolValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetInteger(Object target, string propertyName, int value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(propertyName).intValue = value;
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
            // The root stays unit-scale so the visual mesh is not squashed by the
            // collision box; the box is sized explicitly instead.
            var opponent = new GameObject("RaceOpponent");
            opponent.transform.position = new Vector3(0f, specification.BodyCenterHeight, specification.BodyLength * 3f);
            var collider = opponent.AddComponent<BoxCollider>();
            collider.size = new Vector3(specification.BodyWidth, specification.BodyHeight, specification.BodyLength);

            if (PhysicalAiVisualAssets.CreateVehicleVisual(opponent.transform, specification, PhysicalAiVisualAssets.OpponentBody(), null) == null)
            {
                var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
                visual.name = "OpponentBodyVisual";
                visual.transform.SetParent(opponent.transform, false);
                visual.transform.localScale = new Vector3(specification.BodyWidth, specification.BodyHeight, specification.BodyLength);
                Object.DestroyImmediate(visual.GetComponent<Collider>());
                PhysicalAiVisualAssets.ApplyMaterial(visual, PhysicalAiVisualAssets.OpponentBody());
            }

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
