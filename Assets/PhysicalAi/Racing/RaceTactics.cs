// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using System;
using UnityEngine;

namespace PhysicalAi.Racing
{
    public enum OpponentIntent { Unknown, Ahead, Closing, OvertakingLeft, OvertakingRight, Blocking }
    public enum RaceTactic { HoldLine, BrakeForSafety, OvertakeLeft, OvertakeRight, DefendLeft, DefendRight }

    /// <summary>Opponent state expressed in the ego vehicle's local track frame.</summary>
    [Serializable]
    public struct OpponentKinematics
    {
        public float LongitudinalDistance;
        public float LateralOffset;
        public float RelativeLongitudinalSpeed;
        public float RelativeLateralSpeed;
    }

    [Serializable]
    public struct RaceTacticInput
    {
        [Min(1f)] public float TrackWidth;
        [Min(0f)] public float VehicleHalfWidth;
        [Min(0f)] public float SafetyMargin;
        [Min(0f)] public float MinimumTimeToCollision;
        public OpponentKinematics Opponent;
        public bool LeftLaneClear;
        public bool RightLaneClear;
    }

    /// <summary>Relative opponent observation and conservative tactical recommendation.</summary>
    public static class RaceTactics
    {
        public static float TimeToCollision(OpponentKinematics opponent)
        {
            if (opponent.LongitudinalDistance <= 0f || opponent.RelativeLongitudinalSpeed <= 0.001f)
                return float.PositiveInfinity;
            return opponent.LongitudinalDistance / opponent.RelativeLongitudinalSpeed;
        }

        public static OpponentIntent PredictIntent(OpponentKinematics opponent, float lateralThreshold = 0.25f)
        {
            if (opponent.LongitudinalDistance > 0f && opponent.RelativeLongitudinalSpeed > 0.1f)
                return OpponentIntent.Closing;
            if (opponent.LongitudinalDistance < 0f && Mathf.Abs(opponent.RelativeLateralSpeed) > lateralThreshold)
                return opponent.RelativeLateralSpeed > 0f ? OpponentIntent.OvertakingRight : OpponentIntent.OvertakingLeft;
            if (opponent.LongitudinalDistance < 0f && Mathf.Abs(opponent.RelativeLateralSpeed) <= lateralThreshold)
                return OpponentIntent.Blocking;
            return opponent.LongitudinalDistance > 0f ? OpponentIntent.Ahead : OpponentIntent.Unknown;
        }

        public static RaceTactic Choose(RaceTacticInput input)
        {
            var opponent = input.Opponent;
            var clearance = input.VehicleHalfWidth * 2f + input.SafetyMargin;
            var collisionTime = TimeToCollision(opponent);
            var opponentInLane = Mathf.Abs(opponent.LateralOffset) < clearance;

            if (opponentInLane && collisionTime < input.MinimumTimeToCollision)
            {
                if (input.LeftLaneClear && HasTrackClearance(input, -1f))
                    return RaceTactic.OvertakeLeft;
                if (input.RightLaneClear && HasTrackClearance(input, 1f))
                    return RaceTactic.OvertakeRight;
                return RaceTactic.BrakeForSafety;
            }

            if (opponent.LongitudinalDistance < 0f && Mathf.Abs(opponent.LongitudinalDistance) < 8f)
            {
                var intent = PredictIntent(opponent);
                if (intent == OpponentIntent.OvertakingLeft && HasTrackClearance(input, -1f))
                    return RaceTactic.DefendLeft;
                if (intent == OpponentIntent.OvertakingRight && HasTrackClearance(input, 1f))
                    return RaceTactic.DefendRight;
            }
            return RaceTactic.HoldLine;
        }

        static bool HasTrackClearance(RaceTacticInput input, float direction)
        {
            var requiredHalfWidth = input.VehicleHalfWidth + input.SafetyMargin;
            return input.TrackWidth * 0.5f >= requiredHalfWidth * 2f && Mathf.Abs(direction) == 1f;
        }
    }
}
