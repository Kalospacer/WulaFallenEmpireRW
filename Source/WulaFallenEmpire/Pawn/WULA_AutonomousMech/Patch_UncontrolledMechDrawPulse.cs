using HarmonyLib;
using RimWorld;
using Verse;
using System.Reflection;
using UnityEngine;

namespace WulaFallenEmpire
{
    // 修复红色名字问题 - 直接修补 PawnNameColorUtility.PawnNameColorOf 方法
    [HarmonyPatch(typeof(PawnNameColorUtility), "PawnNameColorOf")]
    public static class Patch_PawnNameColorUtility_PawnNameColorOf
    {
        public static void Postfix(Pawn pawn, ref Color __result)
        {
            if (pawn != null && pawn.IsColonyMech)
            {
                var comp = pawn.GetComp<CompAutonomousMech>();
                if (comp != null && comp.ShouldSuppressUncontrolledWarning)
                {
                    // 使用正常的白色名字
                    __result = Color.white;
                }
            }
        }
    }

    // 修复红光闪烁问题 - 使用 Traverse 访问私有字段
    [HarmonyPatch(typeof(Pawn_PlayerSettings), "get_UncontrolledMechDrawPulse")]
    public static class Patch_Pawn_PlayerSettings_UncontrolledMechDrawPulse
    {
        public static bool Prefix(Pawn_PlayerSettings __instance, ref float __result)
        {
            var traverse = Traverse.Create(__instance);
            Pawn pawn = traverse.Field("pawn").GetValue<Pawn>();
            
            if (pawn != null && pawn.IsColonyMech)
            {
                var comp = pawn.GetComp<CompAutonomousMech>();
                if (comp != null && comp.ShouldSuppressUncontrolledWarning)
                {
                    // 返回 0 来禁用红光脉冲
                    __result = 0f;
                    return false; // 跳过原始方法
                }
            }
            return true;
        }
    }

    // 修复机械族控制状态检查
    [HarmonyPatch(typeof(MechanitorUtility), "IsMechanitorControlled")]
    public static class Patch_MechanitorUtility_IsMechanitorControlled
    {
        public static void Postfix(Pawn mech, ref bool __result)
        {
            if (!__result && mech != null && mech.IsColonyMech)
            {
                var comp = mech.GetComp<CompAutonomousMech>();
                if (comp != null && comp.ShouldSuppressUncontrolledWarning)
                {
                    // 让游戏认为机械族是受控的
                    __result = true;
                }
            }
        }
    }

    // 修复机械族控制状态检查的另一个方法
    [HarmonyPatch(typeof(CompOverseerSubject), "get_IsControlled")]
    public static class Patch_CompOverseerSubject_IsControlled
    {
        public static void Postfix(CompOverseerSubject __instance, ref bool __result)
        {
            Pawn mech = __instance.parent as Pawn;
            if (!__result && mech != null && mech.IsColonyMech)
            {
                var comp = mech.GetComp<CompAutonomousMech>();
                if (comp != null && comp.ShouldSuppressUncontrolledWarning)
                {
                    // 让游戏认为机械族是受控的
                    __result = true;
                }
            }
        }
    }

    // 修复机械族控制状态显示的另一个检查
    [HarmonyPatch(typeof(Pawn), "GetOverseer")]
    public static class Patch_Pawn_GetOverseer
    {
        public static void Postfix(Pawn __instance, ref Pawn __result)
        {
            if (__result == null && __instance.IsColonyMech)
            {
                var comp = __instance.GetComp<CompAutonomousMech>();
                if (comp != null && comp.ShouldSuppressUncontrolledWarning)
                {
                    // 返回一个虚拟的监管者来避免红色显示
                    __result = __instance;
                }
            }
        }
    }

    // 修复机械族控制组提示
    [HarmonyPatch(typeof(CompOverseerSubject), "GetUndraftedControlGroupTip")]
    public static class Patch_CompOverseerSubject_GetUndraftedControlGroupTip
    {
        public static bool Prefix(CompOverseerSubject __instance, ref string __result)
        {
            Pawn mech = __instance.parent as Pawn;
            if (mech != null && mech.IsColonyMech)
            {
                var comp = mech.GetComp<CompAutonomousMech>();
                if (comp != null && comp.ShouldSuppressUncontrolledWarning)
                {
                    // 提供自主状态的提示而不是未受控提示
                    __result = comp.GetAutonomousStatusString() ?? "Autonomous operation";
                    return false; // 跳过原始方法
                }
            }
            return true;
        }
    }

    // 修复机械族控制状态的其他检查
    [HarmonyPatch(typeof(Pawn), "GetInspectString")]
    public static class Patch_Pawn_GetInspectString
    {
        public static void Postfix(Pawn __instance, ref string __result)
        {
            if (__instance.IsColonyMech)
            {
                var comp = __instance.GetComp<CompAutonomousMech>();
                if (comp != null && comp.ShouldSuppressUncontrolledWarning)
                {
                    // 清理任何未受控相关的文本
                    if (__result.Contains("MechUncontrolled") || __result.Contains("uncontrolled"))
                    {
                        string autonomousStatus = comp.GetAutonomousStatusString();
                        if (!string.IsNullOrEmpty(autonomousStatus))
                        {
                            __result = autonomousStatus;
                        }
                        else
                        {
                            __result = __result.Replace("MechUncontrolled", "")
                                              .Replace("uncontrolled", "autonomous");
                        }
                    }
                }
            }
        }
    }

    // 修复机械族控制状态的UI显示
    [HarmonyPatch(typeof(MechanitorUtility), "GetMechControlGroupDesc")]
    public static class Patch_MechanitorUtility_GetMechControlGroupDesc
    {
        public static bool Prefix(Pawn mech, ref string __result)
        {
            if (mech != null && mech.IsColonyMech)
            {
                var comp = mech.GetComp<CompAutonomousMech>();
                if (comp != null && comp.ShouldSuppressUncontrolledWarning)
                {
                    // 提供自主状态描述
                    __result = comp.GetAutonomousStatusString() ?? "Autonomous operation";
                    return false; // 跳过原始方法
                }
            }
            return true;
        }
    }
}
