using RimWorld;
using System.Collections.Generic;
using Verse;

namespace WulaFallenEmpire
{
    public class CompProperties_EquippableAbilities : CompProperties
    {
        public List<AbilityDef> abilityDefs;

        public CompProperties_EquippableAbilities()
        {
            compClass = typeof(CompEquippableAbilities);
        }
    }
}
