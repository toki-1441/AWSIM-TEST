// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using System;
using UnityEngine;

namespace PhysicalAi.Planning
{
    /// <summary>Portable linear BC model emitted by scripts/train_bc.py.</summary>
    [Serializable]
    public class BehaviorCloningModel
    {
        public float[] SpeedWeights;
        public float SpeedBias;
        public float[] LateralWeights;
        public float LateralBias;
    }

    public static class BehaviorCloningInference
    {
        public static PlanningCommand Predict(BehaviorCloningModel model, PlanningObservation observation)
        {
            if (model == null || observation.Features == null || model.SpeedWeights == null || model.LateralWeights == null ||
                model.SpeedWeights.Length != observation.Features.Length || model.LateralWeights.Length != observation.Features.Length)
                throw new ArgumentException("The BC model dimensions must match the planning observation.");

            var speed = model.SpeedBias;
            var lateral = model.LateralBias;
            for (var i = 0; i < observation.Features.Length; i++)
            {
                speed += model.SpeedWeights[i] * observation.Features[i];
                lateral += model.LateralWeights[i] * observation.Features[i];
            }
            return new PlanningCommand
            {
                DesiredSpeed = Mathf.Max(0f, speed),
                DesiredLateralOffsetNormalized = Mathf.Clamp(lateral, -1f, 1f)
            };
        }
    }

    /// <summary>Optional Unity runtime policy; safely falls back when no compatible exported model is assigned.</summary>
    public sealed class BehaviorCloningPolicy : MonoBehaviour
    {
        [SerializeField] TextAsset _modelAsset;
        BehaviorCloningModel _model;

        /// <summary>Whether a compatible exported BC model is available for inference.</summary>
        public bool IsReady => _model != null && _model.SpeedWeights != null && _model.LateralWeights != null;

        void Awake() => LoadModel();

        public void LoadModel()
        {
            _model = _modelAsset == null ? null : JsonUtility.FromJson<BehaviorCloningModel>(_modelAsset.text);
        }

        public bool TryPredict(PlanningObservation observation, out PlanningCommand command)
        {
            if (_model == null || observation.Features == null || _model.SpeedWeights == null || _model.LateralWeights == null ||
                _model.SpeedWeights.Length != observation.Features.Length || _model.LateralWeights.Length != observation.Features.Length)
            {
                command = default;
                return false;
            }
            command = BehaviorCloningInference.Predict(_model, observation);
            return true;
        }
    }
}
