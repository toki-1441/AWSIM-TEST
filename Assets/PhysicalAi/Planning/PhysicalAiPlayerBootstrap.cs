// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PhysicalAi.Planning
{
    /// <summary>Chooses the scene a player build starts in.</summary>
    /// <remarks>
    /// This is a component in the initial build scene, rather than only a
    /// <c>RuntimeInitializeOnLoadMethod</c>. The latter is removed by managed
    /// stripping when its assembly has no scene reference, leaving a player in
    /// an empty bootstrap scene and silently producing no evaluation report.
    /// </remarks>
    [DefaultExecutionOrder(-10000)]
    public sealed class PhysicalAiPlayerBootstrap : MonoBehaviour
    {
        const string AutowareDemoArgument = "-physicalAiAutowareDemo";

        void Awake()
        {
            var arguments = Environment.GetCommandLineArgs();
            // Unity's PlayMode runner creates its own empty bootstrap scene. Do not
            // replace it with the regular AWSIM demo, whose runtime systems are not
            // part of these isolated tests.
            foreach (var argument in arguments)
                if (argument == "-runTests")
                    return;

            // The AWSIM demo needs the environment assets under Assets/Awsim/Externals,
            // which are distributed separately and are absent from a plain checkout.
            // Starting there by default drops the ego vehicle into an empty world, so
            // it is opt-in and the self-contained Physical AI scene is the default.
            foreach (var argument in arguments)
            {
                if (argument != AutowareDemoArgument)
                    continue;
                SceneManager.LoadScene("AutowareSimulationDemo", LoadSceneMode.Single);
                return;
            }
            SceneManager.LoadScene("PhysicalAiTraining", LoadSceneMode.Single);
        }
    }
}
