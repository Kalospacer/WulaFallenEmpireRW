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
            if (pawn == null || pawn.Dead || pawn.Downed)
                return false;
                
            // 检查是否有自主机械组件
            var comp = pawn.GetComp<CompAutonomousMech>();
            if (comp == null)
                return false;
                
            // 检查当前工作模式是否匹配要求
            return comp.CurrentWorkMode == requiredMode;
        }
    }
}
