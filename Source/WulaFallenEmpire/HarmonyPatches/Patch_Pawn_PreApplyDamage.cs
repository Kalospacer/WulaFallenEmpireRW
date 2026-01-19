using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    [HarmonyPatch(typeof(Pawn), "PreApplyDamage")]
    public static class Patch_Pawn_PreApplyDamage
    {
        [HarmonyPrefix]
        public static bool Prefix(Pawn __instance, ref DamageInfo dinfo)
        {
            var interceptorComp = __instance.TryGetComp<CompDamageInterceptor>();
            if (interceptorComp != null)
            {
                WulaLog.Debug($"[DamageInterceptor] {__instance.LabelShort} 即将受到 {dinfo.Amount} 点伤害，拦截组件激活");
                return interceptorComp.PreApplyDamage(ref dinfo);
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(Pawn), "PostApplyDamage")]
    public static class Patch_Pawn_PostApplyDamage
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn __instance, DamageInfo dinfo, float totalDamageDealt)
        {
            try
            {
                var interceptorComp = __instance.TryGetComp<CompDamageInterceptor>();
                if (interceptorComp != null && totalDamageDealt == 0f)
                {
                    WulaLog.Debug($"[DamageInterceptor] {__instance.LabelShort} 成功拦截所有伤害，实际承受0点伤害");
                }
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[DamageInterceptor] Error in PostApplyDamage patch: {ex}");
            }
        }
    }
}
