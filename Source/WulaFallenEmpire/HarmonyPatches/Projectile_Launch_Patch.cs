using HarmonyLib;
using RimWorld;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire.HarmonyPatches
{
    [HarmonyPatch(typeof(Projectile), "CheckForFreeInterceptBetween")]
    public static class Projectile_CheckForFreeInterceptBetween_Patch
    {
        private static readonly MethodInfo ImpactMethod = AccessTools.Method(typeof(Projectile), "Impact");

        public static bool Prefix(Projectile __instance, Vector3 lastExactPos, Vector3 newExactPos)
        {
            if (__instance.Map == null || __instance.Destroyed) return true;

            foreach (Pawn pawn in __instance.Map.mapPawns.AllPawnsSpawned)
            {
                if (pawn.apparel != null)
                {
                    foreach (Apparel apparel in pawn.apparel.WornApparel)
                    {
                        if (apparel.TryGetComp<CompApparelInterceptor>(out var interceptor))
                        {
                            if (interceptor.TryIntercept(__instance, lastExactPos, newExactPos))
                            {
                                // Directly destroy the projectile instead of calling Impact via reflection.
                                // This is cleaner and avoids the NRE that happens when the game engine
                                // continues to process a projectile that was destroyed mid-tick.
                                __instance.Destroy(DestroyMode.Vanish);

                                return false; // Prevent original method from running.
                            }
                        }
                    }
                }
            }

            return true;
        }
    }
}