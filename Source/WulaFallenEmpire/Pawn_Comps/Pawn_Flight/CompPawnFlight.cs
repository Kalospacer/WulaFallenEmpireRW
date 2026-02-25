using Verse;

namespace WulaFallenEmpire
{
    /// <summary>
    /// A marker component that holds custom flight properties.
    /// The actual flight logic is handled by Harmony patches that check for this component
    /// and use its properties to override or trigger vanilla flight behavior.
    /// </summary>
    public class CompPawnFlight : ThingComp
    {
        public CompProperties_PawnFlight Props => (CompProperties_PawnFlight)props;
    }
}