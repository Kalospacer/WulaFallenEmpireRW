using HarmonyLib;
using Verse;
using System.Reflection;

namespace WulaFallenEmpire
{
    [HarmonyPatch(typeof(Pawn_HealthTracker), "get_CanBleed")]
    public static class NoBloodForWula_CanBleed_Patch
    {
        public static void Postfix(Pawn_HealthTracker __instance, ref bool __result)
        {
            // 使用反射获取Pawn_HealthTracker的私有pawn字段
            Pawn pawn = (Pawn)typeof(Pawn_HealthTracker).GetField("pawn", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(__instance);

            if (pawn.def.defName == "WulaSpecies")
            {
                __result = false;
            }
        }
    }

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