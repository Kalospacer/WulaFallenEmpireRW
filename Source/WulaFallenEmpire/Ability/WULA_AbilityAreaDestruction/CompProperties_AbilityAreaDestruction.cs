using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class CompProperties_AbilityAreaDestruction : CompProperties_AbilityEffect
    {
        public float range;
        public float lineWidthEnd;
        
        // 发射特效（在施法者位置）
        public EffecterDef castEffecter;
        public int castEffecterMaintainTicks = 60;
        
        // 命中特效（在目标位置）
        public EffecterDef hitEffecter;
        public int hitEffecterMaintainTicks = 30;
        
        public bool canHitFilledCells;
        public bool affectAllies = false;
        public bool affectCaster = false;

        public CompProperties_AbilityAreaDestruction()
        {
            compClass = typeof(CompAbilityEffect_AreaDestruction);
        }
    }
}
