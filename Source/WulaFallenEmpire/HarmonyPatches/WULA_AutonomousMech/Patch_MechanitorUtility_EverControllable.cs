using HarmonyLib;
using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    [HarmonyPatch(typeof(MechanitorUtility), "EverControllable")]
    public static class Patch_MechanitorUtility_EverControllable
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn mech, ref bool __result)
        {
            if (!__result && mech.TryGetComp<CompAutonomousMech>() != null)
            {
                __result = true;
            }
        }
    }
}