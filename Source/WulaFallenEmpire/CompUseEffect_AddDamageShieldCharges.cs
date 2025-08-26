using Verse;
using RimWorld;
using System.Collections.Generic;

namespace WulaFallenEmpire
{
    public class CompUseEffect_AddDamageShieldCharges : CompUseEffect
    {
        public CompProperties_AddDamageShieldCharges Props => (CompProperties_AddDamageShieldCharges)props;

        public override void DoEffect(Pawn user)
        {
            base.DoEffect(user);

            // 获取或添加 Hediff_DamageShield
            Hediff_DamageShield damageShield = user.health.hediffSet.GetFirstHediff<Hediff_DamageShield>();

            if (damageShield == null)
            {
                // 如果没有 Hediff，则添加一个
                damageShield = (Hediff_DamageShield)HediffMaker.MakeHediff(Props.hediffDef, user);
                user.health.AddHediff(damageShield);
                damageShield.ShieldCharges = Props.chargesToAdd; // 设置初始层数
            }
            else
            {
                // 如果已有 Hediff，则增加层数
                damageShield.ShieldCharges += Props.chargesToAdd;
            }

            // 确保层数不超过最大值
            if (damageShield.ShieldCharges > (int)damageShield.def.maxSeverity)
            {
                damageShield.ShieldCharges = (int)damageShield.def.maxSeverity;
            }

            // 发送消息
            Messages.Message("WULA_MessageGainedDamageShieldCharges".Translate(user.LabelShort, Props.chargesToAdd), user, MessageTypeDefOf.PositiveEvent);
        }

        // 修正 CanBeUsedBy 方法签名
        public override AcceptanceReport CanBeUsedBy(Pawn p)
        {
            // 确保只能对活着的 Pawn 使用
            if (p.Dead)
            {
                return "WULA_CannotUseOnDeadPawn".Translate();
            }
            
            // 检查是否已达到最大层数
            Hediff_DamageShield damageShield = p.health.hediffSet.GetFirstHediff<Hediff_DamageShield>();
            if (damageShield != null && damageShield.ShieldCharges >= (int)damageShield.def.maxSeverity)
            {
                return "WULA_DamageShieldMaxChargesReached".Translate();
            }

            return true; // 可以使用
        }

        // 可以在这里添加 GetDescriptionPart() 来显示描述
        public override string GetDescriptionPart()
        {
            return "WULA_DamageShieldChargesDescription".Translate(Props.chargesToAdd);
        }
    }

    public class CompProperties_AddDamageShieldCharges : CompProperties_UseEffect
    {
        public HediffDef hediffDef;
        public int chargesToAdd;

        public CompProperties_AddDamageShieldCharges()
        {
            compClass = typeof(CompUseEffect_AddDamageShieldCharges);
        }
    }
}