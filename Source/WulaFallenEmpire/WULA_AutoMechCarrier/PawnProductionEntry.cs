using Verse;

namespace WulaFallenEmpire
{
    /// <summary>
    /// A data class to hold information about a pawn to be produced in a queue.
    /// Used in XML definitions.
    /// </summary>
    public class PawnProductionEntry
    {
        // The PawnKindDef of the unit to spawn.
        public PawnKindDef pawnKind;

        // The maximum number of this kind of unit to maintain.
        public int count = 1;

        // Optional: specific cooldown for this entry. If not set, the parent comp's cooldown is used.
        public int? cooldownTicks;
        
        // Optional: specific cost for this entry. If not set, the parent comp's costPerPawn is used.
        public int? cost;
    }
}