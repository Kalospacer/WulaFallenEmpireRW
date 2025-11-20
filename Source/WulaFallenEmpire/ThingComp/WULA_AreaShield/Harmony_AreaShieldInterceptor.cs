using HarmonyLib;
using RimWorld;
using Verse;
using UnityEngine;
using System.Collections.Generic;

namespace WulaFallenEmpire
{
    public static class ReflectedProjectileManager
    {
        private static Dictionary<Projectile, int> projectilesToDestroy = new Dictionary<Projectile, int>();
        private const int DESTROY_DELAY_TICKS = 1;

        public static void MarkForDelayedDestroy(Projectile projectile)
        {
            if (projectile != null && !projectile.Destroyed)
            {
                projectilesToDestroy[projectile] = Find.TickManager.TicksGame + DESTROY_DELAY_TICKS;
            }
        }

        public static void Tick()
        {
            var toRemove = new List<Projectile>();
            
            foreach (var kvp in projectilesToDestroy)
            {
                if (kvp.Key == null || kvp.Key.Destroyed || Find.TickManager.TicksGame >= kvp.Value)
                {
                    if (kvp.Key != null && !kvp.Key.Destroyed)
                    {
                        kvp.Key.Destroy(DestroyMode.Vanish);
                    }
                    toRemove.Add(kvp.Key);
                }
            }

            foreach (var projectile in toRemove)
            {
                projectilesToDestroy.Remove(projectile);
            }
        }
        
        // 在 ReflectedProjectileManager 类中添加这个方法
        public static bool IsMarkedForDestroy(Projectile projectile)
        {
            return projectile != null && projectilesToDestroy.ContainsKey(projectile);
        }
    }

    [HarmonyPatch(typeof(Projectile), "CheckForFreeInterceptBetween")]
    public static class Projectile_CheckForFreeInterceptBetween_Patch
    {
        public static bool Prefix(Projectile __instance, Vector3 lastExactPos, Vector3 newExactPos, ref bool __result)
        {
            try
            {
                // 安全检查
                if (__instance == null || __instance.Map == null || __instance.Destroyed)
                {
                    return true; // 继续执行原方法
                }

                bool shouldDestroy = false;
                bool wasReflected = false;

                // 使用缓存系统获取激活的护盾
                foreach (var shield in AreaShieldManager.GetActiveShieldsForMap(__instance.Map))
                {
                    if (shield == null || shield.parent == null || shield.parent.Destroyed)
                        continue;

                    if (shield?.TryIntercept(__instance, lastExactPos, newExactPos) == true)
                    {
                        shouldDestroy = true;
                        break;
                    }
                    
                    // 检查抛射体是否已经被反射（被标记为延迟销毁）
                    if (ReflectedProjectileManager.IsMarkedForDestroy(__instance))
                    {
                        wasReflected = true;
                        break;
                    }
                }

                if (shouldDestroy)
                {
                    __instance.Destroy(DestroyMode.Vanish);
                    __result = true; // 设置结果为 true 表示已被拦截
                    return false; // 跳过原方法
                }
                
                if (wasReflected)
                {
                    __result = false; // 设置结果为 false 表示未被拦截（因为被反射了）
                    return false; // 跳过原方法
                }

                return true; // 继续执行原方法
            }
            catch (System.Exception ex)
            {
                Log.Warning($"AreaShield interception error: {ex}");
                return true; // 出错时继续执行原方法
            }
        }
    }

    // 添加Tick管理器
    [HarmonyPatch(typeof(TickManager), "DoSingleTick")]
    public static class TickManager_DoSingleTick_Patch
    {
        public static void Postfix()
        {
            ReflectedProjectileManager.Tick();
        }
    }

    // 额外的清理补丁
    [HarmonyPatch(typeof(Map), "FinalizeInit")]
    public static class Map_FinalizeInit_Patch
    {
        public static void Postfix()
        {
            AreaShieldManager.Cleanup();
        }
    }
}
