// File: HarmonyPatches/SkillSystemPatches.cs
using HarmonyLib;
using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    /// <summary>
    /// 针对SkillRecord.Interval()的补丁，防止在机甲上出现空引用
    /// </summary>
    [HarmonyPatch(typeof(SkillRecord))]
    [HarmonyPatch("Interval")]
    public static class Patch_SkillRecord_Interval
    {
        [HarmonyPrefix]
        public static bool Prefix(SkillRecord __instance)
        {
            // 额外检查：如果pawn.story为null，也跳过
            if (__instance?.Pawn?.story == null)
            {
                return false; // 跳过原方法
            }

            return true; // 执行原方法
        }
    }

}
