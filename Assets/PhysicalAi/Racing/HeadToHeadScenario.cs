// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using PhysicalAi.Course;
using UnityEngine;

namespace PhysicalAi.Racing
{
    /// <summary>Provides the closest same-course opponent in ego-local coordinates to the planning layer.</summary>
    public sealed class HeadToHeadScenario : MonoBehaviour
    {
        const float PassHysteresisMeters = 0.25f;
        [SerializeField] Rigidbody _egoBody;
        [SerializeField] OpponentCourseFollower[] _opponents;

        float[] _previousRelativeProgress;
        float _courseLength;
        float _previousEgoProgress;
        float _unwrappedEgoProgress;
        bool _hasEgoProgress;
        bool _interactionEnabled;
        public bool InteractionEnabled => _interactionEnabled;
        public int EgoOvertakeCount { get; private set; }
        public int OpponentOvertakeCount { get; private set; }

        /// <summary>
        /// Enables the separately-scored opponent scenario.  Solo train/holdout
        /// evaluation must not silently become a collision test against a
        /// kinematic opponent; callers opt in to head-to-head explicitly.
        /// </summary>
        public void SetInteractionEnabled(bool enabled)
        {
            _interactionEnabled = enabled;
            if (_opponents == null)
                return;
            foreach (var opponent in _opponents)
                if (opponent != null)
                    opponent.gameObject.SetActive(enabled);
        }

        /// <summary>
        /// Configures a reproducible interaction probe before <see cref="SetCourse"/>.
        /// Keeping this opt-in prevents solo generalisation runs from inheriting a
        /// racing-specific opponent setup.
        /// </summary>
        public void ConfigureOpponents(float? targetSpeed = null, float? initialProgressMeters = null, float? lateralOffset = null,
            float? lateralOscillationAmplitude = null, float? lateralOscillationFrequencyHz = null)
        {
            if (_opponents == null)
                return;
            foreach (var opponent in _opponents)
                opponent?.Configure(targetSpeed, initialProgressMeters, lateralOffset,
                    lateralOscillationAmplitude, lateralOscillationFrequencyHz);
        }

        public void SetCourse(GeneratedCourse course)
        {
            EgoOvertakeCount = 0;
            OpponentOvertakeCount = 0;
            _courseLength = CourseLength(course);
            _previousEgoProgress = 0f;
            _unwrappedEgoProgress = 0f;
            _hasEgoProgress = false;
            _previousRelativeProgress = _opponents == null ? null : new float[_opponents.Length];
            if (_previousRelativeProgress != null)
                for (var index = 0; index < _previousRelativeProgress.Length; index++)
                    _previousRelativeProgress[index] = float.NaN;
            if (!_interactionEnabled || _opponents == null)
                return;
            foreach (var opponent in _opponents)
                opponent?.SetCourse(course);
        }

        /// <summary>
        /// Supplies appended geometry without restarting the interaction. A
        /// streaming extension must not teleport opponents or erase pass events.
        /// </summary>
        public void ExtendCourse(GeneratedCourse course)
        {
            _courseLength = CourseLength(course);
            if (!_interactionEnabled || _opponents == null)
                return;
            foreach (var opponent in _opponents)
                opponent?.ExtendCourse(course);
        }

        /// <summary>Records pass events from arc-length order, including curved and closed courses.</summary>
        public void UpdateEgoProgress(float egoProgressMeters)
        {
            if (!_interactionEnabled || _opponents == null || _previousRelativeProgress == null)
                return;
            for (var index = 0; index < _opponents.Length; index++)
            {
                var opponent = _opponents[index];
                if (opponent == null)
                    continue;
                // Opponents retain an unwrapped distance, while CourseProgressMonitor
                // reports an index distance that returns to zero at a closed-course
                // start line. Convert ego progress to the same monotonic domain
                // before comparing order; folding their difference would turn a
                // lap boundary into a fictitious pass.
                var relative = UpdateUnwrappedEgoProgress(egoProgressMeters) - opponent.ProgressMeters;
                var previous = _previousRelativeProgress[index];
                if (!float.IsNaN(previous))
                {
                    if (previous < -PassHysteresisMeters && relative > PassHysteresisMeters)
                        EgoOvertakeCount++;
                    else if (previous > PassHysteresisMeters && relative < -PassHysteresisMeters)
                        OpponentOvertakeCount++;
                }
                _previousRelativeProgress[index] = relative;
            }
        }

        /// <summary>Moves kinematic opponents with an origin shift while retaining their course progress.</summary>
        public void Translate(Vector3 offset)
        {
            if (!_interactionEnabled || _opponents == null)
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
            if (!_interactionEnabled || _opponents == null) return;
            foreach (var opponent in _opponents) opponent?.RebaseCourse(removedWaypointCount);
        }

        public OpponentKinematics ObserveClosestOpponent()
        {
            if (!_interactionEnabled || _egoBody == null || _opponents == null || _opponents.Length == 0)
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

        float UpdateUnwrappedEgoProgress(float reportedProgress)
        {
            if (!_hasEgoProgress)
            {
                _previousEgoProgress = reportedProgress;
                _unwrappedEgoProgress = reportedProgress;
                _hasEgoProgress = true;
                return _unwrappedEgoProgress;
            }

            var delta = reportedProgress - _previousEgoProgress;
            if (_courseLength > 0f)
            {
                var halfLength = _courseLength * 0.5f;
                if (delta < -halfLength) delta += _courseLength;
                else if (delta > halfLength) delta -= _courseLength;
            }
            _previousEgoProgress = reportedProgress;
            _unwrappedEgoProgress += delta;
            return _unwrappedEgoProgress;
        }

        static float CourseLength(GeneratedCourse course)
        {
            if (course == null || course.Centerline == null || course.Centerline.Count < 2)
                return 0f;
            var length = 0f;
            for (var index = 1; index < course.Centerline.Count; index++)
                length += Vector3.Distance(course.Centerline[index - 1], course.Centerline[index]);
            return length;
        }

    }
}
