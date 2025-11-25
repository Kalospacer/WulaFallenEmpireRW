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
        
        // 传送设置 - 移除冷却时间
        public int checkIntervalTicks = 30; // 检查间隔
        public int stunTicks = 30; // 传送后眩晕时间
        public int maxPositionAdjustRadius = 5; // 最大位置调整半径
        
        // 效果设置
        public EffecterDef entryEffecter;
        public EffecterDef exitEffecter;
        public EffecterDef enterRangeEffecter;
        public EffecterDef leaveRangeEffecter;
        public SoundDef teleportSound;
        
        // 到达时的喧嚣效果
        public ClamorDef destClamorType;
        public float destClamorRadius = 2f;

        public CompProperties_AreaTeleporter()
        {
            compClass = typeof(ThingComp_AreaTeleporter);
        }
    }
}
