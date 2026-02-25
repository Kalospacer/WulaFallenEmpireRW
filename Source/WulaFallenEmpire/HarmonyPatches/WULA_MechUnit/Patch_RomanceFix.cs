// File: Patch_RomanceFix.cs
using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace WulaFallenEmpire
{
    /// <summary>
    /// 修复浪漫关系菜单相关的空引用异常
    /// </summary>
    public static class RomancePatches
    {
        /// <summary>
        /// 补丁：防止对机甲单位显示浪漫菜单
        /// </summary>
        [HarmonyPatch(typeof(FloatMenuOptionProvider_Romance))]
        [HarmonyPatch("GetSingleOptionFor")]
        public static class Patch_FloatMenuOptionProvider_Romance_GetSingleOptionFor
        {
            [HarmonyPrefix]
            public static bool Prefix(Pawn clickedPawn, ref FloatMenuOption __result)
            {
                // 如果是机甲单位，直接返回null
                if (clickedPawn is Wulamechunit)
                {
                    __result = null;
                    return false; // 跳过原始方法
                }
                
                // 额外检查：如果clickedPawn没有story组件，也跳过
                if (clickedPawn?.story == null)
                {
                    __result = null;
                    return false;
                }
                
                return true; // 继续执行原始方法
            }
        }
        
        /// <summary>
        /// 补丁：防止在爱情关系检查中出现空引用
        /// </summary>
        [HarmonyPatch(typeof(LovePartnerRelationUtility))]
        [HarmonyPatch("ExistingLovePartners")]
        public static class Patch_LovePartnerRelationUtility_ExistingLovePartners
        {
            [HarmonyPrefix]
            public static bool Prefix(Pawn pawn, bool allowDead, ref List<DirectPawnRelation> __result)
            {
                // 如果pawn是机甲单位，返回空列表
                if (pawn is Wulamechunit)
                {
                    __result = new List<DirectPawnRelation>();
                    return false; // 跳过原始方法
                }
                
                // 如果pawn没有story组件，返回空列表
                if (pawn?.story == null)
                {
                    __result = new List<DirectPawnRelation>();
                    return false;
                }
                
                return true; // 继续执行原始方法
            }
        }
        
        /// <summary>
        /// 补丁：防止浪漫关系配对检查中的空引用
        /// </summary>
        [HarmonyPatch(typeof(RelationsUtility))]
        [HarmonyPatch("RomanceEligiblePair")]
        public static class Patch_RelationsUtility_RomanceEligiblePair
        {
            [HarmonyPrefix]
            public static bool Prefix(Pawn initiator, Pawn target, bool forOpinionExplanation, ref AcceptanceReport __result)
            {
                // 如果任一pawn是机甲单位，返回拒绝
                if (initiator is Wulamechunit || target is Wulamechunit)
                {
                    __result = new AcceptanceReport("WULA_MechCannotRomance".Translate());
                    return false; // 跳过原始方法
                }
                
                // 如果任一pawn没有story组件，返回拒绝
                if (initiator?.story == null || target?.story == null)
                {
                    __result = new AcceptanceReport("WULA_NoStoryComponent".Translate());
                    return false;
                }
                
                return true; // 继续执行原始方法
            }
        }
        
        /// <summary>
        /// 补丁：防止浪漫关系检查中的空引用
        /// </summary>
        [HarmonyPatch(typeof(RelationsUtility))]
        [HarmonyPatch("RomanceOption")]
        public static class Patch_RelationsUtility_RomanceOption
        {
            [HarmonyPrefix]
            public static bool Prefix(Pawn initiator, Pawn romanceTarget, ref FloatMenuOption option, ref float chance, ref bool __result)
            {
                // 如果任一pawn是机甲单位，返回false
                if (initiator is Wulamechunit || romanceTarget is Wulamechunit)
                {
                    __result = false;
                    option = null;
                    chance = 0f;
                    return false; // 跳过原始方法
                }
                
                // 如果任一pawn没有story组件，返回false
                if (initiator?.story == null || romanceTarget?.story == null)
                {
                    __result = false;
                    option = null;
                    chance = 0f;
                    return false;
                }
                
                return true; // 继续执行原始方法
            }
        }
    }
}
