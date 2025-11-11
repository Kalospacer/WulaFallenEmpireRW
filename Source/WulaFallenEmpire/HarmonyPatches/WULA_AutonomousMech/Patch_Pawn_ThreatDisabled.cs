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
        public static void Postfix(Pawn __instance, IAttackTargetSearcher disabledFor, ref bool __result)
        {
            if (!__result) return;
            if (!__instance.IsColonyMech) return;

            var comp = __instance.GetComp<CompAutonomousMech>();
            if (comp != null && comp.CanFightAutonomously)
            {
                __result = false;
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
