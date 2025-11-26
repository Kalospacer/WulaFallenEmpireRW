using HarmonyLib;
using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    [HarmonyPatch(typeof(Pawn), "PostApplyDamage")]
    public static class Patch_Pawn_PostApplyDamage
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn __instance, DamageInfo dinfo, float totalDamageDealt)
        {
            // 检查Pawn是否有伤害转移组件
            var transferComp = __instance.TryGetComp<CompDamageTransfer>();
            if (transferComp != null)
            {
                // 组件会在PostPostApplyDamage中自动处理
                // 这里主要用于调试和日志记录
                Log.Message($"[DamageTransfer] {__instance.LabelShort} 受到 {totalDamageDealt} 点伤害，转移组件已激活");
            }
        }
    }

    [HarmonyPatch(typeof(Pawn), "PreApplyDamage")]
    public static class Patch_Pawn_PreApplyDamage
    {
        [HarmonyPrefix]
        public static bool Prefix(Pawn __instance, ref DamageInfo dinfo, out bool __state)
        {
            __state = false;
            
            // 检查Pawn是否有伤害转移组件
            var transferComp = __instance.TryGetComp<CompDamageTransfer>();
            if (transferComp != null && __instance.Spawned && !__instance.Dead)
            {
                // 这里可以添加预处理逻辑
                // 例如：在某些条件下完全阻止伤害
                __state = true;
            }
            
            return true;
        }

        [HarmonyPostfix]
        public static void Postfix(Pawn __instance, DamageInfo dinfo, bool __state)
        {
            if (__state)
            {
                // 后处理逻辑
                // 例如：记录伤害转移统计
            }
        }
    }
}
