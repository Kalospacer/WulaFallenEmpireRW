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
        
        // 是否已初始化
        private bool initialized = false;
        
        // 当前阈值索引
        private int currentThresholdIndex = 0;
        
        // 溢出的经验（超过最大阈值的部分）
        private float overflowExperience = 0f;
        
        public CompProperties_ExperienceCore Props => (CompProperties_ExperienceCore)props;
        
        public float CurrentExperience => currentExperience;
        public QualityCategory CurrentQuality => currentQuality;
        public float OverflowExperience => overflowExperience;
        
        // 获取最大经验阈值（最后一个阈值）
        public float MaxExperienceThreshold
        {
            get
            {
                if (Props.experienceThresholds == null || Props.experienceThresholds.Count == 0)
                    return float.MaxValue;
                    
                return Props.experienceThresholds[Props.experienceThresholds.Count - 1].experienceRequired;
            }
        }
        
        // 获取总经验（当前经验 + 溢出经验）
        public float TotalExperience => currentExperience + overflowExperience;
        
        // 检查是否已达到最大品质
        public bool HasReachedMaxQuality => currentThresholdIndex >= Props.experienceThresholds.Count;
        
        // 获取下一个品质阈值
        public ExperienceThreshold NextThreshold
        {
            get
            {
                if (Props.experienceThresholds == null || currentThresholdIndex >= Props.experienceThresholds.Count)
                    return null;
                    
                return Props.experienceThresholds[currentThresholdIndex];
            }
        }

        public override void Initialize(CompProperties props)
        {
            base.Initialize(props);
            
            // 初始化当前品质
            var qualityComp = parent.TryGetComp<CompQuality>();
            if (qualityComp != null && !initialized)
            {
                // 如果武器已经有品质，使用现有品质，否则设置为Normal
                currentQuality = qualityComp.Quality;
                initialized = true;
                
                Log.Message($"[ExperienceCore] Initialized {parent.Label} with quality: {currentQuality}");
            }
        }

        // 新增：获取命令按钮
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }

            // 弹出数据包按钮
            if (Props.dataPackDef != null && TotalExperience > 0)
            {
                yield return new Command_Action
                {
                    defaultLabel = "WULA_EjectDataPack".Translate(),
                    defaultDesc = "WULA_EjectDataPackDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("Wula/UI/Commands/WULA_EjectDataPack"),
                    action = EjectDataPack
                };
            }

            // 吸收数据包按钮
            if (Props.dataPackDef != null)
            {
                yield return new Command_Action
                {
                    defaultLabel = "WULA_AbsorbDataPack".Translate(),
                    defaultDesc = "WULA_AbsorbDataPackDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("Wula/UI/Commands/WULA_AbsorbDataPack"),
                    action = AbsorbDataPack
                };
            }
        }

        // 弹出数据包方法
        private void EjectDataPack()
        {
            if (Props.dataPackDef == null)
            {
                Messages.Message("WULA_NoDataPackDef".Translate(), parent, MessageTypeDefOf.RejectInput);
                return;
            }

            if (TotalExperience <= 0)
            {
                Messages.Message("WULA_NoExperienceToEject".Translate(), parent, MessageTypeDefOf.RejectInput);
                return;
            }

            // 创建数据包
            Thing dataPack = ThingMaker.MakeThing(Props.dataPackDef);
            CompExperienceDataPack dataPackComp = dataPack.TryGetComp<CompExperienceDataPack>();
            
            if (dataPackComp != null)
            {
                // 计算要存储的总经验（当前经验 + 溢出经验）
                float experienceToStore = TotalExperience;
                float retainedExperience = TotalExperience * Props.experienceRetentionOnEject;
                
                dataPackComp.storedExperience = experienceToStore;
                dataPackComp.sourceWeaponLabel = parent.Label;
                
                // 生成数据包
                GenPlace.TryPlaceThing(dataPack, parent.Position, parent.Map, ThingPlaceMode.Near);
                
                // 重置武器状态，保留部分经验
                ResetWeaponState(retainedExperience);
                
                Messages.Message("WULA_DataPackEjected".Translate(experienceToStore.ToString("F0")), parent, MessageTypeDefOf.PositiveEvent);
                
                Log.Message($"[ExperienceCore] Ejected data pack with {experienceToStore} experience, retained {retainedExperience}");
            }
            else
            {
                Messages.Message("WULA_DataPackCompMissing".Translate(), parent, MessageTypeDefOf.RejectInput);
            }
        }

        // 吸收数据包方法 - 现在会吸收所有附近的数据包并处理溢出
        private void AbsorbDataPack()
        {
            if (Props.dataPackDef == null)
            {
                Messages.Message("WULA_NoDataPackDef".Translate(), parent, MessageTypeDefOf.RejectInput);
                return;
            }

            // 查找附近的所有数据包
            List<Thing> dataPacks = FindNearbyDataPacks();
            if (dataPacks.Count == 0)
            {
                Messages.Message("WULA_NoDataPackNearby".Translate(), parent, MessageTypeDefOf.RejectInput);
                return;
            }

            // 计算总可吸收经验
            float totalExperienceToAbsorb = 0f;
            foreach (Thing dataPack in dataPacks)
            {
                CompExperienceDataPack dataPackComp = dataPack.TryGetComp<CompExperienceDataPack>();
                if (dataPackComp != null && dataPackComp.storedExperience > 0)
                {
                    totalExperienceToAbsorb += dataPackComp.storedExperience;
                }
            }

            if (totalExperienceToAbsorb <= 0)
            {
                Messages.Message("WULA_DataPackEmpty".Translate(), parent, MessageTypeDefOf.RejectInput);
                return;
            }

            // 计算实际可吸收的经验（不超过最大阈值）
            float actualExperienceToAbsorb;
            float overflowFromAbsorption = 0f;

            if (HasReachedMaxQuality)
            {
                // 如果已经达到最大品质，所有吸收的经验都算作溢出
                actualExperienceToAbsorb = 0f;
                overflowFromAbsorption = totalExperienceToAbsorb;
            }
            else
            {
                float remainingToMax = MaxExperienceThreshold - currentExperience;
                if (totalExperienceToAbsorb <= remainingToMax)
                {
                    // 可以完全吸收
                    actualExperienceToAbsorb = totalExperienceToAbsorb;
                    overflowFromAbsorption = 0f;
                }
                else
                {
                    // 只能吸收部分，其余溢出
                    actualExperienceToAbsorb = remainingToMax;
                    overflowFromAbsorption = totalExperienceToAbsorb - remainingToMax;
                }
            }

            // 应用吸收
            currentExperience += actualExperienceToAbsorb;
            overflowExperience += overflowFromAbsorption;

            // 销毁所有被吸收的数据包
            foreach (Thing dataPack in dataPacks)
            {
                dataPack.Destroy();
            }

            // 检查升级
            if (actualExperienceToAbsorb > 0)
            {
                CheckForQualityUpgrade();
            }

            // 处理溢出经验 - 如果吸收后有溢出，自动创建一个新的数据包
            if (overflowFromAbsorption > 0)
            {
                CreateOverflowDataPack(overflowFromAbsorption);
            }

            // 发送消息
            string messageText;
            if (actualExperienceToAbsorb > 0 && overflowFromAbsorption > 0)
            {
                messageText = "WULA_DataPackPartiallyAbsorbed".Translate(
                    actualExperienceToAbsorb.ToString("F0"), 
                    overflowFromAbsorption.ToString("F0")
                );
            }
            else if (actualExperienceToAbsorb > 0)
            {
                messageText = "WULA_DataPackAbsorbed".Translate(actualExperienceToAbsorb.ToString("F0"));
            }
            else
            {
                messageText = "WULA_DataPackOverflowOnly".Translate(overflowFromAbsorption.ToString("F0"));
            }
            
            Messages.Message(messageText, parent, MessageTypeDefOf.PositiveEvent);
            
            Log.Message($"[ExperienceCore] {parent.Label} absorbed {actualExperienceToAbsorb} experience, overflow: {overflowFromAbsorption}, total: {currentExperience}, overflow total: {overflowExperience}");
        }

        // 创建溢出数据包
        private void CreateOverflowDataPack(float overflowAmount)
        {
            if (Props.dataPackDef == null || overflowAmount <= 0)
                return;

            Thing overflowDataPack = ThingMaker.MakeThing(Props.dataPackDef);
            CompExperienceDataPack dataPackComp = overflowDataPack.TryGetComp<CompExperienceDataPack>();
            
            if (dataPackComp != null)
            {
                dataPackComp.storedExperience = overflowAmount;
                dataPackComp.sourceWeaponLabel = parent.Label + " (Overflow)";
                
                GenPlace.TryPlaceThing(overflowDataPack, parent.Position, parent.Map, ThingPlaceMode.Near);
                
                Log.Message($"[ExperienceCore] Created overflow data pack with {overflowAmount} experience");
            }
        }

        // 查找附近的所有数据包
        private List<Thing> FindNearbyDataPacks()
        {
            List<Thing> foundDataPacks = new List<Thing>();
            
            if (parent.Map == null) 
                return foundDataPacks;

            // 在指定半径范围内查找所有相同类型的数据包
            CellRect searchRect = CellRect.CenteredOn(parent.Position, Props.absorbRadius);
            foreach (IntVec3 cell in searchRect)
            {
                if (cell.InBounds(parent.Map))
                {
                    List<Thing> things = parent.Map.thingGrid.ThingsListAt(cell);
                    foreach (Thing thing in things)
                    {
                        if (thing.def == Props.dataPackDef && 
                            thing.TryGetComp<CompExperienceDataPack>() != null &&
                            !foundDataPacks.Contains(thing))
                        {
                            foundDataPacks.Add(thing);
                        }
                    }
                }
            }
            
            Log.Message($"[ExperienceCore] Found {foundDataPacks.Count} data packs within {Props.absorbRadius} tiles");
            return foundDataPacks;
        }

        // 重置武器状态
        private void ResetWeaponState(float retainedExperience = 0f)
        {
            var oldTotalExperience = TotalExperience;
            
            // 分配保留的经验
            if (retainedExperience <= MaxExperienceThreshold)
            {
                currentExperience = retainedExperience;
                overflowExperience = 0f;
            }
            else
            {
                currentExperience = MaxExperienceThreshold;
                overflowExperience = retainedExperience - MaxExperienceThreshold;
            }
            
            currentQuality = QualityCategory.Normal;
            currentThresholdIndex = 0;
            
            // 更新品质组件
            var qualityComp = parent.TryGetComp<CompQuality>();
            if (qualityComp != null)
            {
                qualityComp.SetQuality(currentQuality, ArtGenerationContext.Outsider);
            }
            
            Log.Message($"[ExperienceCore] {parent.Label} reset from {oldTotalExperience} total experience to {currentExperience} + {overflowExperience} overflow");
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
                    Log.Message($"[ExperienceCore] {parent.Label} equipped by {pawn.Name}, tracking {Props.trackedSkill.defName}, starting experience: {lastSkillExperience}");
                }
            }
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
                
                // 分配经验：优先填充当前经验，超出部分转为溢出经验
                if (HasReachedMaxQuality)
                {
                    // 已经达到最大品质，所有新经验都算作溢出
                    overflowExperience += actualGained;
                }
                else
                {
                    float remainingToMax = MaxExperienceThreshold - currentExperience;
                    if (actualGained <= remainingToMax)
                    {
                        // 可以完全添加到当前经验
                        currentExperience += actualGained;
                    }
                    else
                    {
                        // 部分添加到当前经验，其余转为溢出
                        currentExperience = MaxExperienceThreshold;
                        overflowExperience += (actualGained - remainingToMax);
                    }
                }
                
                Log.Message($"[ExperienceCore] {parent.Label} gained {actualGained:F1} experience (current: {currentExperience:F1}, overflow: {overflowExperience:F1})");
                
                // 检查品质升级
                CheckForQualityUpgrade();
                
                // 更新记录的经验值
                lastSkillExperience = currentSkillExperience;
            }
        }

        private void CheckForQualityUpgrade()
        {
            var nextThreshold = NextThreshold;
            while (nextThreshold != null && currentExperience >= nextThreshold.experienceRequired)
            {
                UpgradeQuality(nextThreshold);
                nextThreshold = NextThreshold; // 获取下一个阈值
            }
        }

        private void UpgradeQuality(ExperienceThreshold threshold)
        {
            var oldQuality = currentQuality;
            currentQuality = threshold.quality;
            currentThresholdIndex++; // 移动到下一个阈值
            
            // 更新武器的品质组件
            var qualityComp = parent.TryGetComp<CompQuality>();
            if (qualityComp != null)
            {
                qualityComp.SetQuality(threshold.quality, ArtGenerationContext.Outsider);
                Log.Message($"[ExperienceCore] SUCCESS: {parent.Label} quality updated to {threshold.quality}");
            }
            else
            {
                Log.Error($"[ExperienceCore] ERROR: {parent.Label} has no CompQuality component!");
                return;
            }
            
            // 发送升级消息
            string messageText;
            if (!threshold.messageKey.NullOrEmpty())
            {
                messageText = threshold.messageKey.Translate(parent.Label, threshold.quality.GetLabel());
            }
            else
            {
                messageText = "WULA_WeaponUpgraded".Translate(parent.Label, threshold.quality.GetLabel());
            }
            
            Messages.Message(messageText, parent, MessageTypeDefOf.PositiveEvent);
            
            Log.Message($"[ExperienceCore] {parent.Label} upgraded from {oldQuality} to {threshold.quality} at {currentExperience} experience");
        }

        public override string CompInspectStringExtra()
        {
            if (!Props.showExperienceInfo)
                return null;
                
            StringBuilder sb = new StringBuilder();
            
            // 当前经验
            sb.AppendLine("WULA_CurrentExperience".Translate(currentExperience.ToString("F0")));
            
            // 溢出经验（如果有）
            if (overflowExperience > 0)
            {
                sb.AppendLine("WULA_OverflowExperience".Translate(overflowExperience.ToString("F0")));
            }
            
            // 下一个品质阈值或最大品质信息
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
                if (overflowExperience > 0)
                {
                    sb.AppendLine("WULA_OverflowStored".Translate());
                }
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
                    "WULA_CurrentExperienceStat".Translate(),
                    currentExperience.ToString("F0"),
                    "WULA_CurrentExperienceDesc".Translate(),
                    2100
                );
                
                // 溢出经验统计
                if (overflowExperience > 0)
                {
                    yield return new StatDrawEntry(
                        StatCategoryDefOf.Basics,
                        "WULA_OverflowExperienceStat".Translate(),
                        overflowExperience.ToString("F0"),
                        "WULA_OverflowExperienceDesc".Translate(),
                        2095
                    );
                }
                
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
            Scribe_Values.Look(ref initialized, "initialized", false);
            Scribe_Values.Look(ref lastSkillExperience, "lastSkillExperience", 0f);
            Scribe_Values.Look(ref currentThresholdIndex, "currentThresholdIndex", 0);
            Scribe_Values.Look(ref overflowExperience, "overflowExperience", 0f);
            Scribe_References.Look(ref equippedPawn, "equippedPawn");
            
            // 修复：加载后重新初始化品质
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                var qualityComp = parent.TryGetComp<CompQuality>();
                if (qualityComp != null && qualityComp.Quality != currentQuality)
                {
                    qualityComp.SetQuality(currentQuality, ArtGenerationContext.Outsider);
                    Log.Message($"[ExperienceCore] PostLoad: Updated {parent.Label} quality to {currentQuality}");
                }
            }
        }
    }
}
