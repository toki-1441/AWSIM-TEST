// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using PhysicalAi.Course;
using UnityEngine;

namespace PhysicalAi.Racing
{
    /// <summary>Provides the closest same-course opponent in ego-local coordinates to the planning layer.</summary>
    public sealed class HeadToHeadScenario : MonoBehaviour
    {
        [SerializeField] Rigidbody _egoBody;
        [SerializeField] OpponentCourseFollower[] _opponents;

        public void SetCourse(GeneratedCourse course)
        {
            if (_opponents == null)
                return;
            foreach (var opponent in _opponents)
                opponent?.SetCourse(course);
        }

        /// <summary>Moves kinematic opponents with an origin shift while retaining their course progress.</summary>
        public void Translate(Vector3 offset)
        {
            if (_opponents == null)
                return;
            foreach (var opponent in _opponents)
            {
                if (opponent == null)
                    continue;
                var body = opponent.GetComponent<Rigidbody>();
                if (body != null)
                    body.position += offset;
                else
                    opponent.transform.position += offset;
            }
        }

        public void RebaseCourse(int removedWaypointCount)
        {
            if (_opponents == null) return;
            foreach (var opponent in _opponents) opponent?.RebaseCourse(removedWaypointCount);
        }

        public OpponentKinematics ObserveClosestOpponent()
        {
            if (_egoBody == null || _opponents == null || _opponents.Length == 0)
                return default;

            var closest = default(OpponentKinematics);
            var bestDistance = float.PositiveInfinity;
            foreach (var opponent in _opponents)
            {
                if (opponent == null)
                    continue;
                var candidate = ToEgoLocal(_egoBody.transform, _egoBody.linearVelocity, opponent.transform.position, opponent.Velocity);
                var distance = candidate.LongitudinalDistance * candidate.LongitudinalDistance + candidate.LateralOffset * candidate.LateralOffset;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    closest = candidate;
                }
            }
            return closest;
        }

        public static OpponentKinematics ToEgoLocal(Transform ego, Vector3 egoVelocity, Vector3 opponentPosition, Vector3 opponentVelocity)
        {
            var localPosition = ego.InverseTransformPoint(opponentPosition);
            var egoRelativeVelocity = ego.InverseTransformDirection(egoVelocity - opponentVelocity);
            var opponentRelativeVelocity = ego.InverseTransformDirection(opponentVelocity - egoVelocity);
            return new OpponentKinematics
            {
                LongitudinalDistance = localPosition.z,
                LateralOffset = localPosition.x,
                RelativeLongitudinalSpeed = egoRelativeVelocity.z,
                RelativeLateralSpeed = opponentRelativeVelocity.x
            };
        }
    }
}
