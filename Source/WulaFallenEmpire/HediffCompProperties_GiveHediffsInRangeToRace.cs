using RimWorld;
using System.Collections.Generic;
using Verse;

namespace WulaFallenEmpire
{
    public class HediffCompProperties_GiveHediffsInRangeToRace : HediffCompProperties
    {
        public float range;
        public TargetingParameters targetingParameters;
        public HediffDef hediff;
        public ThingDef mote;
        public bool hideMoteWhenNotDrafted;
        public float initialSeverity = 1f;
        public bool onlyPawnsInSameFaction = true;
        public List<ThingDef> targetRaces; // 新增：可配置的目标种族列表

        public HediffCompProperties_GiveHediffsInRangeToRace()
        {
            compClass = typeof(HediffComp_GiveHediffsInRangeToRace);
        }
    }
}