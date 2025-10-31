using System.Collections.Generic;
using Verse;
using RimWorld;

namespace WulaFallenEmpire
{
    public class CompProperties_CustomUniqueWeapon : CompProperties_UniqueWeapon
    {
        // A list of traits that will always be added to the weapon.
        public List<WeaponTraitDef> forcedTraits;

        // The range of traits to randomly add. If not defined in XML, a default of 1-3 will be used.
        public IntRange? numTraitsRange;

        public CompProperties_CustomUniqueWeapon()
        {
            // Point to the implementation of our custom logic.
            this.compClass = typeof(CompCustomUniqueWeapon);
        }
    }
}