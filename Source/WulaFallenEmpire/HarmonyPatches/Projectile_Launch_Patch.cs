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

                                ImpactMethod.Invoke(__instance, new object[] { null, true });

                                return false;
                            }
                        }
                    }
                }
            }

            return true;
        }
    }
}