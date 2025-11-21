using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class CompProperties_AbilityTeleportSelf : CompProperties_AbilityEffect
    {
        public float range = 12f;
        public IntRange stunTicks = new IntRange(30, 60);
        public float maxBodySize = 2f;
        
        // 到达时的喧嚣效果
        public ClamorDef destClamorType;
        public float destClamorRadius = 2f;
        
        // 传送限制
        public bool requireLineOfSight = true;
        public bool canTeleportToFogged = true;
        public bool canTeleportToRoofed = true;

        // 自定义效果器 - 为大型生物设计
        public EffecterDef customEntryEffecter;
        public EffecterDef customExitEffecter;
        public FleckDef customEntryFleck;
        public FleckDef customExitFleck;
        public float effectScale = 1.0f; // 效果缩放比例

        public CompProperties_AbilityTeleportSelf()
        {
            compClass = typeof(CompAbilityEffect_TeleportSelf);
        }
    }
}
