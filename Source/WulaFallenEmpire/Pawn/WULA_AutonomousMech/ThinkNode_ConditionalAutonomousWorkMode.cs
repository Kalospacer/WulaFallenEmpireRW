using RimWorld;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    public class ThinkNode_ConditionalAutonomousWorkMode : ThinkNode_Conditional
    {
        public DroneWorkModeDef requiredMode;
        
        protected override bool Satisfied(Pawn pawn)
        {
            try
            {
                if (pawn == null || pawn.Dead || pawn.Downed)
                    return false;
                
                // 检查是否有自主机械组件
                var comp = pawn.GetComp<CompAutonomousMech>();
                if (comp == null)
                    return false;
                
                // 检查当前工作模式是否匹配要求
                if (comp.CurrentWorkMode != requiredMode)
                    return false;
                
                // 额外的安全检查：确保pawn有工作设置
                if (pawn.workSettings == null)
                {
                    Log.Warning($"[WULA] {pawn.LabelShort} has no workSettings in ThinkNode_ConditionalAutonomousWorkMode");
                    return false;
                }
                
                // 检查是否启用了工作
                if (!pawn.workSettings.EverWork)
                {
                    Log.Warning($"[WULA] {pawn.LabelShort} has EverWork=false in ThinkNode_ConditionalAutonomousWorkMode");
                    return false;
                }
                
                // 检查是否有工作给予器
                var workGivers = pawn.workSettings.WorkGiversInOrderNormal;
                if (workGivers == null || workGivers.Count == 0)
                {
                    Log.Warning($"[WULA] {pawn.LabelShort} has no work givers in ThinkNode_ConditionalAutonomousWorkMode");
                    return false;
                }
                
                // 检查是否为机械体且具有机械体能力
                if (pawn.RaceProps.IsMechanoid)
                {
                    // 检查是否有操纵能力
                    var manipulation = pawn.health?.capacities?.GetLevel(PawnCapacityDefOf.Manipulation);
                    if (manipulation < 0.1f)
                    {
                        Log.Warning($"[WULA] {pawn.LabelShort} has insufficient manipulation capacity: {manipulation}");
                        return false;
                    }
                }
                
                return true;
            }
            catch (System.Exception ex)
            {
                Log.Error($"[WULA] Exception in ThinkNode_ConditionalAutonomousWorkMode.Satisfied for pawn {pawn?.LabelShort}: {ex}");
                return false;
            }
        }
    }
}
