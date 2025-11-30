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
            }
            else
            {
                Messages.Message("WULA_DataPackCompMissing".Translate(), parent, MessageTypeDefOf.RejectInput);
            }
        }
        // 吸收数据包方法 - 修复版本
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
            float totalExperienceAvailable = 0f;
            foreach (Thing dataPack in dataPacks)
            {
                CompExperienceDataPack dataPackComp = dataPack.TryGetComp<CompExperienceDataPack>();
                if (dataPackComp != null && dataPackComp.storedExperience > 0)
                {
                    totalExperienceAvailable += dataPackComp.storedExperience;
                }
            }
            if (totalExperienceAvailable <= 0)
            {
                Messages.Message("WULA_DataPackEmpty".Translate(), parent, MessageTypeDefOf.RejectInput);
                return;
            }
            // 计算实际需要的经验（如果未达到最大品质）
            float experienceNeeded = 0f;
            if (!HasReachedMaxQuality)
            {
                experienceNeeded = MaxExperienceThreshold - currentExperience;
            }
            // 实际吸收的经验量（不超过需要的经验）
            float actualExperienceToAbsorb = Mathf.Min(totalExperienceAvailable, experienceNeeded);

            // 剩余的经验（留在数据包中）
            float remainingExperience = totalExperienceAvailable - actualExperienceToAbsorb;
            // 应用吸收的经验
            if (actualExperienceToAbsorb > 0)
            {
                currentExperience += actualExperienceToAbsorb;
                CheckForQualityUpgrade(); // 检查升级
            }
            // 处理剩余的经验 - 更新数据包而不是创建新的
            ProcessRemainingExperience(dataPacks, remainingExperience);
            // 发送消息 - 修复阵营检查
            SendAbsorptionMessage(actualExperienceToAbsorb, remainingExperience);
        }
        // 处理剩余的经验 - 更新现有数据包
        private void ProcessRemainingExperience(List<Thing> dataPacks, float remainingExperience)
        {
            if (remainingExperience <= 0)
            {
                // 没有剩余经验，销毁所有数据包
                foreach (Thing dataPack in dataPacks)
                {
                    dataPack.Destroy();
                }
                return;
            }
            // 有剩余经验，找到第一个数据包来存储剩余经验
            bool foundDataPackForRemaining = false;

            foreach (Thing dataPack in dataPacks)
            {
                CompExperienceDataPack dataPackComp = dataPack.TryGetComp<CompExperienceDataPack>();
                if (dataPackComp != null)
                {
                    if (!foundDataPackForRemaining)
                    {
                        // 使用第一个数据包存储剩余经验
                        dataPackComp.storedExperience = remainingExperience;
                        dataPackComp.sourceWeaponLabel = parent.Label + " (Remaining)";
                        foundDataPackForRemaining = true;
                    }
                    else
                    {
                        // 销毁其他数据包
                        dataPack.Destroy();
                    }
                }
            }
        }
        // 发送吸收消息 - 修复阵营检查
        private void SendAbsorptionMessage(float absorbedExperience, float remainingExperience)
        {
            // 检查是否应该显示消息给玩家
            bool shouldShowMessage = ShouldShowMessageToPlayer();

            if (!shouldShowMessage) return;
            string messageText;
            if (absorbedExperience > 0 && remainingExperience > 0)
            {
                messageText = "WULA_DataPackPartiallyAbsorbed".Translate(
                    absorbedExperience.ToString("F0"),
                    remainingExperience.ToString("F0")
                );
            }
            else if (absorbedExperience > 0)
            {
                messageText = "WULA_DataPackAbsorbed".Translate(absorbedExperience.ToString("F0"));
            }
            else
            {
                messageText = "WULA_NoExperienceAbsorbed".Translate(remainingExperience.ToString("F0"));
            }

            Messages.Message(messageText, parent, MessageTypeDefOf.PositiveEvent);
        }
        // 检查是否应该显示消息给玩家
        private bool ShouldShowMessageToPlayer()
        {
            // 如果武器被装备，检查装备者的阵营
            if (equippedPawn != null)
            {
                // 只有玩家阵营或者与玩家结盟的阵营才显示消息
                return equippedPawn.Faction == Faction.OfPlayer ||
                       (equippedPawn.Faction != null && equippedPawn.Faction.PlayerRelationKind == FactionRelationKind.Ally);
            }

            // 如果武器没有被装备，检查武器的持有者（比如在库存中）
            Pawn holder = parent.ParentHolder as Pawn;
            if (holder != null)
            {
                return holder.Faction == Faction.OfPlayer ||
                       (holder.Faction != null && holder.Faction.PlayerRelationKind == FactionRelationKind.Ally);
            }

            // 如果武器在地上，检查地图是否为玩家地图
            if (parent.Map != null)
            {
                return parent.Map.IsPlayerHome;
            }

            // 默认不显示
            return false;
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
        }

        public override void Notify_Unequipped(Pawn pawn)
        {
            base.Notify_Unequipped(pawn);
            
            equippedPawn = null;
            lastSkillExperience = 0f;
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
            }
            else
            {
                Log.Error($"[ExperienceCore] ERROR: {parent.Label} has no CompQuality component!");
                return;
            }

            // 发送升级消息 - 添加阵营检查
            if (ShouldShowMessageToPlayer())
            {
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
            }
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
                }
            }
        }
    }
}
