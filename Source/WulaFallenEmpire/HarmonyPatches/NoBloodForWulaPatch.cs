using HarmonyLib;
using Verse;
using System.Reflection;

namespace WulaFallenEmpire
{
    [HarmonyPatch(typeof(Hediff_Injury), "get_BleedRate")]
    public static class NoBloodForWula_BleedRate_Patch
    {
        public static void Postfix(Hediff_Injury __instance, ref float __result)
        {
            if (__instance.pawn.def.defName == "WulaSpecies")
            {
                __result = 0f;
            }
        }
    }
}