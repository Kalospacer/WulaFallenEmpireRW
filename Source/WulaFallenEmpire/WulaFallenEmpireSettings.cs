using Verse;

namespace WulaFallenEmpire
{
    public class WulaFallenEmpireSettings : ModSettings
    {
        public bool enableDebugLogs = false;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref enableDebugLogs, "enableDebugLogs", false);
            base.ExposeData();
        }
    }
}
