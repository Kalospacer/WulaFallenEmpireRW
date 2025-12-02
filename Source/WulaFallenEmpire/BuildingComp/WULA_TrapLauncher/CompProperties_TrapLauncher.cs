using System.Collections.Generic;
using Verse;

namespace WulaFallenEmpire
{
    public class CompProperties_TrapLauncher : CompProperties
    {
        public float detectionRadius = 10f; // 检测半径
        public int scanIntervalTicks = 60; // 扫描间隔（ticks）
        public ThingDef projectileDef; // 抛射体定义
        public bool ignoreNonHostilePawns = true; // 是否忽略非敌对Pawn
        public bool requireLineOfSight = true; // 是否需要视线
        public int maxTargets = 1; // 最大目标数量
        public int warmupTicks = 30; // 发射前预热ticks
        public bool showDetectionRadius = true; // 是否显示检测半径
        
        // 触发时的音效
        public SoundDef triggerSound;
        public SoundDef launchSound;
        public SoundDef selfDestructSound;
        
        // 视觉效果
        public EffecterDef triggerEffect;
        public EffecterDef launchEffect;
        public EffecterDef selfDestructEffect;
        
        // 扩展选项
        public bool canRetarget = false; // 发射后是否可以重新锁定新目标
        public int burstCount = 1; // 连发数量
        public float burstDelay = 0.1f; // 连发延迟
        public bool targetBuildings = false; // 是否瞄准建筑
        
        public CompProperties_TrapLauncher()
        {
            this.compClass = typeof(CompTrapLauncher);
        }
        
        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (var error in base.ConfigErrors(parentDef))
            {
                yield return error;
            }
            
            if (projectileDef == null)
            {
                yield return $"CompProperties_TrapLauncher: projectileDef must be set for {parentDef.defName}";
            }
        }
    }
}
