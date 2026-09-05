using System.Reflection;
using NUnit.Framework;
using PhysicalAi.Course;
using PhysicalAi.Planning;
using PhysicalAi.Safety;
using UnityEngine;

namespace PhysicalAi.Tests.EditMode
{
    public class BlindCornerSafetyTests
    {
        [Test]
        public void MaximumSafeSpeed_IncreasesWithVisibleDistance()
        {
            var shortSight = BlindCornerSafety.MaximumSafeSpeed(5f, 0.5f, 6f);
            var longSight = BlindCornerSafety.MaximumSafeSpeed(20f, 0.5f, 6f);

            Assert.That(longSight, Is.GreaterThan(shortSight));
        }

        [Test]
        public void MaximumSafeSpeed_AccountsForReactionAndBrakingDistance()
        {
            const float speed = 8f;
            const float reaction = 0.5f;
            const float deceleration = 6f;
            var distance = speed * reaction + speed * speed / (2f * deceleration);

            Assert.That(BlindCornerSafety.MaximumSafeSpeed(distance, reaction, deceleration), Is.EqualTo(speed).Within(0.001f));
        }

        [Test]
        public void IsVisible_OccluderBeforeTargetBlocksCameraView()
        {
            var camera = new GameObject("camera").AddComponent<Camera>();
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = new Vector3(0f, 0f, 2f);
            Physics.SyncTransforms();

            Assert.That(BlindCornerSafety.IsVisible(camera, new Vector3(0f, 0f, 5f), ~0), Is.False);
            Object.DestroyImmediate(wall);
            Object.DestroyImmediate(camera.gameObject);
        }

        [Test]
        public void OrchestratorVisibility_IgnoresDrivableGroundButSeesAnObstacle()
        {
            var root = new GameObject("visibility-orchestrator");
            var orchestrator = root.AddComponent<PhysicalAiEpisodeOrchestrator>();
            var camera = new GameObject("camera").AddComponent<Camera>();
            camera.transform.position = Vector3.up;
            camera.transform.rotation = Quaternion.Euler(30f, 0f, 0f);
            var serialized = new UnityEditor.SerializedObject(orchestrator);
            serialized.FindProperty("_egoCamera").objectReferenceValue = camera;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.layer = LayerMask.NameToLayer(ProceduralCourseTrackBuilder.DrivableSurfaceLayerName);
            ground.transform.position = new Vector3(0f, -0.1f, 2f);
            ground.transform.localScale = new Vector3(20f, 0.2f, 20f);
            Physics.SyncTransforms();

            Assert.That(MeasureVisibleDistance(orchestrator), Is.EqualTo(6f).Within(0.001f),
                "The downward camera ray must not treat the road as an occluder.");

            var obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obstacle.transform.position = camera.transform.position + camera.transform.forward * 4f;
            Physics.SyncTransforms();
            Assert.That(MeasureVisibleDistance(orchestrator), Is.LessThan(4f));

            Object.DestroyImmediate(obstacle);
            // A hazard can emerge off the optical centre while still being inside
            // the camera FOV. Visibility safety must notice it before it reaches
            // the vehicle's swept corridor.
            var lateralObstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lateralObstacle.transform.position = camera.transform.position + camera.transform.forward * 3f + camera.transform.right * 0.45f;
            Physics.SyncTransforms();
            Assert.That(MeasureVisibleDistance(orchestrator), Is.LessThan(3.5f));

            Object.DestroyImmediate(lateralObstacle);
            Object.DestroyImmediate(ground);
            Object.DestroyImmediate(camera.gameObject);
            Object.DestroyImmediate(root);
        }

        static float MeasureVisibleDistance(PhysicalAiEpisodeOrchestrator orchestrator)
        {
            return (float)typeof(PhysicalAiEpisodeOrchestrator)
                .GetMethod("MeasureVisibleDistance", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(orchestrator, null);
        }
    }
}
