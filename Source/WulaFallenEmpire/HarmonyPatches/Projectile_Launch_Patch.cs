using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire.HarmonyPatches
{
    [HarmonyPatch(typeof(Projectile), "Impact")]
    public static class Projectile_Impact_Patch
    {
        private static Dictionary<Projectile, int> bounceCount = new Dictionary<Projectile, int>();

        [HarmonyPrefix]
        public static bool Prefix(Projectile __instance, Thing hitThing)
        {
            try
            {
                if (__instance.Destroyed || !__instance.Spawned || hitThing == null)
                    return true;

                // 检查抛射体是否击中了穿戴护盾的 pawn
                if (hitThing is Pawn hitPawn)
                {
                    // 获取 pawn 身上的所有拦截护盾
                    var interceptors = GetInterceptorsOnPawn(hitPawn);
                    
                    foreach (var interceptor in interceptors)
                    {
                        if (interceptor.TryInterceptProjectile(__instance, hitThing))
                        {
                            // 记录反弹次数
                            int currentBounces = bounceCount.TryGetValue(__instance, 0) + 1;
                            bounceCount[__instance] = currentBounces;

                            // 检查最大反弹次数
                            if (currentBounces >= interceptor.Props.maxBounces)
                            {
                                __instance.Destroy();
                                return false;
                            }

                            // 拦截成功，阻止原版 Impact 逻辑
                            return false;
                        }
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"Error in Projectile_Impact_Patch: {ex}");
                return true;
            }
        }

        private static List<CompApparelInterceptor> GetInterceptorsOnPawn(Pawn pawn)
        {
            var result = new List<CompApparelInterceptor>();
            
            if (pawn == null || pawn.apparel == null)
                return result;

            try
            {
                foreach (var apparel in pawn.apparel.WornApparel)
                {
                    var interceptor = apparel.GetComp<CompApparelInterceptor>();
                    if (interceptor != null && interceptor.Active)
                    {
                        result.Add(interceptor);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error in GetInterceptorsOnPawn: {ex}");
            }

            return result;
        }

        // 清理反弹计数 - 修复：使用 Thing 的 Destroy 方法
        [HarmonyPatch(typeof(Thing), "Destroy")]
        public static class Thing_Destroy_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Thing __instance)
            {
                if (__instance is Projectile projectile)
                {
                    bounceCount.Remove(projectile);
                }
            }
        }
    }
}
