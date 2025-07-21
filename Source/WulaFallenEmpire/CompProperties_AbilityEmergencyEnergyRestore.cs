using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class CompProperties_AbilityEmergencyEnergyRestore : CompProperties_AbilityEffect
    {
        public int durationTicks = 600; // 默认10秒
        public HediffDef hediffDef;
        public bool requireDowned = true; // 是否需要倒地才能使用
        public SoundDef soundCast;

        public CompProperties_AbilityEmergencyEnergyRestore()
        {
            compClass = typeof(CompAbilityEffect_EmergencyEnergyRestore);
        }
    }
}
