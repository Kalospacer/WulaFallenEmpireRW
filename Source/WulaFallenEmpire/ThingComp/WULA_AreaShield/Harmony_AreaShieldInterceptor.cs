using HarmonyLib;
using RimWorld;
using Verse;
using UnityEngine;

namespace WulaFallenEmpire
{
    [HarmonyPatch(typeof(Projectile), "CheckForFreeInterceptBetween")]
    public static class Projectile_CheckForFreeInterceptBetween_Patch
    {
        public static bool Prefix(Projectile __instance, Vector3 lastExactPos, Vector3 newExactPos)
        {
            if (__instance.Map == null || __instance.Destroyed)
            {
                return true;
            }

            bool shouldDestroy = false;

            // 使用缓存系统获取激活的护盾
            foreach (var shield in AreaShieldManager.GetActiveShieldsForMap(__instance.Map))
            {
                if (shield?.TryIntercept(__instance, lastExactPos, newExactPos) == true)
                {
                    shouldDestroy = true;
                    break; // 只要有一个护盾吸收就销毁
                }
                // 如果护盾反射了抛射体，继续检查其他护盾（允许多重反射）
            }

            if (shouldDestroy)
            {
                __instance.Destroy(DestroyMode.Vanish);
                return false;
            }

            return true;
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
