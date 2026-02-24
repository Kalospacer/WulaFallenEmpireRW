using Verse;
using RimWorld;
using System.Collections.Generic;

namespace WulaFallenEmpire
{
    public class CompProperties_AbilityLaunchMultiProjectile : CompProperties_AbilityLaunchProjectile
    {
        public int numProjectiles = 1;
        
        // 发射间隔控制
        public int shotIntervalTicks = 0;  // 每两发射弹的间隔时间（tick）
        public bool useSustainedJob = false; // 是否使用持续Job
        
        // 偏移范围
        public float offsetRadius = 0f;
        public bool useRandomOffset = false;
        public FloatRange offsetRange = new FloatRange(-1f, 1f);
        public bool offsetInLineOnly = false;
        public bool offsetInCircle = true;
        public float minOffsetDistance = 0f;
        public bool avoidOverlap = false;
        public float minProjectileSpacing = 1f;
        
        // 持续发射控制
        public int maxSustainTicks = 180; // 最大持续时间（参考火焰技能）
        public int startDelayTicks = 10; // 开始发射前的延迟

        // 状态定义
        public List<ProjectileState> states;

        public CompProperties_AbilityLaunchMultiProjectile()
        {
            compClass = typeof(CompAbilityEffect_LaunchMultiProjectile);
        }
    }

    // 射弹状态参数
    public class ProjectileState
    {
        public HediffDef hediffDef;          // 触发的hediff
        public int numProjectiles = 1;       // 射弹数量（可选）
        public ThingDef projectileDef;       // 射弹类型（可选）
        public float offsetRadius = 0f;      // 偏移半径（可选）
        public int shotIntervalTicks = 0;    // 发射间隔（可选）
    }
}
