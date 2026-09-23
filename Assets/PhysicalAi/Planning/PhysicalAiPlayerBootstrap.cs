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

            try
            {
                arguments = PhysicalAiLaunchArguments.Current;
                PhysicalAi.Configuration.SimulatorLaunchSettings.ValidateCurrentArguments();
                var map = PhysicalAiLaunchArguments.GetValue("-physicalAiMapPath");
                if (!string.IsNullOrEmpty(map)) PhysicalAi.Course.CourseMapDocument.Load(map);
            }
            catch (Exception error)
            {
                Debug.LogError("Physical AI launch configuration: " + error.Message);
                if (Application.isBatchMode || Array.IndexOf(arguments, "-physicalAiQuitAfterEvaluation") >= 0) Application.Quit(2);
                else PhysicalAiModeSelectionScreen.ShowError(error.Message);
                return;
            }

            // The command line stays authoritative: when it already chooses what to
            // run, start there directly.
            if (PhysicalAiModeCatalog.CommandLineSelectsAMode(arguments))
            {
                foreach (var argument in arguments)
                {
                    if (argument != AutowareDemoArgument)
                        continue;
                    SceneManager.LoadScene("AutowareSimulationDemo", LoadSceneMode.Single);
                    return;
                }
                SceneManager.LoadScene("PhysicalAiTraining", LoadSceneMode.Single);
                return;
            }

            // Otherwise let the operator choose. A build has 23 launch flags; requiring
            // them is an implementer-only interface, and the acceptance criteria for
            // manual data collection need an operator who is not the author.
            PhysicalAiModeSelectionScreen.Create();
        }
    }
}
