using Verse;
using RimWorld;

namespace WulaFallenEmpire
{
    public enum FlightCondition
    {
        Drafted,
        DraftedAndMove,
        Always
    }

    public class CompProperties_PawnFlight : CompProperties
    {
        // --- Custom Flight Logic ---
        public FlightCondition flightCondition = FlightCondition.Drafted;
        public bool showFlightToggle = true;

        // --- Vanilla PawnKindDef Flight Parameters ---
        [NoTranslate]
        public string flyingAnimationFramePathPrefix;

        [NoTranslate]
        public string flyingAnimationFramePathPrefixFemale;

        public int flyingAnimationFrameCount;

        public int flyingAnimationTicksPerFrame = -1;

        public float flyingAnimationDrawSize = 1f;

        public bool flyingAnimationDrawSizeIsMultiplier;

        public bool flyingAnimationInheritColors;

        // --- Vanilla PawnKindLifeStage Flight Parameters ---
        // Note: These are normally defined per lifestage, we define them once here for simplicity.
        // The harmony patch will need to inject these into the correct lifestage at runtime.
        public AnimationDef flyingAnimationEast;
        public AnimationDef flyingAnimationNorth;
        public AnimationDef flyingAnimationSouth;
        public AnimationDef flyingAnimationEastFemale;
        public AnimationDef flyingAnimationNorthFemale;
        public AnimationDef flyingAnimationSouthFemale;


        public CompProperties_PawnFlight()
        {
            compClass = typeof(CompPawnFlight);
        }
    }
}