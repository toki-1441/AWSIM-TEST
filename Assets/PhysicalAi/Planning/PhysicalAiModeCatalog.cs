// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Linq;

namespace PhysicalAi.Planning
{
    /// <summary>One selectable way to start the simulator.</summary>
    public sealed class PhysicalAiMode
    {
        public string Id { get; }
        public string Title { get; }
        public string Description { get; }
        /// <summary>Arguments this mode contributes, without any variant.</summary>
        public IReadOnlyList<string> Arguments { get; }
        /// <summary>Sub-choices such as a scenario or an evaluation suite; empty when the mode has none.</summary>
        public IReadOnlyList<PhysicalAiModeVariant> Variants { get; }

        public PhysicalAiMode(string id, string title, string description,
            IReadOnlyList<string> arguments, IReadOnlyList<PhysicalAiModeVariant> variants = null)
        {
            Id = id;
            Title = title;
            Description = description;
            Arguments = arguments ?? Array.Empty<string>();
            Variants = variants ?? Array.Empty<PhysicalAiModeVariant>();
        }
    }

    /// <summary>A second-level choice within a mode.</summary>
    public sealed class PhysicalAiModeVariant
    {
        public string Id { get; }
        public string Title { get; }
        public IReadOnlyList<string> Arguments { get; }

        public PhysicalAiModeVariant(string id, string title, IReadOnlyList<string> arguments)
        {
            Id = id;
            Title = title;
            Arguments = arguments ?? Array.Empty<string>();
        }
    }

    /// <summary>
    /// The declarative set of launch modes offered when the player starts without
    /// arguments. Adding a mode is adding an entry here: the selection screen, the
    /// resolved argument list and the displayed command line all derive from this
    /// table, so nothing else has to be edited.
    ///
    /// Modes are presets rather than a parameter editor. Exhaustively exposing every
    /// flag would create a second interface to maintain beside the command line, and
    /// the two would drift; anything not offered here is still available by passing
    /// the arguments directly.
    /// </summary>
    public static class PhysicalAiModeCatalog
    {
        public const string EvaluationArgument = "-physicalAiEvaluation";
        public const string AutowareDemoArgument = "-physicalAiAutowareDemo";
        const string DemonstrationScenarioArgument = "-physicalAiDemonstrationScenario";
        const string ArgumentPrefix = "-physicalAi";

        public static IReadOnlyList<PhysicalAiMode> Modes { get; } = new[]
        {
            new PhysicalAiMode(
                "manual-drive",
                "Manual driving",
                "Drive the generated course yourself. F8 toggles teleoperation and starts recording.",
                Array.Empty<string>(),
                new[]
                {
                    new PhysicalAiModeVariant("nominal", "Course following",
                        new[] { DemonstrationScenarioArgument, "nominal" }),
                    new PhysicalAiModeVariant("blind-corner", "Blind-corner hazards",
                        new[] { DemonstrationScenarioArgument, "blind-corner" }),
                    new PhysicalAiModeVariant("head-to-head", "Head to head",
                        new[] { DemonstrationScenarioArgument, "head-to-head" }),
                }),
            new PhysicalAiMode(
                "evaluation",
                "Evaluation",
                "Run a fixed suite without a driver and write a report.",
                Array.Empty<string>(),
                new[]
                {
                    new PhysicalAiModeVariant("holdout", "Unseen holdout courses",
                        new[] { EvaluationArgument, "holdout" }),
                    new PhysicalAiModeVariant("training", "Training-distribution courses",
                        new[] { EvaluationArgument, "training" }),
                }),
            new PhysicalAiMode(
                "rl-rollout",
                "RL rollout",
                "Wait for a Python policy bridge and drive from it.",
                new[] { EvaluationArgument, "rollout" }),
            new PhysicalAiMode(
                "autoware-demo",
                "AWSIM demo",
                "The upstream demo scene. Needs the environment assets under Assets/Awsim/Externals.",
                new[] { AutowareDemoArgument }),
        };

        public static PhysicalAiMode Find(string id) => Modes.FirstOrDefault(mode => mode.Id == id);

        /// <summary>Arguments for a mode and an optional variant, in the order a command line would carry them.</summary>
        public static string[] Resolve(PhysicalAiMode mode, PhysicalAiModeVariant variant)
        {
            if (mode == null)
                throw new ArgumentNullException(nameof(mode));
            if (variant != null && !mode.Variants.Contains(variant))
                throw new ArgumentException($"Variant '{variant.Id}' does not belong to mode '{mode.Id}'.", nameof(variant));
            if (variant == null && mode.Variants.Count > 0)
                throw new ArgumentException($"Mode '{mode.Id}' requires a variant.", nameof(variant));
            return mode.Arguments.Concat(variant?.Arguments ?? Array.Empty<string>()).ToArray();
        }

        /// <summary>The command line that reproduces a selection, so an interactive run can be automated.</summary>
        public static string DescribeCommandLine(PhysicalAiMode mode, PhysicalAiModeVariant variant)
        {
            var arguments = Resolve(mode, variant);
            return arguments.Length == 0
                ? "Build/AWSIM.x86_64"
                : "Build/AWSIM.x86_64 " + string.Join(" ", arguments);
        }

        /// <summary>
        /// True when the command line already determines the run, in which case the
        /// selection screen is skipped and the command line stays authoritative.
        ///
        /// Any Physical AI argument counts, not only one that names a mode. A script
        /// that configures a run — a hazard probability, a report directory, a
        /// recording flag — has already decided what it wants, and interrupting it
        /// with a menu would stall an automated or scripted session that nobody is
        /// watching. The screen is for a bare launch.
        /// </summary>
        public static bool CommandLineSelectsAMode(IReadOnlyList<string> arguments)
        {
            if (arguments == null)
                return false;
            foreach (var argument in arguments)
                if (argument != null && argument.StartsWith(ArgumentPrefix, StringComparison.Ordinal))
                    return true;
            return false;
        }
    }
}
