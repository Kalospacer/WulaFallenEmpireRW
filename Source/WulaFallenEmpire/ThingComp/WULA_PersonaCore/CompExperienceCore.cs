using RimWorld;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    public class CompExperienceCore : ThingComp
    {
        // 当前经验值
        private float currentExperience;
        
        // 当前品质
        private QualityCategory currentQuality = QualityCategory.Normal;
        
        // 当前装备者
        private Pawn equippedPawn;
        
        // 上次检查时的技能经验
        private float lastSkillExperience;
        
        // 是否已初始化品质
        private bool qualityInitialized;
        
        public CompProperties_ExperienceCore Props => (CompProperties_ExperienceCore)props;
        
        public float CurrentExperience => currentExperience;
        public QualityCategory CurrentQuality => currentQuality;
        
        // 获取下一个品质阈值
        public ExperienceThreshold NextThreshold
        {
            get
            {
                var thresholds = Props.experienceThresholds;
                for (int i = 0; i < thresholds.Count; i++)
                {
                    if (currentExperience < thresholds[i].experienceRequired)
                        return thresholds[i];
                }
                return null;
            }
        }
        
        // 获取当前品质对应的经验阈值
        public ExperienceThreshold CurrentThreshold
        {
            get
            {
                var thresholds = Props.experienceThresholds;
                for (int i = thresholds.Count - 1; i >= 0; i--)
                {
                    if (currentExperience >= thresholds[i].experienceRequired)
                        return thresholds[i];
                }
                return null;
            }
        }

        public override void Initialize(CompProperties props)
        {
            base.Initialize(props);
            
            // 如果武器已经有品质，使用现有品质
            var qualityComp = parent.TryGetComp<CompQuality>();
            if (qualityComp != null && !qualityInitialized)
            {
                currentQuality = qualityComp.Quality;
                qualityInitialized = true;
            }
        }

        public override void Notify_Equipped(Pawn pawn)
        {
            base.Notify_Equipped(pawn);
            
            equippedPawn = pawn;
            
            // 记录当前技能经验
            if (pawn.skills != null && Props.trackedSkill != null)
            {
                var skill = pawn.skills.GetSkill(Props.trackedSkill);
                if (skill != null)
                {
                    lastSkillExperience = skill.XpTotalEarned;
                }
            }
            
            Log.Message($"[ExperienceCore] {parent.Label} equipped by {pawn.Name}, tracking {Props.trackedSkill?.defName}");
        }

        public override void Notify_Unequipped(Pawn pawn)
        {
            base.Notify_Unequipped(pawn);
            
            equippedPawn = null;
            lastSkillExperience = 0f;
            
            Log.Message($"[ExperienceCore] {parent.Label} unequipped from {pawn.Name}");
        }

        public override void CompTick()
        {
            base.CompTick();
            
            // 每60tick检查一次经验变化（约1秒）
            if (Find.TickManager.TicksGame % 60 == 0 && equippedPawn != null)
            {
                UpdateExperience();
            }
        }

        private void UpdateExperience()
        {
            if (equippedPawn?.skills == null || Props.trackedSkill == null)
                return;
                
            var skill = equippedPawn.skills.GetSkill(Props.trackedSkill);
            if (skill == null)
                return;
            
            // 计算获得的经验
            float currentSkillExperience = skill.XpTotalEarned;
            float gainedExperience = currentSkillExperience - lastSkillExperience;
            
            if (gainedExperience > 0)
            {
                // 应用倍率
                float actualGained = gainedExperience * Props.experienceMultiplier;
                currentExperience += actualGained;
                
                Log.Message($"[ExperienceCore] {parent.Label} gained {actualGained:F1} experience (from {gainedExperience:F1} skill exp)");
                
                // 检查品质升级
                CheckForQualityUpgrade();
                
                // 更新记录的经验值
                lastSkillExperience = currentSkillExperience;
            }
        }

        private void CheckForQualityUpgrade()
        {
            var nextThreshold = NextThreshold;
            if (nextThreshold != null && currentExperience >= nextThreshold.experienceRequired)
            {
                UpgradeQuality(nextThreshold);
            }
        }

        private void UpgradeQuality(ExperienceThreshold threshold)
        {
            var oldQuality = currentQuality;
            currentQuality = threshold.quality;
            
            // 更新武器的品质组件
            var qualityComp = parent.TryGetComp<CompQuality>();
            if (qualityComp != null)
            {
                qualityComp.SetQuality(threshold.quality, ArtGenerationContext.Outsider);
            }
            
            // 发送升级消息
            if (!threshold.messageKey.NullOrEmpty())
            {
                Messages.Message(threshold.messageKey.Translate(parent.Label, threshold.quality.GetLabel()), 
                               parent, MessageTypeDefOf.PositiveEvent);
            }
            else
            {
                Messages.Message("WULA_WeaponUpgraded".Translate(parent.Label, threshold.quality.GetLabel()), 
                               parent, MessageTypeDefOf.PositiveEvent);
            }
            
            Log.Message($"[ExperienceCore] {parent.Label} upgraded from {oldQuality} to {threshold.quality}");
        }

        public override string CompInspectStringExtra()
        {
            if (!Props.showExperienceInfo)
                return null;
                
            StringBuilder sb = new StringBuilder();
            
            // 当前经验
            sb.AppendLine("WULA_CurrentExperience".Translate(currentExperience.ToString("F0")));
            
            // 下一个品质阈值
            var nextThreshold = NextThreshold;
            if (nextThreshold != null)
            {
                float progress = currentExperience / nextThreshold.experienceRequired;
                sb.AppendLine("WULA_NextQualityProgress".Translate(
                    nextThreshold.quality.GetLabel(), 
                    progress.ToStringPercent()
                ));
            }
            else
            {
                sb.AppendLine("WULA_MaxQuality".Translate(currentQuality.GetLabel()));
            }
            
            // 追踪的技能
            if (Props.trackedSkill != null)
            {
                sb.Append("WULA_TrackedSkill".Translate(Props.trackedSkill.LabelCap));
            }
            
            return sb.ToString().TrimEndNewlines();
        }

        public override IEnumerable<StatDrawEntry> SpecialDisplayStats()
        {
            if (Props.showExperienceInfo)
            {
                yield return new StatDrawEntry(
                    StatCategoryDefOf.Basics,
                    "WULA_CurrentExperience".Translate(),
                    currentExperience.ToString("F0"),
                    "WULA_CurrentExperienceDesc".Translate(),
                    2100
                );
                
                var nextThreshold = NextThreshold;
                if (nextThreshold != null)
                {
                    float progress = currentExperience / nextThreshold.experienceRequired;
                    yield return new StatDrawEntry(
                        StatCategoryDefOf.Basics,
                        "WULA_NextQuality".Translate(),
                        $"{nextThreshold.quality.GetLabel()} ({progress.ToStringPercent()})",
                        "WULA_NextQualityDesc".Translate(nextThreshold.experienceRequired),
                        2099
                    );
                }
                
                yield return new StatDrawEntry(
                    StatCategoryDefOf.Basics,
                    "WULA_CurrentQuality".Translate(),
                    currentQuality.GetLabel(),
                    "WULA_CurrentQualityDesc".Translate(),
                    2098
                );
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            
            Scribe_Values.Look(ref currentExperience, "currentExperience", 0f);
            Scribe_Values.Look(ref currentQuality, "currentQuality", QualityCategory.Normal);
            Scribe_Values.Look(ref qualityInitialized, "qualityInitialized", false);
            Scribe_Values.Look(ref lastSkillExperience, "lastSkillExperience", 0f);
            Scribe_References.Look(ref equippedPawn, "equippedPawn");
        }
    }
}
