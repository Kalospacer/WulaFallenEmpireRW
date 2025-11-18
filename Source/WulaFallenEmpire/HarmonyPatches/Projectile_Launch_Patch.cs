using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire.HarmonyPatches
{
    [HarmonyPatch(typeof(Projectile), "CheckForFreeInterceptBetween")]
    public static class Projectile_CheckForFreeInterceptBetween_Patch
    {
        public static bool Prefix(Projectile __instance, Vector3 lastExactPos, Vector3 newExactPos)
        {
            try
            {
                if (__instance == null || __instance.Map == null || __instance.Destroyed || !__instance.Spawned)
                    return true;

                var map = __instance.Map;
                var pawns = map.mapPawns?.AllPawnsSpawned;
                if (pawns == null) return true;

                foreach (Pawn pawn in pawns)
                {
                    if (pawn == null || !pawn.Spawned || pawn.Dead || pawn.Downed || pawn.apparel == null)
                        continue;

                    foreach (Apparel apparel in pawn.apparel.WornApparel)
                    {
                        if (apparel?.TryGetComp<CompApparelInterceptor>() is CompApparelInterceptor interceptor)
                        {
                            try
                            {
                                if (interceptor.TryIntercept(__instance, lastExactPos, newExactPos))
                                {
                                    // 简单直接：立即销毁子弹
                                    if (!__instance.Destroyed && __instance.Spawned)
                                    {
                                        __instance.Destroy(DestroyMode.Vanish);
                                    }
                                    return false;
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.Warning($"[Interceptor] Error: {ex.Message}");
                            }
                        }
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[Interceptor] Critical error: {ex}");
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(Projectile), "Tick")]
    public static class Projectile_Tick_Patch
    {
        public static bool Prefix(Projectile __instance)
        {
            return __instance != null && !__instance.Destroyed && __instance.Spawned;
        }
    }

    [HarmonyPatch(typeof(Projectile), "TickInterval")]
    public static class Projectile_TickInterval_Patch
    {
        public static bool Prefix(Projectile __instance, int delta)
        {
            return __instance != null && !__instance.Destroyed && __instance.Spawned;
        }
    }
}
