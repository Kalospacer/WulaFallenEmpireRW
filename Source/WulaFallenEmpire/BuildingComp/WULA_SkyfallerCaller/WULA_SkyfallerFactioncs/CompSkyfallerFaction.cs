using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class CompSkyfallerFaction : ThingComp
    {
        public CompProperties_SkyfallerFaction Props => (CompProperties_SkyfallerFaction)props;

        public Faction GetFactionForPrefab()
        {
            // 如果指定了派系定义，使用该派系
            if (Props.factionDef != null)
            {
                Faction faction = Find.FactionManager.FirstFactionOfDef(Props.factionDef);
                if (faction != null)
                    return faction;
            }

            // 如果没有指定派系定义，根据设置决定
            if (Props.usePlayerFactionIfNull)
            {
                return Faction.OfPlayer;
            }

            // 如果都不满足，返回 null（使用默认行为）
            return null;
        }
    }
}
