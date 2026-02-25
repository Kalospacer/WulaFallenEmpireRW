using RimWorld;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    public class ThinkNode_ConditionalWorkMode_Drone : ThinkNode_Conditional
    {
        public MechWorkModeDef workMode;

        public override ThinkNode DeepCopy(bool resolve = true)
        {
            ThinkNode_ConditionalWorkMode_Drone thinkNode_ConditionalWorkMode_Drone = (ThinkNode_ConditionalWorkMode_Drone)base.DeepCopy(resolve);
            thinkNode_ConditionalWorkMode_Drone.workMode = workMode;
            return thinkNode_ConditionalWorkMode_Drone;
        }

        protected override bool Satisfied(Pawn pawn)
        {
            if (!pawn.RaceProps.IsMechanoid || pawn.Faction != Faction.OfPlayer)
            {
                return false;
            }
            CompAutonomousMech compDrone = pawn.TryGetComp<CompAutonomousMech>();
            if (compDrone == null)
            {
                return false;
            }
            return compDrone.CurrentWorkMode == workMode;
        }
    }
}