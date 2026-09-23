// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Globalization;

namespace PhysicalAi.Configuration
{
    /// <summary>Shared by all assemblies without a Safety/Sensors -> Planning dependency cycle.</summary>
    public static class RuntimeArguments
    {
        static string[] _override;
        static string[] _process;
        public static string[] Current => _override ?? (_process ??= Expand(Environment.GetCommandLineArgs()));
        public static string[] Expand(string[] arguments)
        {
            string config = null;
            for (var i = 0; i < arguments.Length; i++)
                if (arguments[i] == "-physicalAiConfig")
                {
                    if (i + 1 >= arguments.Length) throw new ArgumentException("-physicalAiConfig requires a JSON file.");
                    config = arguments[++i];
                }
            // Resolve high-level selections before deriving RL/evaluation/scenario switches.
            // Otherwise a CLI mode override leaves the previous config mode's switches active.
            string Selection(string option)
            {
                var index = Array.LastIndexOf(arguments, option);
                if (index < 0) return null;
                if (index + 1 >= arguments.Length || arguments[index + 1].StartsWith("-"))
                    throw new ArgumentException(option + " requires a value.");
                return arguments[index + 1];
            }
            var mode = Selection("-physicalAiMode");
            if (config == null && mode == null) return (string[])arguments.Clone(); // Preserve legacy script presets.
            var settings = config == null ? new SimulatorLaunchSettings() : SimulatorLaunchSettings.Load(config);
            settings.mode = mode ?? settings.mode;
            settings.scenario = Selection("-physicalAiDemonstrationScenario") ?? settings.scenario;
            var mapMode = Selection("-physicalAiMapMode");
            if (mapMode != null)
            {
                settings.mapMode = mapMode;
                if (mapMode == "endless") settings.mapPath = "";
            }
            var result = new List<string> { arguments.Length == 0 ? "PhysicalAi" : arguments[0] };
            result.AddRange(settings.ToArguments());
            // Explicit CLI values override configuration values. GetValue is last-wins.
            for (var i = 1; i < arguments.Length; i++) result.Add(arguments[i]);
            return result.ToArray();
        }
        public static void Override(IEnumerable<string> arguments)
        {
            if (arguments == null) throw new ArgumentNullException(nameof(arguments));
            var values = new List<string> { "PhysicalAi" };
            values.AddRange(arguments);
            _override = Expand(values.ToArray());
        }
        public static void ClearOverride() { _override = null; _process = null; }
        public static bool HasFlag(string flag)
        {
            var arguments = Current;
            var index = Array.LastIndexOf(arguments, flag);
            if (index < 0) return false;
            // Bare switches are true; an explicit final false overrides a config's earlier switch.
            return index + 1 >= arguments.Length ||
                !string.Equals(arguments[index + 1], "false", StringComparison.OrdinalIgnoreCase);
        }
        public static string GetValue(string flag)
        {
            var arguments = Current;
            for (var i = arguments.Length - 1; i >= 0; i--)
                if (arguments[i] == flag)
                {
                    if (i + 1 >= arguments.Length || (arguments[i + 1].StartsWith("-") &&
                        !float.TryParse(arguments[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
                        throw new ArgumentException(flag + " requires a value.");
                    return arguments[i + 1];
                }
            return null;
        }
        public static int GetInt(string flag, int fallback)
        {
            var value = GetValue(flag);
            if (value == null) return fallback;
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)) return result;
            throw new ArgumentException(flag + " requires a whole number, got: " + value);
        }
        public static float GetFloat(string flag, float fallback)
        {
            var value = GetValue(flag);
            if (value == null) return fallback;
            if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) && float.IsFinite(result)) return result;
            throw new ArgumentException(flag + " requires a finite number, got: " + value);
        }
    }
}
