// CompProperties_MechInherentWeapon.cs
using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class CompProperties_MechInherentWeapon : CompProperties
    {
        public ThingDef weaponDef; // 固有武器的定义
        
        public CompProperties_MechInherentWeapon()
        {
            this.compClass = typeof(CompMechInherentWeapon);
        }
    }
}
