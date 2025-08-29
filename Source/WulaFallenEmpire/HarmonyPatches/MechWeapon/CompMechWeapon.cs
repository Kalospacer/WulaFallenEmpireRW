using Verse;

namespace WulaFallenEmpire
{
    public class CompMechWeapon : ThingComp
{
    // You can add custom logic or fields here if needed for this component.
    // For now, it primarily serves as a marker for mechanical units that can use MechWeapon features.
}


public class CompProperties_MechWeapon : CompProperties
{
    public CompProperties_MechWeapon()
    {
        compClass = typeof(CompMechWeapon);
    }
}
}