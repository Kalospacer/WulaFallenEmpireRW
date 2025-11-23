using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    public class ThinkNode_ConditionalLowEnergy_Drone : ThinkNode_Conditional
    {
        protected override bool Satisfied(Pawn pawn)
        {
            CompAutonomousMech compDrone = pawn.TryGetComp<CompAutonomousMech>();
            if (compDrone != null && compDrone.IsLowEnergy)
            {
                return true;
            }
            return false;
        }
    }
}