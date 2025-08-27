using HarmonyLib;
using Verse;
using RimWorld;

namespace WulaFallenEmpire
{
    [HarmonyPatch(typeof(Building_ArmedShuttle), "get_CanSetForcedTarget")]
    public static class Patch_Building_ArmedShuttle_CanSetForcedTarget
    {
        /// <summary>
        /// Postfix patch to allow armed shuttles with CompForceTargetable to be manually targeted.
        /// </summary>
        public static void Postfix(Building_ArmedShuttle __instance, ref bool __result)
        {
            // If the result is already true, no need to do anything.
            if (__result)
            {
                return;
            }

            // Check if the shuttle has our marker component and belongs to the player.
            if (__instance.GetComp<CompForceTargetable>() != null && __instance.Faction == Faction.OfPlayer)
            {
                __result = true;
            }
        }
    }
}