using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class CompProperties_EnergyLance : CompProperties_EffectWithDest
    {
        public int durationTicks = 600;                    // 光束持续时间
        public float moveDistance = 15f;                   // 光束移动距离
        public bool useFixedDistance = true;              // 是否使用固定距离
        
        // 伤害配置
        public int firesPerTick = 4;                       // 每刻产生的火焰数量
        public IntRange flameDamageRange = new IntRange(65, 100);      // 火焰伤害范围
        public IntRange corpseFlameDamageRange = new IntRange(5, 10);  // 尸体火焰伤害范围
        
        public CompProperties_EnergyLance()
        {
            this.compClass = typeof(CompAbilityEffect_EnergyLance);
        }
    }
}
