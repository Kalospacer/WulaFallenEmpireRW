using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class CompProperties_AbilityPullTarget : CompProperties_AbilityEffect
    {
        public float range = 12f;
        public float minRange = 1f; // 新增：最小拉取距离，默认为1格
        public IntRange stunTicks = new IntRange(30, 60);
        public float maxTargetBodySize = 2f; // 最大可拉取目标的体型
        
        // 拉取目的地设置
        public int pullDestinationSearchRadius = 3; // 在施法者周围搜索拉取目的地的半径
        public bool requireLineOfSightToDestination = false; // 是否需要视线到拉取目的地
        
        // 到达时的喧嚣效果
        public ClamorDef destClamorType;
        public float destClamorRadius = 2f;
        
        // 拉取限制
        public bool requireLineOfSight = true;
        public bool canTeleportToFogged = true;
        public bool canTeleportToRoofed = true;

        // 派系关系限制
        public bool canPullHostile = true;
        public bool canPullNeutral = true;
        public bool canPullFriendly = false; // 默认不拉取友军

        // 自定义效果器
        public EffecterDef customEntryEffecter;
        public EffecterDef customExitEffecter;
        public FleckDef customEntryFleck;
        public FleckDef customExitFleck;
        public float effectScale = 1.0f; // 效果缩放比例

        // 位置调整设置
        public int maxPositionAdjustRadius = 15; // 最大位置调整半径
        public bool allowPositionAdjustment = true; // 是否允许自动调整位置

        public CompProperties_AbilityPullTarget()
        {
            compClass = typeof(CompAbilityEffect_PullTarget);
        }
    }
}
