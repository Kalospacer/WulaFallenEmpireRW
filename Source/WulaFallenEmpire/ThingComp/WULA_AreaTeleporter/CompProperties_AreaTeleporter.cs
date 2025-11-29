using RimWorld;
using Verse;
using System.Collections.Generic;

namespace WulaFallenEmpire
{
    public class CompProperties_AreaTeleporter : CompProperties
    {
        public float teleportRadius = 15f;
        
        // 种族筛选
        public List<ThingDef> allowedRaces;
        public List<ThingDef> excludedRaces;
        public bool requireHumanlike = false;
        public bool requireAnimal = false;
        public bool requireMechanoid = false;

        // 派系控制
        public bool onlyPawnsInSameFaction = true;
        public bool affectAllies = true;
        public bool affectEnemies = true;
        public bool affectNeutrals = true;
        public bool affectPrisoners = false;
        public bool affectSlaves = false;
        
        // 传送设置
        public int checkIntervalTicks = 30;
        public int stunTicks = 30;
        public int maxPositionAdjustRadius = 5;
        
        // 效果设置
        public EffecterDef entryEffecter;
        public EffecterDef exitEffecter;
        public EffecterDef enterRangeEffecter;
        public EffecterDef leaveRangeEffecter;
        public SoundDef teleportSound;
        
        // 到达时的喧嚣效果
        public ClamorDef destClamorType;
        public float destClamorRadius = 2f;

        // 新增：科技需求
        public ResearchProjectDef requiredResearch;
        public bool requireResearchToUse = false;

        // 新增：开关控制
        public bool canBeToggled = true;
        public bool defaultEnabled = true;

        public CompProperties_AreaTeleporter()
        {
            compClass = typeof(ThingComp_AreaTeleporter);
        }
    }
}
