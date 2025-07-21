using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class Hediff_EmergencyEnergyRestore : HediffWithComps
    {
        private float originalEnergyLevel = 0f;
        private bool hasStoredOriginalLevel = false;

        public override void PostAdd(DamageInfo? dinfo)
        {
            base.PostAdd(dinfo);
            
            // 存储原始能量水平
            var energyNeed = pawn.needs?.TryGetNeed<Need_WulaEnergy>();
            if (energyNeed != null)
            {
                originalEnergyLevel = energyNeed.CurLevel;
                hasStoredOriginalLevel = true;
                
                // 立即将能量设置为100%
                energyNeed.CurLevel = 1.0f;
                
                if (Prefs.DevMode)
                {
                    Log.Message($"[EmergencyEnergyRestore] Stored original energy: {originalEnergyLevel:F2}, set to 1.0");
                }
            }
        }

        public override void PostRemoved()
        {
            base.PostRemoved();
            
            // 恢复原始能量水平
            if (hasStoredOriginalLevel)
            {
                var energyNeed = pawn.needs?.TryGetNeed<Need_WulaEnergy>();
                if (energyNeed != null)
                {
                    energyNeed.CurLevel = originalEnergyLevel;
                    
                    if (Prefs.DevMode)
                    {
                        Log.Message($"[EmergencyEnergyRestore] Restored energy to: {originalEnergyLevel:F2}");
                    }
                }
            }
        }

        public override void Tick()
        {
            base.Tick();
            
            // 确保能量保持在100%
            var energyNeed = pawn.needs?.TryGetNeed<Need_WulaEnergy>();
            if (energyNeed != null && energyNeed.CurLevel < 1.0f)
            {
                energyNeed.CurLevel = 1.0f;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref originalEnergyLevel, "originalEnergyLevel", 0f);
            Scribe_Values.Look(ref hasStoredOriginalLevel, "hasStoredOriginalLevel", false);
        }
    }
}