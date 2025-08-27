using Verse;

namespace WulaFallenEmpire
{
    /// <summary>
    /// This CompProperties class is used in the XML defs to add the CompForceTargetable to a Thing.
    /// </summary>
    public class CompProperties_ForceTargetable : CompProperties
    {
        public CompProperties_ForceTargetable()
        {
            this.compClass = typeof(CompForceTargetable);
        }
    }

    /// <summary>
    /// A simple marker component. Any Building_TurretGun that has this component
    /// will be forcefully made targetable by players via a Harmony patch.
    /// </summary>
    public class CompForceTargetable : ThingComp
    {
        // This component doesn't need any specific logic.
        // Its mere presence on a turret is checked by the Harmony patch.
    }
}