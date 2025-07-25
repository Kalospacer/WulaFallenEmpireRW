using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace WulaFallenEmpire
{
    public class MentalState_BrokenPersonality : MentalState
    {
        public override void PostStart(string reason)
        {
            base.PostStart(reason);

            // 发送信件
            if (PawnUtility.ShouldSendNotificationAbout(pawn))
            {
                // 手动实现备用逻辑：如果信件标题(beginLetterLabel)为空，则使用精神状态的通用标签(label)
                string labelText = def.beginLetterLabel;
                if (string.IsNullOrEmpty(labelText))
                {
                    labelText = def.label;
                }
                TaggedString letterLabel = labelText.Formatted(pawn.LabelShort, pawn.Named("PAWN")).CapitalizeFirst();
                TaggedString letterText = def.beginLetter.Formatted(pawn.LabelShort, pawn.Named("PAWN")).CapitalizeFirst();
                if (reason != null)
                {
                    letterText += "\n\n" + reason;
                }
                Find.LetterStack.ReceiveLetter(letterLabel, letterText, LetterDefOf.ThreatBig, pawn);
            }

            var extension = def.GetModExtension<MentalStateDefExtension_BrokenPersonality>();
            if (extension != null)
            {
                bool alreadyBroken = pawn.story.traits.HasTrait(extension.traitToAdd);

                if (!alreadyBroken)
                {
                    // 移除所有技能热情
                    foreach (SkillRecord skill in pawn.skills.skills)
                    {
                        skill.passion = Passion.None;
                    }

                    // 所有技能等级减半
                    foreach (SkillRecord skill in pawn.skills.skills)
                    {
                        int currentLevel = skill.Level;
                        skill.Level = (int)(currentLevel * extension.skillLevelFactor);
                    }
                }
                
                // 改变派系
                Faction newFaction = Find.FactionManager.FirstFactionOfDef(extension.factionToJoin);
                if (newFaction == null)
                {
                    newFaction = Find.FactionManager.FirstFactionOfDef(FactionDefOf.AncientsHostile);
                }

                if (newFaction != null)
                {
                    pawn.SetFaction(newFaction, null);
                }

            }

            // 离开地图
            Lord lord = pawn.GetLord();
            if (lord == null)
            {
                LordJob_ExitMapBest lordJob = new LordJob_ExitMapBest(LocomotionUrgency.Jog, canDig: true, canDefendSelf: true);
                lord = LordMaker.MakeNewLord(pawn.Faction, lordJob, pawn.Map, Gen.YieldSingle(pawn));
            }
            else
            {
                lord.ReceiveMemo("PawnBroken");
            }
            
            // 强制恢复以避免状态无限持续
            this.forceRecoverAfterTicks = 150;
        }

        public override void MentalStateTick(int delta)
        {
            base.MentalStateTick(delta);
            // 确保在下一帧就恢复，因为所有效果都已经应用
            if (age > 0)
            {
                RecoverFromState();
            }
        }
    }
}
