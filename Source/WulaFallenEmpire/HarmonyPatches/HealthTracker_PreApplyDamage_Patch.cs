using HarmonyLib;
using RimWorld;
using Verse;

namespace WulaFallenEmpire.HarmonyPatches
{
    [HarmonyPatch(typeof(Pawn_HealthTracker), "PreApplyDamage")]
    public static class HealthTracker_PreApplyDamage_Patch
    {
        /// <summary>
        /// 在伤害应用到Pawn之前执行的补丁。
        /// </summary>
        /// <param name="dinfo">伤害信息，可以被修改。</param>
        /// <param name="absorbed">输出参数，如果伤害被完全吸收则为true。</param>
        public static void Prefix(ref DamageInfo dinfo, out bool absorbed)
        {
            // 必须为out参数赋默认值
            absorbed = false;

            // 检查伤害来源是否是一个Pawn
            Pawn instigatorPawn = dinfo.Instigator as Pawn;
            if (instigatorPawn == null)
            {
                return;
            }

            // 检查这个Pawn是否装备了武器
            if (instigatorPawn.equipment?.Primary == null)
            {
                return;
            }

            // 检查武器上是否有我们的心灵增幅组件
            var psychicComp = instigatorPawn.equipment.Primary.GetComp<CompPsychicScaling>();
            if (psychicComp == null)
            {
                return;
            }

            // 获取心灵敏感度属性值
            float psychicSensitivity = instigatorPawn.GetStatValue(StatDefOf.PsychicSensitivity);

            // 根据心灵敏感度是否大于100%，使用不同的计算逻辑
            float damageMultiplier;
            if (psychicSensitivity > 1f)
            {
                // 增伤：伤害会根据XML中的增伤系数获得额外加成
                damageMultiplier = 1 + ((psychicSensitivity - 1) * psychicComp.Props.damageMultiplierPerSensitivityPoint);
            }
            else if (psychicSensitivity < 1f)
            {
                // 减伤：伤害会根据XML中的减伤系数降低
                damageMultiplier = 1 - ((1 - psychicSensitivity) * psychicComp.Props.damageReductionMultiplierPerSensitivityPoint);
            }
            else
            {
                // 敏感度正好为100%，伤害不变
                damageMultiplier = 1f;
            }

            // 获取当前伤害值并应用乘数
            float originalAmount = dinfo.Amount;
            float newAmount = originalAmount * damageMultiplier;

            // 更新伤害信息中的伤害值
            dinfo.SetAmount(newAmount);
        }
    }
}