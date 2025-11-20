using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class CompProperties_AbilityAreaDestruction : CompProperties_AbilityEffect
    {
        public float range;
        public float lineWidthEnd;
        public EffecterDef effecterDef;
        public bool canHitFilledCells;
        
        // 新增：命中效果器
        public EffecterDef hitEffecter;
        
        // 新增：是否影响友方单位
        public bool affectAllies = false;
        
        // 新增：是否影响施法者自己
        public bool affectCaster = false;

        public CompProperties_AbilityAreaDestruction()
        {
            compClass = typeof(CompAbilityEffect_AreaDestruction);
        }
    }
}
