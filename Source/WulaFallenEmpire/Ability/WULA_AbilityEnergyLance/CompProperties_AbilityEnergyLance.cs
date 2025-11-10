using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class CompProperties_AbilityEnergyLance : CompProperties_EffectWithDest
    {
        // 光束持续时间
        public int durationTicks = 600;
        
        // 移动配置
        public float moveDistance = 15f;
        public bool useFixedDistance = true;
        
        // 光束类型配置 - 新增：暴露光束类型
        public ThingDef energyLanceDef;            // 使用的EnergyLance ThingDef
        public int firesPerTick = 4;               // 每刻造成的火灾数量
        
        public CompProperties_AbilityEnergyLance()
        {
            this.compClass = typeof(CompAbilityEffect_EnergyLance);
            this.destination = AbilityEffectDestination.Selected;
        }
    }
}
