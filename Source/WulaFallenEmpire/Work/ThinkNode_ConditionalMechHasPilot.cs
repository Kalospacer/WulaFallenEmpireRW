// ThinkNode_ConditionalMechHasPilot.cs (修复版)
using RimWorld;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    public class ThinkNode_ConditionalMechHasPilot : ThinkNode_Conditional
    {
        // 可选的：可以在XML中设置的参数
        public int minPilotCount = 1;  // 最少需要的驾驶员数量

        protected override bool Satisfied(Pawn pawn)
        {
            if (pawn.Faction != Faction.OfPlayer)
                return false; // 仅适用于玩家派系的机甲
            var pilotComp = pawn.TryGetComp<CompMechPilotHolder>();
            if (pilotComp == null)
                return false; // 如果没有驾驶员组件，条件满足（允许执行）

            int currentPilotCount = pilotComp.CurrentPilotCount;

            // 检查是否满足最小驾驶员数量要求
            bool hasEnoughPilots = currentPilotCount >= minPilotCount;

            // 这意味着机甲可以正常工作
            bool conditionSatisfied = hasEnoughPilots;

            return !conditionSatisfied;
        }

        public override ThinkNode DeepCopy(bool resolve = true)
        {
            ThinkNode_ConditionalMechHasPilot thinkNode_ConditionalMechHasPilot = (ThinkNode_ConditionalMechHasPilot)base.DeepCopy(resolve);
            thinkNode_ConditionalMechHasPilot.minPilotCount = minPilotCount;
            return thinkNode_ConditionalMechHasPilot;
        }
    }
}