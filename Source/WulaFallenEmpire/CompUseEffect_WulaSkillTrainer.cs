using RimWorld;
using Verse;
using System.Collections.Generic;

namespace WulaFallenEmpire
{
    public class CompProperties_UseEffect_WulaSkillTrainer : CompProperties_UseEffect
    {
        public SkillDef skill; // 目标技能
        public float xpGainAmount = 50000f; // 目标技能学习量，默认值与原版一致
        public float baseLossAmount; // 非目标技能基础减少量
        public float noPassionLossFactor; // 无火技能减少乘数
        public float minorPassionLossFactor; // 小火技能减少乘数

        public CompProperties_UseEffect_WulaSkillTrainer()
        {
            compClass = typeof(CompUseEffect_WulaSkillTrainer);
        }
    }

    public class CompUseEffect_WulaSkillTrainer : CompUseEffect
    {
        public CompProperties_UseEffect_WulaSkillTrainer Props => (CompProperties_UseEffect_WulaSkillTrainer)props;

        public override void DoEffect(Pawn usedBy)
        {
            base.DoEffect(usedBy);

            if (usedBy.skills == null)
            {
                return;
            }

            // 获取目标技能
            SkillDef targetSkill = Props.skill;

            // 遍历所有技能
            foreach (SkillRecord skillRecord in usedBy.skills.skills)
            {
                if (skillRecord.def == targetSkill)
                {
                    // 目标技能：增加经验
                    skillRecord.Learn(Props.xpGainAmount, true);
                    Messages.Message("WULA_SkillTrainer_TargetSkillGained".Translate(usedBy.LabelShort, skillRecord.def.label), usedBy, MessageTypeDefOf.PositiveEvent);
                }
                else
                {
                    // 非目标技能：减少经验
                    float experienceLoss = Props.baseLossAmount;
                    if (skillRecord.passion == Passion.None)
                    {
                        experienceLoss *= Props.noPassionLossFactor;
                    }
                    else if (skillRecord.passion == Passion.Minor)
                    {
                        experienceLoss *= Props.minorPassionLossFactor;
                    }
                    // 大火的技能掉得最少，保持默认值

                    skillRecord.Learn(-experienceLoss, true); // 减少经验
                    Messages.Message("WULA_SkillTrainer_SkillLoss".Translate(usedBy.LabelShort, skillRecord.def.label), usedBy, MessageTypeDefOf.NegativeEvent);
                }
            }
        }

        public override AcceptanceReport CanBeUsedBy(Pawn p)
        {
            if (p.skills == null)
            {
                return "PawnHasNoSkills".Translate(p.LabelShort);
            }
            if (Props.skill == null)
            {
                return "SkillTrainerHasNoSkill".Translate(parent.LabelShort);
            }
            // 检查目标技能是否被禁用
            if (p.skills.GetSkill(Props.skill).TotallyDisabled)
            {
                return "SkillDisabled".Translate();
            }
            return base.CanBeUsedBy(p);
        }
    }
}
