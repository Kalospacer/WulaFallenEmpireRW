using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
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