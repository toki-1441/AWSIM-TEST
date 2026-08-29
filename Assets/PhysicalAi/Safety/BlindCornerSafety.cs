// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using UnityEngine;

namespace PhysicalAi.Safety
{
    /// <summary>Converts camera-visible distance into a stopping-speed bound.</summary>
    public static class BlindCornerSafety
    {
        public static float MaximumSafeSpeed(float visibleDistance, float reactionTime, float maximumDeceleration)
        {
            var distance = Mathf.Max(0f, visibleDistance);
            var reaction = Mathf.Max(0f, reactionTime);
            var deceleration = Mathf.Max(0.001f, maximumDeceleration);
            // d = v * reactionTime + v^2 / (2a)
            return Mathf.Max(0f, -deceleration * reaction + Mathf.Sqrt(deceleration * deceleration * reaction * reaction + 2f * deceleration * distance));
        }

        public static bool IsVisible(Camera camera, Vector3 point, LayerMask occluderMask)
        {
            if (camera == null)
                return false;

            var viewport = camera.WorldToViewportPoint(point);
            if (viewport.z <= 0f || viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f)
                return false;

            var origin = camera.transform.position;
            var direction = point - origin;
            return !Physics.Raycast(origin, direction.normalized, out var hit, direction.magnitude, occluderMask, QueryTriggerInteraction.Ignore)
                   || hit.distance >= direction.magnitude - 0.01f;
        }
    }
}
