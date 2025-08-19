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
    }
}