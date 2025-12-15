using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using System.Linq;
using System.Collections.Generic;

namespace WulaFallenEmpire.HarmonyPatches
{
    [HarmonyPatch(typeof(CaravanInventoryUtility), "FindShuttle")]
    public static class Patch_CaravanInventoryUtility_FindShuttle
    {
        [HarmonyPostfix]
        public static void Postfix(Caravan caravan, ref Building_PassengerShuttle __result)
        {
            // If the original method already found a PassengerShuttle, no need to do anything.
            if (__result != null)
            {
                return;
            }

            // If original method returned null, try to find our Building_ArmedShuttle
            List<Thing> allInventoryItems = CaravanInventoryUtility.AllInventoryItems(caravan);
            foreach (Thing item in allInventoryItems)
            {
                if (item is Building_ArmedShuttle armedShuttle)
                {
                    WulaLog.Debug($"[WULA] Harmony Patch: Found Building_ArmedShuttle ({armedShuttle.Label}) in caravan inventory. Setting as __result.");
                    // We need to cast our Building_ArmedShuttle to Building_PassengerShuttle
                    // This is safe because Building_ArmedShuttle is designed to be compatible with Building_PassengerShuttle's interface for caravan purposes.
                    __result = (Building_PassengerShuttle)armedShuttle; 
                    return;
                }
            }
        }
    }
}