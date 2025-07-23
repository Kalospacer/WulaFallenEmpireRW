using HarmonyLib;
using RimWorld;
using Verse;

namespace WulaFallenEmpire.HarmonyPatches
{
    [HarmonyPatch(typeof(FloatMenuOptionProvider_Ingest), "GetSingleOptionFor")]
    public static class FloatMenuOptionProvider_Ingest_GetSingleOptionFor_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ref FloatMenuOption __result, Thing clickedThing)
        {
            // If the standard "Ingest" option is for our energy core, nullify it.
            // Our custom "摄取能量" option is added by CompUsable and is not affected by this provider.
            if (__result != null && clickedThing != null && clickedThing.def.defName == "WULA_Charge_Cube")
            {
                __result = null;
            }
        }
    }
}
