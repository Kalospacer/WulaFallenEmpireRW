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
            // 检查Pawn是否有伤害拦截组件
            var interceptorComp = __instance.TryGetComp<CompDamageInterceptor>();
            if (interceptorComp != null)
            {
                WulaLog.Debug($"[DamageInterceptor] {__instance.LabelShort} 即将受到 {dinfo.Amount} 点伤害，拦截组件激活");
                
                // 让拦截组件处理伤害
                return interceptorComp.PreApplyDamage(ref dinfo);
            }
            
            return true; // 继续正常处理伤害
        }
    }

    [HarmonyPatch(typeof(Pawn), "PostApplyDamage")]
    public static class Patch_Pawn_PostApplyDamage
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn __instance, DamageInfo dinfo, float totalDamageDealt)
        {
            // 记录实际承受的伤害
            var interceptorComp = __instance.TryGetComp<CompDamageInterceptor>();
            if (interceptorComp != null && totalDamageDealt == 0f)
            {
                WulaLog.Debug($"[DamageInterceptor] {__instance.LabelShort} 成功拦截所有伤害，实际承受0点伤害");
            }
        }
    }
}
