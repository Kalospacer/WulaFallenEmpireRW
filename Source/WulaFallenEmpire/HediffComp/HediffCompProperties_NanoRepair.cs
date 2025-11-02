using RimWorld;
using Verse;
using System.Collections.Generic;
using UnityEngine;

namespace WulaFallenEmpire
{
    public class HediffCompProperties_NanoRepair : HediffCompProperties
    {
        public float activeSeverity = 0.5f;      // 有能量且损伤时的严重性
        public float inactiveSeverity = 1.5f;    // 其他情况的严重性
        public float minEnergyThreshold = 0.1f;  // 最低能量阈值
        public float repairCostPerHP = 0.1f;     // 每点生命值修复的能量消耗
        public int repairCooldownAfterDamage = 300; // 受到伤害后的修复冷却时间
        public float repairTolerance = 0f;     // 修复容忍度，低于此值认为已完全修复

        public HediffCompProperties_NanoRepair()
        {
            compClass = typeof(HediffComp_NanoRepair);
        }
    }

    public class HediffComp_NanoRepair : HediffComp
    {
        public HediffCompProperties_NanoRepair Props => (HediffCompProperties_NanoRepair)props;
        
        private int lastDamageTick = -9999;
        private const int CheckInterval = 60;
        private int debugCounter = 0;
        private bool repairSystemEnabled = true; // 默认开启修复系统

        // 获取可用的能量源
        private Need GetEnergyNeed()
        {
            if (Pawn?.needs == null) return null;

            // 优先尝试 WULA_Energy
            var wulaEnergy = Pawn.needs.TryGetNeed(DefDatabase<NeedDef>.GetNamedSilentFail("WULA_Energy"));
            if (wulaEnergy != null)
            {
                if (debugCounter % 10 == 0)
                    Log.Message($"[NanoRepair] 使用 WULA_Energy 作为能量源");
                return wulaEnergy;
            }

            // 回退到 MechEnergy
            var mechEnergy = Pawn.needs?.TryGetNeed<Need_MechEnergy>();
            if (mechEnergy != null)
            {
                if (debugCounter % 10 == 0)
                    Log.Message($"[NanoRepair] 使用 MechEnergy 作为能量源");
                return mechEnergy;
            }

            if (debugCounter % 10 == 0)
                Log.Message($"[NanoRepair] 没有找到可用的能量源");
            return null;
        }

        // 获取能量源名称（用于显示）
        private string GetEnergyNeedName()
        {
            var energyNeed = GetEnergyNeed();
            if (energyNeed == null) return "Unknown";

            if (energyNeed.def.defName == "WULA_Energy")
                return "WULA Energy";
            else if (energyNeed is Need_MechEnergy)
                return "Mech Energy";
            else
                return energyNeed.def.defName;
        }

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);

            // 如果修复系统关闭，跳过所有修复逻辑
            if (!repairSystemEnabled)
            {
                // 如果系统关闭，设置为不活跃状态
                if (parent.Severity != Props.inactiveSeverity)
                {
                    parent.Severity = Props.inactiveSeverity;
                    if (debugCounter % 10 == 0)
                        Log.Message($"[NanoRepair] 修复系统已关闭，设置为不活跃状态");
                }
                return;
            }

