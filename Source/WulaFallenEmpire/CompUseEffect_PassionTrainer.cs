using RimWorld;
using Verse;
using System.Collections.Generic;
using System.Linq;

namespace WulaFallenEmpire
{
    public class CompProperties_UseEffect_PassionTrainer : CompProperties_UseEffect
    {
        public SkillDef skill;
        public Passion passionGained = Passion.Major; // 可配置获得的热情等级，默认为大火
        public IntRange passionsLostRange = new IntRange(1, 1); // 可配置失去热情的技能数量范围

        public CompProperties_UseEffect_PassionTrainer()
        {
            compClass = typeof(CompUseEffect_PassionTrainer);
        }
    }

    public class CompUseEffect_PassionTrainer : CompUseEffect
    {
        public CompProperties_UseEffect_PassionTrainer Props => (CompProperties_UseEffect_PassionTrainer)props;

        public override void DoEffect(Pawn usedBy)
        {
            base.DoEffect(usedBy);

            if (usedBy.skills == null || Props.skill == null)
            {
                return;
            }

            // 1. 为指定技能设置热情
            SkillRecord targetSkillRecord = usedBy.skills.GetSkill(Props.skill);
            if (targetSkillRecord != null && !targetSkillRecord.TotallyDisabled)
            {
                if (targetSkillRecord.passion != Props.passionGained)
                {
                    targetSkillRecord.passion = Props.passionGained;
                    Messages.Message("WULA_PassionGained".Translate(usedBy.LabelShort, targetSkillRecord.def.label), usedBy, MessageTypeDefOf.PositiveEvent);
                }
            }

            // 2. 确定要移除的热情数量
            int numToLose = Props.passionsLostRange.RandomInRange;
            if (numToLose <= 0)
            {
                return; // 如果随机到0或更少，则不移除任何热情
            }

            // 3. 找到所有其他拥有热情的技能
            List<SkillRecord> skillsWithPassion = usedBy.skills.skills
                .Where(s => s.def != Props.skill && s.passion > Passion.None && !s.TotallyDisabled)
                .ToList();
            
            skillsWithPassion.Shuffle(); // 打乱列表以实现随机性

            // 4. 移除指定数量技能的热情
            int passionsRemoved = 0;
            foreach (var skillToLosePassion in skillsWithPassion)
            {
                if (passionsRemoved >= numToLose) break;

                skillToLosePassion.passion = Passion.None;
                Messages.Message("WULA_PassionLost".Translate(usedBy.LabelShort, skillToLosePassion.def.label), usedBy, MessageTypeDefOf.NegativeEvent);
                passionsRemoved++;
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
            if (p.skills.GetSkill(Props.skill).TotallyDisabled)
            {
                return "SkillDisabled".Translate();
            }
            return base.CanBeUsedBy(p);
        }
    }
}
