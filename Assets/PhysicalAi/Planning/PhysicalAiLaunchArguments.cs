using System.Collections.Generic;
using PhysicalAi.Configuration;

namespace PhysicalAi.Planning
{
    /// <summary>Compatibility facade; all subsystems share RuntimeArguments.</summary>
    public static class PhysicalAiLaunchArguments
    {
        public static string[] Current => RuntimeArguments.Current;
        public static void Override(IEnumerable<string> arguments) => RuntimeArguments.Override(arguments);
        public static void ClearOverride() => RuntimeArguments.ClearOverride();
        public static bool HasFlag(string parameter) => RuntimeArguments.HasFlag(parameter);
        public static string GetValue(string parameter) => RuntimeArguments.GetValue(parameter);
        public static float GetFloat(string parameter, float fallback) => RuntimeArguments.GetFloat(parameter, fallback);
        public static int GetInt(string parameter, int fallback) => RuntimeArguments.GetInt(parameter, fallback);
    }
}
