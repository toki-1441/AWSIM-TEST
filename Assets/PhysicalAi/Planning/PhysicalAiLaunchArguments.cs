// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;

namespace PhysicalAi.Planning
{
    /// <summary>
    /// The single source of launch arguments for the Physical AI runtime.
    /// Components read their configuration through here rather than calling
    /// <see cref="Environment.GetCommandLineArgs"/> directly, so an interactive
    /// mode selection can supply the same configuration a command line would.
    /// The process command line is the default and remains authoritative: an
    /// override is only ever installed when no mode was requested on it.
    /// </summary>
    public static class PhysicalAiLaunchArguments
    {
        static string[] _override;

        /// <summary>Arguments currently in effect, including the process name at index 0.</summary>
        public static string[] Current => _override ?? Environment.GetCommandLineArgs();

        /// <summary>Replaces the arguments with an interactively chosen set.</summary>
        public static void Override(IEnumerable<string> arguments)
        {
            if (arguments == null)
                throw new ArgumentNullException(nameof(arguments));
            var resolved = new List<string> { "PhysicalAi" };
            resolved.AddRange(arguments);
            _override = resolved.ToArray();
        }

        /// <summary>Restores the process command line. Used by tests and by returning to the mode screen.</summary>
        public static void ClearOverride() => _override = null;

        public static bool HasFlag(string parameter)
        {
            foreach (var argument in Current)
                if (argument == parameter)
                    return true;
            return false;
        }

        /// <summary>Value that follows <paramref name="parameter"/>, or null when it is absent or last.</summary>
        public static string GetValue(string parameter)
        {
            var arguments = Current;
            for (var index = 0; index + 1 < arguments.Length; index++)
                if (arguments[index] == parameter)
                    return arguments[index + 1];
            return null;
        }

        public static float GetFloat(string parameter, float fallback)
        {
            var value = GetValue(parameter);
            return float.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : fallback;
        }

        public static int GetInt(string parameter, int fallback)
        {
            var value = GetValue(parameter);
            return int.TryParse(value, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : fallback;
        }
    }
}
