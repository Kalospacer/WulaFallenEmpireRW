using Verse;

namespace WulaFallenEmpire
{
    /// <summary>
    /// Centralized debug logging controlled by mod settings.
    /// Only shows when mod option is enabled, independent of DevMode.
    /// </summary>
    public static class WulaLog
    {
        private static bool DebugEnabled =>
            WulaFallenEmpireMod.settings?.enableDebugLogs ?? false;

        public static void Debug(string message)
        {
            if (DebugEnabled)
            {
                Log.Message(message);
            }
        }
    }
}
