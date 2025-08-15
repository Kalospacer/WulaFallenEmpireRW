using Verse;

namespace WulaFallenEmpire
{
    [StaticConstructorOnStartup]
    public static class StartupLogger
    {
        static StartupLogger()
        {
            Log.Message("WulaFallenEmpire Mod DLL, version 1.0.2, has been loaded.");
        }
    }
}