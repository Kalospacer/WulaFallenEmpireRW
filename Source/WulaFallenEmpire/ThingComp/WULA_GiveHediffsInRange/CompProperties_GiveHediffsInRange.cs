using RimWorld;
using Verse;
using System.Collections.Generic;

namespace WulaFallenEmpire
{
    public class CompProperties_GiveHediffsInRange : CompProperties
    {
        public float range = 10f;
        public TargetingParameters targetingParameters;
        public HediffDef hediff;
        public ThingDef mote;
        public bool hideMoteWhenNotDrafted;
        public float initialSeverity = 1f;
        public bool onlyPawnsInSameFaction = true;
        
        // 新增：种族筛选
        public List<ThingDef> allowedRaces;
        public List<ThingDef> excludedRaces;
        public bool requireHumanlike = false;
        public bool requireAnimal = false;
        public bool requireMechanoid = false;

        // 新增：效果控制
        public bool affectAllies = true;
        public bool affectEnemies = true;
        public bool affectNeutrals = true;
        public bool affectPrisoners = false;
        public bool affectSlaves = false;
        
        // 新增：间隔控制
        public int checkIntervalTicks = 60;
        public int hediffDurationTicks = -1; // -1 表示永久

        public CompProperties_GiveHediffsInRange()
        {
            compClass = typeof(CompGiveHediffsInRange);
        }
    }
}
