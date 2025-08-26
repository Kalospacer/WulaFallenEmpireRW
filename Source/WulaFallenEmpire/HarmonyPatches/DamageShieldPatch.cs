using HarmonyLib;
using Verse;
using System.Reflection;
using UnityEngine; // Add UnityEngine for MoteMaker and Color

namespace WulaFallenEmpire.HarmonyPatches
{
    [HarmonyPatch(typeof(Pawn_HealthTracker), "PreApplyDamage")]
    public static class DamageShieldPatch
    {
        // 使用 Harmony 的 AccessTools.Field 来获取私有的 pawn 字段
        private static readonly FieldInfo PawnField = AccessTools.Field(typeof(Pawn_HealthTracker), "pawn");

        public static bool Prefix(Pawn_HealthTracker __instance, ref DamageInfo dinfo, out bool absorbed)
        {
            // 获取 Pawn 实例
            Pawn pawn = (Pawn)PawnField.GetValue(__instance);

            // 查找 Pawn 身上是否有 Hediff_DamageShield
            Hediff_DamageShield damageShield = pawn.health.hediffSet.GetFirstHediff<Hediff_DamageShield>();

            if (damageShield != null && damageShield.ShieldCharges > 0)
            {
                // 如果有护盾层数，则消耗一层并抵挡伤害
                damageShield.ShieldCharges--;
                // MoteMaker.ThrowText(pawn.DrawPos, pawn.Map, "伤害被护盾抵挡!", Color.cyan, 1.2f); // 视觉反馈，明确指定 Verse.MoteMaker，此行将被删除

                // 设置 absorbed 为 true，表示伤害被完全吸收
                absorbed = true;

                // 返回 false，阻止原始方法执行，即伤害不会被应用
                return false;
            }

            // 如果没有护盾 Hediff 或者层数用尽，则正常处理伤害
            absorbed = false;
            return true; // 继续执行原始方法
        }
    }
}