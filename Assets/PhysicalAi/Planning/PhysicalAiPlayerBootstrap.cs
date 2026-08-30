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
            // Unity's PlayMode runner creates its own empty bootstrap scene. Do not
            // replace it with the regular AWSIM demo, whose runtime systems are not
            // part of these isolated tests.
            foreach (var argument in arguments)
                if (argument == "-runTests")
                    return;
            for (var index = 0; index + 1 < arguments.Length; index++)
            {
                if (arguments[index] != EvaluationArgument) continue;
                if (arguments[index + 1] != "training" && arguments[index + 1] != "holdout") break;
                SceneManager.LoadScene("PhysicalAiTraining", LoadSceneMode.Single);
                return;
            }
            SceneManager.LoadScene("AutowareSimulationDemo", LoadSceneMode.Single);
        }
    }
}