            // 每60 ticks检查一次状态
            if (Find.TickManager.TicksGame % CheckInterval == 0)
            {
                debugCounter++;
                if (debugCounter % 10 == 0)
                {
                    Log.Message($"[NanoRepair] Tick {Find.TickManager.TicksGame} - 开始检查修复状态");
                }
                UpdateSeverityAndRepair();
            }
        }

        private void UpdateSeverityAndRepair()
        {
            if (Pawn == null || Pawn.Dead)
            {
                if (debugCounter % 10 == 0)
                    Log.Message($"[NanoRepair] Pawn为null或已死亡");
                return;
            }

            bool shouldBeActive = ShouldBeActive();
            float targetSeverity = shouldBeActive ? Props.activeSeverity : Props.inactiveSeverity;

            // 更新严重性
            if (parent.Severity != targetSeverity)
            {
                parent.Severity = targetSeverity;
                if (debugCounter % 10 == 0)
                    Log.Message($"[NanoRepair] 更新严重性: {parent.Severity} -> {targetSeverity}");
            }

            // 如果处于活跃状态，执行修复
            if (shouldBeActive)
            {
                if (debugCounter % 10 == 0)
                    Log.Message($"[NanoRepair] 系统活跃，尝试修复损伤");
                TryRepairDamage();
            }
            else
            {
                if (debugCounter % 10 == 0)
                    Log.Message($"[NanoRepair] 系统不活跃，跳过修复");
            }
        }

        private bool ShouldBeActive()
        {
            // 如果修复系统关闭，直接返回不活跃
            if (!repairSystemEnabled)
            {
                if (debugCounter % 10 == 0)
                    Log.Message($"[NanoRepair] 修复系统已关闭，系统不活跃");
                return false;
            }

            // 检查能量
            var energyNeed = GetEnergyNeed();
            if (energyNeed == null)
            {
                if (debugCounter % 10 == 0)
                    Log.Message($"[NanoRepair] 没有能量需求，系统不活跃");
                return false;
            }

            if (energyNeed.CurLevelPercentage < Props.minEnergyThreshold)
            {
                if (debugCounter % 10 == 0)
                    Log.Message($"[NanoRepair] 能量不足: {energyNeed.CurLevelPercentage:P0} < {Props.minEnergyThreshold:P0}，系统不活跃");
                return false;
            }

            // 检查是否在冷却期内
            int cooldownRemaining = Props.repairCooldownAfterDamage - (Find.TickManager.TicksGame - lastDamageTick);
            if (cooldownRemaining > 0)
            {
                if (debugCounter % 10 == 0)
                    Log.Message($"[NanoRepair] 冷却中: {cooldownRemaining} ticks剩余，系统不活跃");
                return false;
            }

            // 检查是否有需要修复的损伤
            if (!HasDamageToRepair())
            {
                if (debugCounter % 10 == 0)
                    Log.Message($"[NanoRepair] 没有需要修复的损伤，系统不活跃");
                return false;
            }

            if (debugCounter % 10 == 0)
                Log.Message($"[NanoRepair] 所有条件满足，系统活跃");
            return true;
        }

        private bool HasDamageToRepair()
        {
            if (Pawn.health == null || Pawn.health.hediffSet == null)
            {
                if (debugCounter % 10 == 0)
                    Log.Message($"[NanoRepair] Health或HediffSet为null");
                return false;
            }

            // 检查是否有缺失部件
            var missingParts = Pawn.health.hediffSet.GetMissingPartsCommonAncestors();
            if (missingParts.Count > 0)
            {
                if (debugCounter % 10 == 0)
                    Log.Message($"[NanoRepair] 检测到缺失部件: {missingParts.Count}个");
                return true;
            }

            // 使用 GetPartHealth 检查是否有损伤
            if (HasDamagedParts())
            {
                if (debugCounter % 10 == 0)
                    Log.Message($"[NanoRepair] 检测到损伤部位");
                return true;
            }

            // 检查是否有需要治疗的疾病
            if (Pawn.health.hediffSet.HasTendableNonInjuryNonMissingPartHediff())
            {
                if (debugCounter % 10 == 0)
                    Log.Message($"[NanoRepair] 检测到可治疗疾病");
                return true;
            }

            if (debugCounter % 10 == 0)
                Log.Message($"[NanoRepair] 没有检测到任何需要修复的损伤");
            return false;
        }

        // 使用 GetPartHealth 检测损伤
        private bool HasDamagedParts()
        {
            var bodyParts = Pawn.RaceProps.body.AllParts;
            int damagedCount = 0;
            
            foreach (var part in bodyParts)
            {
                // 如果部位不是缺失的，但健康值小于最大健康值，说明有损伤
                if (!Pawn.health.hediffSet.PartIsMissing(part))
                {
                    float maxHealth = part.def.GetMaxHealth(Pawn);
                    float currentHealth = Pawn.health.hediffSet.GetPartHealth(part);
                    
                    // 使用修复容忍度，只有当损伤大于容忍度时才认为需要修复
                    if (currentHealth < maxHealth - Props.repairTolerance)
                    {
                        damagedCount++;
                        if (debugCounter % 10 == 0 && damagedCount == 1)
                            Log.Message($"[NanoRepair] 部位 {part.def.defName} 有损伤: {currentHealth}/{maxHealth} (容忍度: {Props.repairTolerance})");
                    }
                }
            }
            
            if (debugCounter % 10 == 0 && damagedCount > 0)
                Log.Message($"[NanoRepair] 总共检测到 {damagedCount} 个损伤部位");
                
            return damagedCount > 0;
        }

        // 获取所有需要修复的部位
        private List<BodyPartRecord> GetDamagedParts()
        {
            var damagedParts = new List<BodyPartRecord>();
            var bodyParts = Pawn.RaceProps.body.AllParts;
            
            foreach (var part in bodyParts)
            {
                if (!Pawn.health.hediffSet.PartIsMissing(part))
                {
                    float maxHealth = part.def.GetMaxHealth(Pawn);
                    float currentHealth = Pawn.health.hediffSet.GetPartHealth(part);
                    
                    // 使用修复容忍度
                    if (currentHealth < maxHealth - Props.repairTolerance)
                    {
                        damagedParts.Add(part);
                        if (debugCounter % 10 == 0 && damagedParts.Count <= 3)
                            Log.Message($"[NanoRepair] 损伤部位: {part.def.defName} ({currentHealth}/{maxHealth})");
                    }
                }
            }
            
            if (debugCounter % 10 == 0 && damagedParts.Count > 3)
                Log.Message($"[NanoRepair] ... 还有 {damagedParts.Count - 3} 个损伤部位");
                
            return damagedParts;
        }

        private void TryRepairDamage()
        {
            var energyNeed = GetEnergyNeed();
            if (energyNeed == null)
            {
                if (debugCounter % 10 == 0)
                    Log.Message($"[NanoRepair] 能量需求为null，无法修复");
                return;
            }

            if (debugCounter % 10 == 0)
                Log.Message($"[NanoRepair] 当前能量({GetEnergyNeedName()}): {energyNeed.CurLevel:F1}/{energyNeed.MaxLevel:F1} ({energyNeed.CurLevelPercentage:P0})");

            // 优先修复缺失部件
            if (TryRepairMissingParts(energyNeed))
            {
                if (debugCounter % 10 == 0)
                    Log.Message($"[NanoRepair] 已修复缺失部件");
                return;
            }

            // 然后修复损伤
            if (TryRepairDamagedParts(energyNeed))
            {
                if (debugCounter % 10 == 0)
                    Log.Message($"[NanoRepair] 已修复损伤部位");
                return;
            }

            // 最后修复疾病
            if (TryRepairDiseases(energyNeed))
            {
                if (debugCounter % 10 == 0)
                    Log.Message($"[NanoRepair] 已修复疾病");
                return;
            }

            if (debugCounter % 10 == 0)
                Log.Message($"[NanoRepair] 没有执行任何修复");
        }

        private bool TryRepairMissingParts(Need energyNeed)
        {
            var missingParts = Pawn.health.hediffSet.GetMissingPartsCommonAncestors();
            if (missingParts == null || missingParts.Count == 0)
            {
                if (debugCounter % 10 == 0)
                    Log.Message($"[NanoRepair] 没有缺失部件需要修复");
                return false;
            }

            if (debugCounter % 10 == 0)
                Log.Message($"[NanoRepair] 检查修复缺失部件，共有 {missingParts.Count} 个");

            // 选择最小的缺失部件进行修复（成本较低）
            Hediff_MissingPart partToRepair = null;
            float minHealth = float.MaxValue;

            foreach (var missingPart in missingParts)
            {
                float partHealth = missingPart.Part.def.GetMaxHealth(Pawn);
                if (partHealth < minHealth)
                {
                    minHealth = partHealth;
                    partToRepair = missingPart;
                }
            }

            if (partToRepair != null)
            {
                // 计算修复成本 - 使用正常损伤的修复成本
                float repairCost = minHealth * Props.repairCostPerHP;
                
                // 根据机械族的能量消耗属性调整成本
                var mechEnergyLoss = Pawn.GetStatValue(StatDefOf.MechEnergyLossPerHP);
                if (mechEnergyLoss > 0)
                {
                    repairCost *= mechEnergyLoss;
                }
                
                if (debugCounter % 10 == 0)
                    Log.Message($"[NanoRepair] 尝试修复缺失部件 {partToRepair.Part.def.defName}, 成本: {repairCost:F2}, 当前能量: {energyNeed.CurLevel:F2}");
                
                if (energyNeed.CurLevel >= repairCost)
                {
                    if (ConvertMissingPartToInjury(partToRepair, repairCost))
                    {
                        energyNeed.CurLevel -= repairCost;
                        if (debugCounter % 10 == 0)
                            Log.Message($"[NanoRepair] 成功将缺失部件 {partToRepair.Part.def.defName} 转换为损伤, 消耗能量: {repairCost:F2}");
                        return true;
                    }
                }
                else
                {
                    if (debugCounter % 10 == 0)
                        Log.Message($"[NanoRepair] 能量不足修复缺失部件: {energyNeed.CurLevel:F2} < {repairCost:F2}");
                }
            }
            return false;
        }

        private bool TryRepairDamagedParts(Need energyNeed)
        {
            var damagedParts = GetDamagedParts();
            if (damagedParts.Count == 0)
            {
                if (debugCounter % 10 == 0)
                    Log.Message($"[NanoRepair] 没有损伤部位需要修复");
                return false;
            }

            if (debugCounter % 10 == 0)
                Log.Message($"[NanoRepair] 检查修复损伤部位，共有 {damagedParts.Count} 个");

            // 选择健康值最低的部位进行修复
            BodyPartRecord partToRepair = null;
            float minHealthRatio = float.MaxValue;

            foreach (var part in damagedParts)
            {
                float maxHealth = part.def.GetMaxHealth(Pawn);
                float currentHealth = Pawn.health.hediffSet.GetPartHealth(part);
                float healthRatio = currentHealth / maxHealth;
                
                if (healthRatio < minHealthRatio)
                {
                    minHealthRatio = healthRatio;
                    partToRepair = part;
                }
            }

            if (partToRepair != null)
            {
                float maxHealth = partToRepair.def.GetMaxHealth(Pawn);
                float currentHealth = Pawn.health.hediffSet.GetPartHealth(partToRepair);
                float healthToRepair = maxHealth - currentHealth;
                
                // 计算修复成本
                float repairCost = healthToRepair * Props.repairCostPerHP;
                
                // 根据机械族的能量消耗属性调整成本
                var mechEnergyLoss = Pawn.GetStatValue(StatDefOf.MechEnergyLossPerHP);
                if (mechEnergyLoss > 0)
                {
                    repairCost *= mechEnergyLoss;
                }
                
                if (debugCounter % 10 == 0)
                    Log.Message($"[NanoRepair] 尝试修复部位 {partToRepair.def.defName}, 健康: {currentHealth:F1}/{maxHealth:F1}, 修复量: {healthToRepair:F1}, 成本: {repairCost:F2}");
                
                if (energyNeed.CurLevel >= repairCost)
                {
                    if (RepairDamagedPart(partToRepair, repairCost))
                    {
                        energyNeed.CurLevel -= repairCost;
                        if (debugCounter % 10 == 0)
                            Log.Message($"[NanoRepair] 成功修复部位 {partToRepair.def.defName}, 消耗能量: {repairCost:F2}");
                        return true;
                    }
                }
                else
                {
                    if (debugCounter % 10 == 0)
                        Log.Message($"[NanoRepair] 能量不足修复损伤: {energyNeed.CurLevel:F2} < {repairCost:F2}");
                }
            }
            return false;
        }

        private bool TryRepairDiseases(Need energyNeed)
        {
            var diseases = Pawn.health.hediffSet.GetTendableNonInjuryNonMissingPartHediffs();
            if (diseases == null)
            {
                if (debugCounter % 10 == 0)
                    Log.Message($"[NanoRepair] 没有可治疗疾病");
                return false;
            }

            int diseaseCount = 0;
            foreach (var disease in diseases)
            {
                if (disease.TendableNow())
                    diseaseCount++;
            }

            if (diseaseCount == 0)
            {
                if (debugCounter % 10 == 0)
                    Log.Message($"[NanoRepair] 没有可治疗的疾病");
                return false;
            }

            if (debugCounter % 10 == 0)
                Log.Message($"[NanoRepair] 检查修复疾病，共有 {diseaseCount} 个");

            foreach (var disease in diseases)
            {
                if (disease.TendableNow())
                {
                    float repairCost = CalculateRepairCost(disease);
                    
                    if (debugCounter % 10 == 0)
                        Log.Message($"[NanoRepair] 尝试修复疾病 {disease.def.defName}, 严重性: {disease.Severity:F2}, 成本: {repairCost:F2}, 当前能量: {energyNeed.CurLevel:F2}");
                    
                    if (energyNeed.CurLevel >= repairCost)
                    {
                        if (RepairDisease(disease, repairCost))
                        {
                            energyNeed.CurLevel -= repairCost;
                            if (debugCounter % 10 == 0)
                                Log.Message($"[NanoRepair] 成功修复疾病 {disease.def.defName}, 消耗能量: {repairCost:F2}");
                            return true;
                        }
                    }
                    else
                    {
                        if (debugCounter % 10 == 0)
                            Log.Message($"[NanoRepair] 能量不足修复疾病: {energyNeed.CurLevel:F2} < {repairCost:F2}");
                    }
                }
            }
            return false;
        }

        private float CalculateRepairCost(Hediff hediff)
        {
            float baseCost = Props.repairCostPerHP;
            
            // 根据机械族的能量消耗属性调整成本
            var mechEnergyLoss = Pawn.GetStatValue(StatDefOf.MechEnergyLossPerHP);
            if (mechEnergyLoss > 0)
            {
                baseCost *= mechEnergyLoss;
            }

            float severityMultiplier = 1.0f;

            if (hediff is Hediff_Injury injury)
            {
                severityMultiplier = injury.Severity;
            }
            else if (hediff is Hediff_MissingPart)
            {
                // 缺失部件修复成本使用正常计算
                severityMultiplier = 1.0f;
            }
            else
            {
                severityMultiplier = hediff.Severity * 1.5f;
            }

            float finalCost = baseCost * severityMultiplier;
            
            if (debugCounter % 10 == 0)
                Log.Message($"[NanoRepair] 计算修复成本: 基础={Props.repairCostPerHP}, 机械族能耗系数={mechEnergyLoss}, 严重性系数={severityMultiplier}, 最终成本={finalCost:F2}");
                
            return finalCost;
        }

        private bool RepairDamagedPart(BodyPartRecord part, float repairCost)
        {
            try
            {
                float maxHealth = part.def.GetMaxHealth(Pawn);
                float currentHealth = Pawn.health.hediffSet.GetPartHealth(part);
                float healthToRepair = Mathf.Min(maxHealth - currentHealth, repairCost / Props.repairCostPerHP);
                
                if (debugCounter % 10 == 0)
                    Log.Message($"[NanoRepair] 开始修复部位 {part.def.defName}, 计划修复量: {healthToRepair:F1}");
                
                // 直接修复部位的健康值，而不是查找特定的hediff
                // 找到该部位的所有hediff并尝试修复
                var hediffsOnPart = new List<Hediff>();
                foreach (var hediff in Pawn.health.hediffSet.hediffs)
                {
                    if (hediff.Part == part)
                    {
                        hediffsOnPart.Add(hediff);
                        if (debugCounter % 10 == 0)
                            Log.Message($"[NanoRepair] 部位 {part.def.defName} 上的hediff: {hediff.def.defName}, 类型: {hediff.GetType()}, 严重性: {hediff.Severity}");
                    }
                }
                
                if (hediffsOnPart.Count == 0)
                {
                    if (debugCounter % 10 == 0)
                        Log.Message($"[NanoRepair] 部位 {part.def.defName} 上没有找到任何hediff");
                    return false;
                }
                
                if (debugCounter % 10 == 0)
                    Log.Message($"[NanoRepair] 在部位 {part.def.defName} 上找到 {hediffsOnPart.Count} 个hediff");
                
                // 按严重性排序，先修复最严重的hediff
                hediffsOnPart.Sort((a, b) => b.Severity.CompareTo(a.Severity));
                
                float remainingRepair = healthToRepair;
                bool anyRepairDone = false;
                
                foreach (var hediff in hediffsOnPart)
                {
                    if (remainingRepair <= 0)
                        break;
                        
                    // 检查hediff是否可修复
                    if (!CanRepairHediff(hediff))
                    {
                        if (debugCounter % 10 == 0)
                            Log.Message($"[NanoRepair] 跳过不可修复的hediff: {hediff.def.defName}");
                        continue;
                    }
                        
                    float healAmount = Mathf.Min(hediff.Severity, remainingRepair);
                    hediff.Severity -= healAmount;
                    remainingRepair -= healAmount;
                    anyRepairDone = true;
                    
                    if (debugCounter % 10 == 0)
                        Log.Message($"[NanoRepair] 修复hediff {hediff.def.defName}, 修复量: {healAmount:F1}, 剩余严重性: {hediff.Severity:F1}");
                    
                    // 使用修复容忍度，只有当严重性大于容忍度时才移除hediff
                    if (hediff.Severity <= Props.repairTolerance)
                    {
                        Pawn.health.RemoveHediff(hediff);
                        if (debugCounter % 10 == 0)
                            Log.Message($"[NanoRepair] hediff {hediff.def.defName} 已完全修复并移除");
                    }
                }
                
                if (debugCounter % 10 == 0)
                    Log.Message($"[NanoRepair] 部位 {part.def.defName} 修复完成，总修复量: {healthToRepair - remainingRepair:F1}");
                
                return anyRepairDone;
            }
            catch (System.Exception ex)
            {
                Log.Error($"[NanoRepair] 修复部位 {part.def.defName} 时出错: {ex}");
                return false;
            }
        }

        // 检查hediff是否可修复
        private bool CanRepairHediff(Hediff hediff)
        {
            // 如果是机械族特有的hediff，总是可以修复
            if (IsMechSpecificHediff(hediff))
                return true;
            
            // 如果是可治疗的hediff，可以修复
            if (hediff.TendableNow())
                return true;
            
            // 如果是损伤类型的hediff，可以修复
            if (hediff is Hediff_Injury)
                return true;
            
            // 其他情况不可修复
            return false;
        }

        // 将缺失部件转换为指定的hediff
        private bool ConvertMissingPartToInjury(Hediff_MissingPart missingPart, float repairCost)
        {
            try
            {
                if (debugCounter % 10 == 0)
                    Log.Message($"[NanoRepair] 开始将缺失部件 {missingPart.Part.def.defName} 转换为损伤");

                float partMaxHealth = missingPart.Part.def.GetMaxHealth(Pawn);
                
                // 关键修复：确保转换后的损伤不会导致部位再次缺失
                // 我们设置损伤严重性为最大健康值-1，这样部位健康值至少为1
                float injurySeverity = partMaxHealth - 1;
                
                // 如果最大健康值为1，则设置为0.5，确保部位健康值大于0
                if (partMaxHealth <= 1)
                {
                    injurySeverity = 0.5f;
                }
                
                // 移除缺失部件hediff
                Pawn.health.RemoveHediff(missingPart);
                
                // 添加指定的hediff (Crush)
                HediffDef injuryDef = DefDatabase<HediffDef>.GetNamedSilentFail("Crush");
                if (injuryDef == null)
                {
                    Log.Error($"[NanoRepair] 找不到指定的hediff定义: Crush");
                    return false;
                }
                
                // 创建损伤
                Hediff injury = HediffMaker.MakeHediff(injuryDef, Pawn, missingPart.Part);
                injury.Severity = injurySeverity;
                
                Pawn.health.AddHediff(injury);
                
                if (debugCounter % 10 == 0)
                    Log.Message($"[NanoRepair] 成功将缺失部件 {missingPart.Part.def.defName} 转换为 {injuryDef.defName} 损伤, 严重性: {injurySeverity} (最大健康值: {partMaxHealth})");
                
                return true;
            }
            catch (System.Exception ex)
            {
                Log.Error($"[NanoRepair] 转换缺失部件 {missingPart.Part.def.defName} 时出错: {ex}");
                return false;
            }
        }

        // 检查是否是机械族特有的hediff（可能不可治疗但需要修复）
        private bool IsMechSpecificHediff(Hediff hediff)
        {
            // 机械族的损伤可能不是标准的Hediff_Injury
            // 这里可以根据需要添加更多机械族特有的hediff判断
            return hediff.def.defName.Contains("Mech") || 
                   hediff.def.defName.Contains("Mechanical") ||
                   hediff.def.defName.Contains("Gunshot"); // 包括枪伤
        }

        private bool RepairDisease(Hediff disease, float repairCost)
        {
            try
            {
                // 修复疾病
                float healAmount = Mathf.Min(disease.Severity, repairCost / (Props.repairCostPerHP * 1.5f));
                disease.Severity -= healAmount;
                
                if (debugCounter % 10 == 0)
                    Log.Message($"[NanoRepair] 修复疾病 {disease.def.defName}, 修复量: {healAmount:F2}, 剩余严重性: {disease.Severity:F2}");
                
                // 使用修复容忍度
                if (disease.Severity <= Props.repairTolerance)
                {
                    Pawn.health.RemoveHediff(disease);
                    if (debugCounter % 10 == 0)
                        Log.Message($"[NanoRepair] 疾病 {disease.def.defName} 已完全修复并移除");
                }
                return true;
            }
            catch (System.Exception ex)
            {
                Log.Error($"[NanoRepair] 修复疾病 {disease.def.defName} 时出错: {ex}");
                return false;
            }
        }

        public override void Notify_PawnPostApplyDamage(DamageInfo dinfo, float totalDamageDealt)
        {
            base.Notify_PawnPostApplyDamage(dinfo, totalDamageDealt);
            
            // 记录最后一次受到伤害的时间
            lastDamageTick = Find.TickManager.TicksGame;
            Log.Message($"[NanoRepair] 受到伤害，开始修复冷却: {lastDamageTick}");
        }

        // 添加Gizmo（小工具）用于切换修复系统
        public override IEnumerable<Gizmo> CompGetGizmos()
        {
            // 只有玩家派系的机械族才显示Gizmo
            if (Pawn.Faction == Faction.OfPlayer)
            {
                Command_Toggle toggleCommand = new Command_Toggle
                {
                    defaultLabel = repairSystemEnabled ? "WULA_NanoRepair_Disable".Translate() : "WULA_NanoRepair_Enable".Translate(),
                    defaultDesc = repairSystemEnabled ? "WULA_NanoRepair_DisableDesc".Translate() : "WULA_NanoRepair_EnableDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("Wula/UI/Commands/WULA_NanoRepairHediff_Switch"),
                    isActive = () => repairSystemEnabled,
                    toggleAction = () => {
                        repairSystemEnabled = !repairSystemEnabled;
                        Messages.Message(
                            repairSystemEnabled ? 
                            "WULA_NanoRepair_EnabledMsg".Translate(Pawn.LabelShort) : 
                            "WULA_NanoRepair_DisabledMsg".Translate(Pawn.LabelShort), 
                            MessageTypeDefOf.SilentInput
                        );
                        Log.Message($"[NanoRepair] 修复系统已{(repairSystemEnabled ? "启用" : "禁用")}");
                    },
                    hotKey = KeyBindingDefOf.Misc1
                };

                yield return toggleCommand;
            }
        }

        public override string CompTipStringExtra
        {
            get
            {
                string status = repairSystemEnabled ? 
                    (ShouldBeActive() ? 
                        "WULA_NanoRepair_Active".Translate() : 
                        "WULA_NanoRepair_Inactive".Translate()) :
                    "WULA_NanoRepair_Disabled".Translate();

                var energyNeed = GetEnergyNeed();

                string damageInfo = HasDamageToRepair() ? 
                    "WULA_DamageDetected".Translate() : 
                    "WULA_NoDamage".Translate();

                string cooldownInfo = "";
                int cooldownRemaining = Props.repairCooldownAfterDamage - (Find.TickManager.TicksGame - lastDamageTick);
                if (cooldownRemaining > 0)
                {
                    cooldownInfo = "\n" + "WULA_RepairCooldown".Translate((cooldownRemaining / 60f).ToString("F1"));
                }

                return status + "\n" + damageInfo + cooldownInfo;
            }
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref lastDamageTick, "lastDamageTick", -9999);
            Scribe_Values.Look(ref repairSystemEnabled, "repairSystemEnabled", true);
        }
    }
}
