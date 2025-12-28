using HarmonyLib;
using RimWorld;
using Verse;
using System.Reflection;

namespace WulaFallenEmpire
{
    // 修复 Wula 种族尸体可能缺少 CompRottable 导致 ResurrectionUtility.TryResurrectWithSideEffects 崩溃的问题
    [HarmonyPatch]
    public static class ResurrectionCrashFix
    {
        private static MethodInfo TargetMethod()
        {
            return AccessTools.Method(typeof(ResurrectionUtility), "TryResurrectWithSideEffects");
        }

        [HarmonyPrefix]
        public static bool Prefix(Pawn pawn)
        {
            if (pawn == null) return true;

            // 只针对 Wula 种族（防止误伤其他 Pawn）
            if (pawn.def == null || pawn.def.defName != "WulaSpecies")
            {
                return true;
            }

            // 检查尸体是否缺少必要的腐烂组件
            // 原版 TryResurrectWithSideEffects 会无条件访问 corpse.GetComp<CompRottable>().RotProgress
            if (pawn.Corpse != null && pawn.Corpse.GetComp<CompRottable>() == null)
            {
                if (Prefs.DevMode) 
                {
                    WulaLog.Debug($"[WulaFix] Intercepted crash: {pawn.LabelShort} corpse missing CompRottable. Performing safe resurrection.");
                }
                
                // 直接调用不带副作用的复活方法
                ResurrectionUtility.TryResurrect(pawn);
                
                return false; // 阻止原版方法执行
            }

            return true;
        }
    }
}
