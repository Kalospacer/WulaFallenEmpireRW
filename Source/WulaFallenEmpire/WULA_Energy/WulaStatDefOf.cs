using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    [DefOf]
    public static class WulaStatDefOf
    {
        public static StatDef WulaEnergyMaxLevelOffset;
        public static StatDef WulaEnergyFallRateFactor;

        static WulaStatDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(WulaStatDefOf));
        }
    }
}
