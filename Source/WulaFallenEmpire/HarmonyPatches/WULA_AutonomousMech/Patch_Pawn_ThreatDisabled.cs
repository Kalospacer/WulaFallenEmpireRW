using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using System.Collections.Generic;

namespace WulaFallenEmpire
{
    // 核心补丁：修复威胁禁用检查
    [HarmonyPatch(typeof(Pawn), "ThreatDisabled")]
    public static class Patch_Pawn_ThreatDisabled
    {
        public static void Postfix(Pawn __instance, ref bool __result)
        {
            // 如果已经判定为无威胁，检查是否有CompAutonomousMech组件
            if (__result && __instance.GetComp<CompAutonomousMech>() != null)
            {
                __result = false; // 强制设置为有威胁
            }
        }
    }

    // 核心补丁：修复机械师需求检查 - 正确的方法在 MechanitorUtility 中
    [HarmonyPatch(typeof(MechanitorUtility), "IsColonyMechRequiringMechanitor")]
    public static class Patch_MechanitorUtility_IsColonyMechRequiringMechanitor
    {
        public static void Postfix(Pawn mech, ref bool __result)
        {
            if (__result && mech.IsColonyMech)
            {
                var comp = mech.GetComp<CompAutonomousMech>();
                if (comp != null && comp.CanFightAutonomously)
                {
                    __result = false;
                }
            }
        }
    }
}
