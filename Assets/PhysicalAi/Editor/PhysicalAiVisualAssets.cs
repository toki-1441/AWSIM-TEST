// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using System.IO;
using PhysicalAi.VehicleDynamics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace PhysicalAi.Editor
{
    /// <summary>
    /// HDRP materials and vehicle meshes for the generated training scene.
    /// The scene is generated from code, so its appearance has to be reproducible
    /// from code too. Materials are written as assets instead of being built at
    /// runtime, so a player build keeps their shaders rather than stripping them.
    /// </summary>
    public static class PhysicalAiVisualAssets
    {
        public const string MaterialDirectory = "Assets/PhysicalAi/Materials";
        const string LitShaderName = "HDRP/Lit";
        const string UnlitShaderName = "HDRP/Unlit";

        /// <summary>
        /// Visual-only donor mesh reused from AWSIM. A 1/10 touring-car shell is a
        /// scale replica of a road car, so a downscaled road car reads correctly.
        /// The geometry is CC BY-NC and must not ship in a commercial build; the
        /// physics never reads it.
        /// </summary>
        public const string VehicleModelPath = "Assets/Awsim/Models/Entity/Vehicle/NpcVehicle/HatchbackCar/Hatchback.prefab";

        /// <summary>Donor model's front axle offset and wheel radius, in its own unscaled metres.</summary>
        const float DonorFrontAxleZ = 1.1571f;
        const float DonorWheelbase = 2.53318f;
        const float DonorWheelRadius = 0.31659f;

        // A MAGP course is conference-room carpet bounded by red-and-white duct.
        static readonly Color CarpetColor = new Color(0.15f, 0.16f, 0.17f);
        static readonly Color RoadColor = new Color(0.21f, 0.22f, 0.23f);
        static readonly Color WallPrimaryColor = new Color(0.86f, 0.87f, 0.88f);
        static readonly Color WallAlternateColor = new Color(0.60f, 0.08f, 0.10f);
        static readonly Color CourseLineColor = new Color(0.95f, 0.84f, 0.15f);
        static readonly Color EgoBodyColor = new Color(0.10f, 0.34f, 0.72f);
        static readonly Color OpponentBodyColor = new Color(0.82f, 0.32f, 0.05f);
        static readonly Color GlassColor = new Color(0.06f, 0.07f, 0.09f);
        static readonly Color TireColor = new Color(0.045f, 0.045f, 0.05f);

        public static Material Carpet() => Lit("PhysicalAiCarpet", CarpetColor, 0.04f);
        public static Material Road() => Lit("PhysicalAiRoad", RoadColor, 0.06f);
        public static Material WallPrimary() => Lit("PhysicalAiWallWhite", WallPrimaryColor, 0.35f);
        public static Material WallAlternate() => Lit("PhysicalAiWallRed", WallAlternateColor, 0.35f);
        public static Material EgoBody() => Lit("PhysicalAiEgoBody", EgoBodyColor, 0.75f, 0.1f);
        public static Material OpponentBody() => Lit("PhysicalAiOpponentBody", OpponentBodyColor, 0.75f, 0.1f);
        public static Material Glass() => Lit("PhysicalAiGlass", GlassColor, 0.9f);
        public static Material Tire() => Lit("PhysicalAiTire", TireColor, 0.15f);

        /// <summary>
        /// The regulation floor line is unlit, so it keeps a constant appearance
        /// regardless of how the episode's lighting is randomised.
        /// </summary>
        public static Material CourseLine() => Unlit("PhysicalAiCourseLine", CourseLineColor);

        /// <summary>
        /// Instantiates the donor mesh under <paramref name="parent"/>, scaled and
        /// aligned to the specification. Returns null when the donor asset is
        /// absent so the caller can fall back to a primitive.
        /// </summary>
        public static GameObject CreateVehicleVisual(
            Transform parent,
            VehicleSpecification specification,
            Material bodyMaterial,
            Transform[] wheelMounts)
        {
            var donor = AssetDatabase.LoadAssetAtPath<GameObject>(VehicleModelPath);
            if (donor == null)
                return null;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(donor, parent);
            if (instance == null)
                return null;
            // Unpacking keeps the mesh and texture references pointing at the donor
            // asset while letting the hierarchy be restructured for our wheel mounts.
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.name = "VehicleBodyVisual";

            var bodyScale = specification.Wheelbase / DonorWheelbase;
            var wheelScale = specification.TireRadius / DonorWheelRadius;
            instance.transform.localScale = Vector3.one * bodyScale;
            instance.transform.localRotation = Quaternion.identity;
            // The donor's origin is on the ground at its wheelbase reference; lift it
            // to the vehicle origin and slide it so its front axle meets the specified one.
            instance.transform.localPosition = new Vector3(
                0f,
                -specification.BodyCenterHeight,
                specification.DistanceToFrontAxle - DonorFrontAxleZ * bodyScale);

            var tire = Tire();
            var glass = Glass();
            MoveWheelsToMounts(instance.transform, wheelMounts, specification, wheelScale, tire);
            StripNonVisualChildren(instance.transform);
            PaintRenderers(instance.transform, bodyMaterial, glass, tire);
            return instance;
        }

        /// <summary>Re-parents the donor wheels onto the physical wheel mounts so they sit at the specified axles.</summary>
        static void MoveWheelsToMounts(
            Transform visual,
            Transform[] wheelMounts,
            VehicleSpecification specification,
            float wheelScale,
            Material tire)
        {
            // A mount sits at the suspension attachment on the body underside, while the
            // wheel centre belongs exactly one tire radius above the ground.
            var mountHeight = specification.BodyCenterHeight - specification.BodyHeight * 0.5f;
            var wheelCentreOffset = new Vector3(0f, specification.TireRadius - mountHeight, 0f);
            var donorWheelNames = new[] { "Wheel_Front_L", "Wheel_Front_R", "Wheel_Rear_L", "Wheel_Rear_R" };
            for (var index = 0; index < donorWheelNames.Length; index++)
            {
                var wheel = visual.Find(donorWheelNames[index]);
                if (wheel == null)
                    continue;
                // Without mounts (the opponent) the donor's own wheel placement is kept.
                if (wheelMounts == null || index >= wheelMounts.Length || wheelMounts[index] == null)
                    continue;

                wheel.SetParent(wheelMounts[index], false);
                wheel.localScale = Vector3.one * wheelScale;
                wheel.localRotation = Quaternion.identity;
                wheel.localPosition = wheelCentreOffset;
                ApplyMaterial(wheel, tire);
            }
        }

        static void StripNonVisualChildren(Transform visual)
        {
            // The donor carries a low-polygon collision hull as a renderable child.
            // Physics here is ray-cast, so the hull would only draw a grey box.
            var hull = visual.Find("Collider");
            if (hull != null)
                Object.DestroyImmediate(hull.gameObject);
            foreach (var collider in visual.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(collider);
        }

        static void PaintRenderers(Transform visual, Material body, Material glass, Material tire)
        {
            foreach (var renderer in visual.GetComponentsInChildren<MeshRenderer>(true))
            {
                var chosen = body;
                if (renderer.name.Contains("Window") || renderer.name.Contains("Glass"))
                    chosen = glass;
                else if (renderer.name.StartsWith("Wheel"))
                    chosen = tire;
                SetRendererMaterial(renderer, chosen);
            }
        }

        static void SetRendererMaterial(MeshRenderer renderer, Material material)
        {
            if (renderer == null || material == null)
                return;
            var materials = new Material[Mathf.Max(1, renderer.sharedMaterials.Length)];
            for (var index = 0; index < materials.Length; index++)
                materials[index] = material;
            renderer.sharedMaterials = materials;
        }

        static void ApplyMaterial(Transform target, Material material)
        {
            foreach (var renderer in target.GetComponentsInChildren<MeshRenderer>(true))
                SetRendererMaterial(renderer, material);
        }

        public static void ApplyMaterial(GameObject target, Material material)
        {
            if (target != null)
                ApplyMaterial(target.transform, material);
        }

        static Material Lit(string name, Color color, float smoothness, float metallic = 0f)
        {
            var material = LoadOrCreate(name, LitShaderName);
            if (material == null)
                return null;
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", metallic);
            HDMaterial.ValidateMaterial(material);
            EditorUtility.SetDirty(material);
            return material;
        }

        static Material Unlit(string name, Color color)
        {
            var material = LoadOrCreate(name, UnlitShaderName);
            if (material == null)
                return null;
            material.SetColor("_UnlitColor", color);
            HDMaterial.ValidateMaterial(material);
            EditorUtility.SetDirty(material);
            return material;
        }

        static Material LoadOrCreate(string name, string shaderName)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogWarning($"Physical AI visuals: shader '{shaderName}' is unavailable; leaving the default material in place.");
                return null;
            }

            var path = $"{MaterialDirectory}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Directory.CreateDirectory(MaterialDirectory);
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            return material;
        }
    }
}
