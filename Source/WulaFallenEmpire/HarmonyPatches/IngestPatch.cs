using HarmonyLib;
using RimWorld;
using Verse;
using System; // For Type

namespace WulaFallenEmpire
{
    // Patch for WillEat(Pawn p, ThingDef food, ...)
    [HarmonyPatch(typeof(FoodUtility), "WillEat", new Type[] { typeof(Pawn), typeof(ThingDef), typeof(Pawn), typeof(bool), typeof(bool) })]
    public static class IngestPatch_ThingDef
    {
        [HarmonyPrefix]
        public static bool Prefix(Pawn p, ThingDef food, ref bool __result)
        {
            // 检查食物是否是能量核心
            ThingDefExtension_EnergySource ext = food.GetModExtension<ThingDefExtension_EnergySource>();
            if (ext != null)
            {
                // 如果是能量核心
                if (p.def.defName == "WulaSpecies")
                {
                    // 如果是乌拉族，则认为愿意吃
                    __result = true;
                    return false; // 跳过原版方法
                }
                else
                {
                    // 如果不是乌拉族，则不允许吃能量核心
                    __result = false;
                    return false; // 跳过原版方法
                }
            }
            return true; // 继续执行原版方法
        }
    }

    // New Patch for WillEat(Pawn p, Thing food, ...)
    [HarmonyPatch(typeof(FoodUtility), "WillEat", new Type[] { typeof(Pawn), typeof(Thing), typeof(Pawn), typeof(bool), typeof(bool) })]
    public static class IngestPatch_Thing
    {
        [HarmonyPrefix]
        public static bool Prefix(Pawn p, Thing food, ref bool __result)
        {
            // 检查食物是否是能量核心
            ThingDefExtension_EnergySource ext = food.def.GetModExtension<ThingDefExtension_EnergySource>();
            if (ext != null)
            {
                // 如果是能量核心
                if (p.def.defName == "WulaSpecies")
                {
                    // 如果是乌拉族，则认为愿意吃
                    __result = true;
                    return false; // 跳过原版方法
                }
                else
                {
                    // 如果不是乌拉族，则不允许吃能量核心
                    __result = false;
                    return false; // 跳过原版方法
                }
            }
            return true; // 继续执行原版方法
        }
    }

    // Patch for FoodUtility.FoodIsSuitable(Pawn p, ThingDef food)
    [HarmonyPatch(typeof(FoodUtility), "FoodIsSuitable", new Type[] { typeof(Pawn), typeof(ThingDef) })]
    public static class IngestPatch_FoodIsSuitable
    {
        [HarmonyPrefix]
        public static bool Prefix(Pawn p, ThingDef food, ref bool __result)
        {
            // 检查食物是否是能量核心
            ThingDefExtension_EnergySource ext = food.GetModExtension<ThingDefExtension_EnergySource>();
            if (ext != null)
            {
                // 如果是能量核心
                if (p.def.defName == "WulaSpecies")
                {
                    // 如果是乌拉族，则认为食物是合适的
                    __result = true;
                    return false; // 跳过原版方法
                }
                else
                {
                    // 如果不是乌拉族，则认为食物不合适
                    __result = false;
                    return false; // 跳过原版方法
                }
            }
            return true; // 继续执行原版方法
        }
    }
}
