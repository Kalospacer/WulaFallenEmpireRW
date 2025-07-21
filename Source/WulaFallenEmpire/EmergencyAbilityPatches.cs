using HarmonyLib;
using RimWorld;
using System; // Added for Type
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Verse;

namespace WulaFallenEmpire
{
    [HarmonyPatch]
    public static class EmergencyAbilityPatches
    {
        // 修复倒地时无法使用能力的问题
        [HarmonyPatch(typeof(Ability), "get_CanCast")]
        [HarmonyPostfix]
        public static void CanCast_Postfix(Ability __instance, ref AcceptanceReport __result)
        {
            if (__instance.def.defName == "WULA_EmergencyEnergyRestore")
            {
                var comp = __instance.CompOfType<CompAbilityEffect_EmergencyEnergyRestore>();
                if (comp != null && comp.Props.requireDowned)
                {
                    if (!__instance.pawn.Downed)
                    {
                        __result = new AcceptanceReport("只能在倒地时使用");
                    }
                    else
                    {
                        __result = true;
                    }
                }
            }
        }

        // 修复倒地时无法显示能力按钮的问题
        [HarmonyPatch(typeof(Pawn_AbilityTracker), "get_AllAbilitiesForReading")]
        [HarmonyPostfix]
        public static void GetAbilitiesForDisplay_Postfix(Pawn_AbilityTracker __instance, ref List<Ability> __result)
        {
            // 检查pawn是否倒地
            if (__instance.pawn.Downed)
            {
                // 添加紧急能量恢复能力，即使pawn倒地
                foreach (Ability ability in __instance.abilities)
                {
                    if (ability.def.defName == "WULA_EmergencyEnergyRestore" && !__result.Contains(ability))
                    {
                        __result.Add(ability);
                    }
                }
            }
        }

        // 修复倒地时无法使用能力的UI限制 - 直接修补Ability.GizmoDisabled方法
        [HarmonyPatch(typeof(Ability), "GizmoDisabled")]
        [HarmonyPostfix]
        public static void Ability_GizmoDisabled_Postfix(Ability __instance, ref bool __result, ref string reason)
        {
            if (__instance.def.defName == "WULA_EmergencyEnergyRestore")
            {
                if (__result)
                {
                    // 检查是否是因为倒地而被禁用
                    if (__instance.pawn.Downed && reason != null && 
                        (reason.Contains("失去知觉") || reason.Contains("unconscious") || reason.Contains("CommandDisabledUnconscious")))
                    {
                        // 对于紧急能量恢复能力，我们允许在倒地时使用
                        __result = false;
                        reason = null;
                    }
                }
            }
        }

        // 额外的安全措施：修复Command_Ability的禁用检查
        [HarmonyPatch(typeof(Command_Ability), "get_Disabled")]
        [HarmonyPostfix]
        public static void Command_Ability_GizmoDisabled_Postfix(Command_Ability __instance, ref bool __result)
        {
            var ability = (Ability)typeof(Command_Ability).GetField("ability", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(__instance);
            if (ability.def.defName == "WULA_EmergencyEnergyRestore")
            {
                if (__result && ability.pawn.Downed)
                {
                    // 对于紧急能量恢复能力，我们允许在倒地时使用
                    __result = false;
                }
            }
        }

        // 新增补丁：检查ApparelPreventsShooting是否阻止了施法
        [HarmonyPatch(typeof(Verb), "ApparelPreventsShooting")]
        [HarmonyPostfix]
        public static void ApparelPreventsShooting_Postfix(Verb __instance, ref bool __result)
        {
            if (__instance is Verb_CastAbility castAbilityVerb && castAbilityVerb.ability?.def.defName == "WULA_EmergencyEnergyRestore")
            {
            }
        }

        // 最终诊断补丁：检查Verb.TryStartCastOn是否被调用
        [HarmonyPatch(typeof(Verb), "TryStartCastOn", new Type[] { typeof(LocalTargetInfo), typeof(LocalTargetInfo), typeof(bool), typeof(bool), typeof(bool), typeof(bool) })]
        [HarmonyPrefix]
        public static void TryStartCastOn_DiagnosticPrefix(Verb __instance, LocalTargetInfo castTarg, LocalTargetInfo destTarg, ref bool __result)
        {
            if (__instance is Verb_CastAbility castAbilityVerb && castAbilityVerb.ability?.def.defName == "WULA_EmergencyEnergyRestore")
            {
            }
        }

        // 诊断补丁：检查Verb_CastAbility.TryCastShot是否被调用
        [HarmonyPatch(typeof(Verb_CastAbility), "TryCastShot")]
        [HarmonyPrefix]
        public static void TryCastShot_DiagnosticPrefix(Verb_CastAbility __instance, ref bool __result)
        {
            if (__instance.ability?.def.defName == "WULA_EmergencyEnergyRestore")
            {
            }
        }
    }
}
