// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PhysicalAi.Planning
{
    /// <summary>Loads the generated Physical AI scene only for explicit player evaluations.</summary>
    public static class PhysicalAiPlayerBootstrap
    {
        const string EvaluationArgument = "-physicalAiEvaluation";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void LoadEvaluationScene()
        {
            var arguments = Environment.GetCommandLineArgs();
            for (var index = 0; index + 1 < arguments.Length; index++)
            {
                if (arguments[index] != EvaluationArgument) continue;
                if (arguments[index + 1] != "training" && arguments[index + 1] != "holdout") return;
                SceneManager.LoadScene("PhysicalAiTraining", LoadSceneMode.Single);
                return;
            }
        }
    }
}
