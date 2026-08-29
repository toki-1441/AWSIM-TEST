// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using PhysicalAi.Planning;
using PhysicalAi.Ros2;
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
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(200f, 0.2f, 200f);
            ground.transform.position = new Vector3(0f, -0.1f, 0f);

            var vehicle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            vehicle.name = "PhysicalAiVehicle";
            vehicle.transform.position = new Vector3(0f, 0.6f, 0f);
            vehicle.transform.localScale = new Vector3(1.2f, 0.4f, 2f);
            var rigidbody = vehicle.AddComponent<Rigidbody>();
            rigidbody.mass = 30f;
            var dynamics = vehicle.AddComponent<CombinedSlipVehicle>();
            var controller = vehicle.AddComponent<PlanningLowLevelController>();
            var ros2Input = vehicle.AddComponent<CombinedSlipVehicleRos2Input>();

            var mounts = new Transform[4];
            for (var i = 0; i < mounts.Length; i++)
            {
                var mount = new GameObject($"WheelMount{i + 1}");
                mount.transform.SetParent(vehicle.transform, false);
                mount.transform.localPosition = new Vector3(i % 2 == 0 ? -0.48f : 0.48f, -0.2f, i < 2 ? 0.65f : -0.65f);
                mounts[i] = mount.transform;
            }
            ConfigureWheels(dynamics, mounts);
            SetReference(controller, "_vehicle", dynamics);
            SetReference(ros2Input, "_vehicle", dynamics);

            var camera = new GameObject("EgoCamera").AddComponent<Camera>();
            camera.transform.SetParent(vehicle.transform, false);
            camera.transform.localPosition = new Vector3(0f, 0.25f, 0.6f);
            camera.transform.localRotation = Quaternion.identity;

            var orchestrator = vehicle.AddComponent<PhysicalAiEpisodeOrchestrator>();
            SetReference(orchestrator, "_vehicle", dynamics);
            SetReference(orchestrator, "_lowLevelController", controller);
            SetReference(orchestrator, "_egoCamera", camera);

            System.IO.Directory.CreateDirectory("Assets/PhysicalAi/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
        }

        static void ConfigureWheels(CombinedSlipVehicle vehicle, Transform[] mounts)
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
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetReference(Object target, string propertyName, Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(propertyName).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
