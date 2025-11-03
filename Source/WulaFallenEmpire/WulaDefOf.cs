using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    [DefOf]
    public static class ThingDefOf_WULA
    {
        public static ThingDef WULA_MaintenancePod;
        public static ThingDef WULA_Charging_Station_Synth;
        public static ThingDef WULA_PocketMapExit;

        static ThingDefOf_WULA()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(ThingDefOf_WULA));
        }
    }
    [DefOf]
    public static class JobDefOf_WULA
    {
        public static JobDef WULA_EnterMaintenancePod;

        public static JobDef WULA_HaulToMaintenancePod;

        static JobDefOf_WULA()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(JobDefOf_WULA));
        }
    }
}