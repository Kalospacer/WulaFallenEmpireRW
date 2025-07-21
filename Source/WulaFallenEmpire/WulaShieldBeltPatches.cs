using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    public static class WulaShieldBeltPatches
    {
        // 拦截投射物
        [HarmonyPatch(typeof(Projectile), "CheckForFreeInterceptBetween")]
        [HarmonyPrefix]
        public static bool CheckForFreeInterceptBetween_Prefix(Projectile __instance, Vector3 lastExactPos, Vector3 newExactPos, ref bool __result)
        {
            var map = __instance.Map;
            if (map == null) return true;

            // 检查所有穿戴护盾腰带的pawn
            var pawns = map.mapPawns.AllPawnsSpawned;
            foreach (var pawn in pawns)
            {
                if (pawn.apparel?.WornApparel == null) continue;

                foreach (var apparel in pawn.apparel.WornApparel)
                {
                    var shieldComp = apparel.GetComp<CompWulaShieldBelt>();
                    if (shieldComp != null && shieldComp.CheckIntercept(__instance, lastExactPos, newExactPos))
                    {
                        // 使用反射调用protected方法
                        typeof(Projectile).GetMethod("Impact", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                            .Invoke(__instance, new object[] { null, true });
                        __result = true;
                        return false;
                    }
                }
            }

            return true;
        }

        // 拦截近战攻击 - 使用Harmony的手动补丁方式
        public static void ApplyMeleePatch(Harmony harmony)
        {
            // 获取Thing.TakeDamage方法
            var originalMethod = typeof(Thing).GetMethod("TakeDamage", 
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            
            if (originalMethod != null)
            {
                // 获取我们的前缀方法
                var prefixMethod = typeof(WulaShieldBeltPatches).GetMethod("TakeDamage_Manual_Prefix", 
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                
                // 应用补丁
                harmony.Patch(originalMethod, new HarmonyMethod(prefixMethod));
            }
        }
        
        // 手动补丁方法
        public static bool TakeDamage_Manual_Prefix(Thing __instance, DamageInfo dinfo, ref DamageWorker.DamageResult __result)
        {
            // 只有当实例是Pawn时才执行护盾腰带的逻辑
            if (__instance is Pawn pawn)
            {
                if (pawn.apparel?.WornApparel == null) return true;

                // 检查是否有护盾腰带可以拦截这次攻击
                foreach (var apparel in pawn.apparel.WornApparel)
                {
                    var shieldComp = apparel.GetComp<CompWulaShieldBelt>();
                    if (shieldComp != null && dinfo.Instigator is Pawn attacker)
                    {
                        if (shieldComp.CheckMeleeIntercept(dinfo, attacker))
                        {
                            __result = new DamageWorker.DamageResult();
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        // 为护盾腰带添加投射物拦截器接口支持
        [HarmonyPatch(typeof(CompProjectileInterceptor), "CheckIntercept")]
        [HarmonyPostfix]
        public static void CheckIntercept_Postfix(CompProjectileInterceptor __instance, Projectile projectile, Vector3 lastExactPos, Vector3 newExactPos, ref bool __result)
        {
            if (__result) return; // 如果已经被拦截了就不需要再检查

            // 这个补丁确保我们的护盾系统与原版的投射物拦截系统兼容
        }
    }
}
