using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    [DefOf]
    public static class ThingDefOf_WULA
    {
        public static ThingDef WULA_MaintenancePod;
        public static ThingDef WULA_Charging_Station_Synth;

        static ThingDefOf_WULA()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(ThingDefOf_WULA));
        }
    }
}