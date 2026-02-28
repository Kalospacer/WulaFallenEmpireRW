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
        public bool flightEnabled = true;

        public CompProperties_PawnFlight Props => (CompProperties_PawnFlight)props;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref flightEnabled, "flightEnabled", true);
        }

        public override System.Collections.Generic.IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (Props.showFlightToggle && parent is Pawn pawn && pawn.Faction == RimWorld.Faction.OfPlayer)
            {
                yield return new Command_Toggle
                {
                    defaultLabel = "WULA_ToggleFlight".Translate(),
                    defaultDesc = "WULA_ToggleFlight_Desc".Translate() + (flightEnabled ? "WULA_ToggleFlight_Enable".Translate() : "WULA_ToggleFlight_Disable".Translate()),
                    Order = 100f,
                    icon = ContentFinder<UnityEngine.Texture2D>.Get("Wula/UI/Commands/WULA_FlightToggle", false)
                           ?? RimWorld.TexCommand.GatherSpotActive,
                    isActive = () => flightEnabled,
                    toggleAction = () =>
                    {
                        flightEnabled = !flightEnabled;
                        if (!flightEnabled && pawn.flight != null && pawn.flight.Flying)
                        {
                            pawn.flight.ForceLand();
                            if (pawn.CurJob != null)
                                pawn.CurJob.flying = false;
                        }
                    }
                };
            }
        }
    }
}