using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class CompProperties_SkyfallerFaction : CompProperties
    {
        public FactionDef factionDef;
        public bool usePlayerFactionIfNull = true; // 如果 factionDef 为 null 时使用玩家派系

        public CompProperties_SkyfallerFaction()
        {
            compClass = typeof(CompSkyfallerFaction);
        }
    }
}
