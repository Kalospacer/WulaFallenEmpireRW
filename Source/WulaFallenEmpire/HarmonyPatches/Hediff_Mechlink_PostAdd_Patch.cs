using HarmonyLib;
using RimWorld;
using Verse;

namespace ArachnaeSwarm
{
    [HarmonyPatch(typeof(Hediff_Mechlink), "PostAdd")]
    public static class Hediff_Mechlink_PostAdd_Patch
    {
        public static bool Prefix(Hediff_Mechlink __instance, DamageInfo? dinfo)
        {
            // 检查 hediff 的 defName 是否是我们要排除的
            if (__instance.def.defName == "WULA_Addons_Antenna_Hediff_Base")
            {
                // 执行基础逻辑但不弹出信件
                if (!ModLister.CheckBiotech("Mechlink"))
                {
                    __instance.pawn.health.RemoveHediff(__instance);
                    return false; // 跳过原始方法
                }
                
                PawnComponentsUtility.AddAndRemoveDynamicComponents(__instance.pawn);
                
                // 不弹出信件，直接返回
                return false; // 跳过原始方法
            }
            
            // 对于其他 hediff，正常执行原始方法
            return true;
        }
    }
}
