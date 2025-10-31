using RimWorld;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    // 检查机械族是否需要充电
    public class ThinkNode_ConditionalNeedRecharge : ThinkNode_Conditional
    {
        public float energyThreshold = 0.3f;
        
        protected override bool Satisfied(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Downed)
                return false;
                
            var energyNeed = pawn.needs?.TryGetNeed<Need_MechEnergy>();
            if (energyNeed == null)
                return false;
                
            return energyNeed.CurLevelPercentage < energyThreshold;
        }
    }

    // 检查机械族是否紧急需要充电
    public class ThinkNode_ConditionalEmergencyRecharge : ThinkNode_Conditional
    {
        public float emergencyThreshold = 0.1f;
        
        protected override bool Satisfied(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Downed)
                return false;
                
            var energyNeed = pawn.needs?.TryGetNeed<Need_MechEnergy>();
            if (energyNeed == null)
                return false;
                
            return energyNeed.CurLevelPercentage < emergencyThreshold;
        }
    }

    // 检查机械族是否充满电
    public class ThinkNode_ConditionalFullyCharged : ThinkNode_Conditional
    {
        public float fullChargeThreshold = 0.9f;
        
        protected override bool Satisfied(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Downed)
                return false;
                
            var energyNeed = pawn.needs?.TryGetNeed<Need_MechEnergy>();
            if (energyNeed == null)
                return false;
                
            return energyNeed.CurLevelPercentage >= fullChargeThreshold;
        }
    }
}
