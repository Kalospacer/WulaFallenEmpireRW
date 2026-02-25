// JobDriver_RepairMech.cs
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;
using System.Linq;

namespace WulaFallenEmpire
{
    public class JobDriver_RepairMech : JobDriver
    {
        private const TargetIndex MechInd = TargetIndex.A;
        
        protected int ticksToNextRepair;
        
        protected Pawn Mech => (Pawn)job.GetTarget(TargetIndex.A).Thing;
        
        protected virtual bool Remote => false;
        
        protected CompMechRepairable RepairableComp => Mech?.TryGetComp<CompMechRepairable>();
        
        // 使用配置的修复周期ticks数，并根据MechRepairSpeed调整
        protected int TicksPerRepairCycle
        {
            get
            {
                if (RepairableComp == null)
                    return 120;
                    
                int baseTicks = RepairableComp.Props.ticksPerRepairCycle;
                return Mathf.RoundToInt(baseTicks / pawn.GetStatValue(StatDefOf.MechRepairSpeed));
            }
        }
        
        // 每次修复的HP量
        protected float RepairAmountPerCycle
        {
            get
            {
                if (RepairableComp == null)
                    return 1f;
                    
                return RepairableComp.Props.repairAmountPerCycle;
            }
        }
        
        // 新增：缺失部位修复配置
        protected float MissingPartRepairCostMultiplier => 2f;
        protected HediffDef MissingPartReplacementInjury => HediffDefOf.Misc;
        
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Mech, job, 1, -1, null, errorOnFailed);
        }
        
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOnForbidden(TargetIndex.A);
            this.FailOn(() => !MechRepairable() || !MechNeedsRepair());
            
            if (!Remote)
            {
                yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            }
            
            Toil repairToil = (Remote ? Toils_General.Wait(int.MaxValue) : Toils_General.WaitWith(TargetIndex.A, int.MaxValue, useProgressBar: true, maintainPosture: true, maintainSleep: true));
            
            // 添加维修特效
            if (RepairableComp?.Props.repairEffect != null)
            {
                repairToil.WithEffect(RepairableComp.Props.repairEffect, TargetIndex.A);
            }
            else
            {
                repairToil.WithEffect(EffecterDefOf.MechRepairing, TargetIndex.A);
            }
            
            // 添加维修音效
            if (RepairableComp?.Props.repairSound != null)
            {
                repairToil.PlaySustainerOrSound(RepairableComp.Props.repairSound);
            }
            else
            {
                repairToil.PlaySustainerOrSound(Remote ? SoundDefOf.RepairMech_Remote : SoundDefOf.RepairMech_Touch);
            }
            
            repairToil.AddPreInitAction(delegate
            {
                ticksToNextRepair = TicksPerRepairCycle;
            });
            
            repairToil.handlingFacing = true;
            
            repairToil.tickIntervalAction = delegate(int delta)
            {
                ticksToNextRepair -= delta;
                if (ticksToNextRepair <= 0)
                {
                    RepairTick(delta);
                    ticksToNextRepair = TicksPerRepairCycle;
                }
                pawn.rotationTracker.FaceTarget(Mech);
                if (pawn.skills != null)
                {
                    pawn.skills.Learn(SkillDefOf.Crafting, 0.05f * (float)delta);
                }
            };
            
            repairToil.AddFinishAction(delegate
            {
                // 维修完成后，如果机甲被征召，恢复其工作
                if (Mech.jobs?.curJob != null && job.playerForced)
                {
                    Mech.jobs.EndCurrentJob(JobCondition.InterruptForced);
                }
            });
            
            repairToil.AddEndCondition(() => MechNeedsRepair() ? JobCondition.Ongoing : JobCondition.Succeeded);
            
            if (!Remote)
            {
                repairToil.activeSkill = () => SkillDefOf.Crafting;
            }
            
            yield return repairToil;
        }
        
        private bool MechRepairable()
        {
            return RepairableComp != null;
        }
        
        private bool MechNeedsRepair()
        {
            return RepairableComp?.NeedsRepair ?? false;
        }
        
        private void RepairTick(int delta)
        {
            if (Mech == null || Mech.health == null || Mech.Dead)
                return;
            
            // 计算本次修复的总HP量
            float totalRepairAmount = RepairAmountPerCycle;
            float originalRepairAmount = totalRepairAmount;
            
            // 第一阶段：先修复现有伤口（非缺失部位）
            totalRepairAmount = RepairExistingInjuries(totalRepairAmount);
            
            // 第二阶段：如果还有修复量，并且机甲血量足够安全，再处理缺失部位
            if (totalRepairAmount > 0f && IsSafeToRepairMissingParts())
            {
                // 直接尝试转换缺失部位，不消耗修复量
                TryConvertMissingParts();
            }
            
            // 记录修复统计（只统计实际修复的伤口）
            if (RepairableComp != null && totalRepairAmount < originalRepairAmount)
            {
                RepairableComp.RecordRepair(originalRepairAmount - totalRepairAmount);
            }
        }
        
        // 修复现有伤口（非缺失部位）
        private float RepairExistingInjuries(float totalRepairAmount)
        {
            float remainingAmount = totalRepairAmount;
            
            // 获取所有非缺失部位的伤口
            var existingInjuries = Mech.health.hediffSet.hediffs
                .Where(h => 
                    (h is Hediff_Injury && h.Severity > 0f) || 
                    (h.def.tendable && h.Severity > 0f && !(h is Hediff_MissingPart))
                )
                .OrderByDescending(h => h.Severity) // 优先修复最严重的伤口
                .ToList();
            
            foreach (var injury in existingInjuries)
            {
                if (remainingAmount <= 0f)
                    break;
                    
                if (injury is Hediff_Injury injuryHediff)
                {
                    // 修复伤害
                    float healAmount = Mathf.Min(remainingAmount, injuryHediff.Severity);
                    injuryHediff.Severity -= healAmount;
                    remainingAmount -= healAmount;
                    
                    if (injuryHediff.Severity <= 0f)
                    {
                        Mech.health.RemoveHediff(injuryHediff);
                    }
                }
                else if (injury.def.tendable)
                {
                    // 其他可治疗的hediff
                    float healAmount = Mathf.Min(remainingAmount, injury.Severity);
                    injury.Severity -= healAmount;
                    remainingAmount -= healAmount;
                    
                    if (injury.Severity <= 0f)
                    {
                        Mech.health.RemoveHediff(injury);
                    }
                }
            }
            
            return remainingAmount;
        }
        
        // 检查是否安全可以修复缺失部位
        private bool IsSafeToRepairMissingParts()
        {
            // 获取机甲当前血量百分比
            float currentHealthPercent = Mech.health.summaryHealth.SummaryHealthPercent;
            
            // 如果血量低于30%，不安全修复缺失部位
            if (currentHealthPercent < 0.3f)
                return false;
            
            // 如果有严重伤口（严重性大于5），先修复它们
            bool hasCriticalInjuries = Mech.health.hediffSet.hediffs
                .Any(h => h is Hediff_Injury && h.Severity > 5f);
            
            if (hasCriticalInjuries)
                return false;
            
            // 检查缺失部位转换是否会致命
            if (WouldMissingPartConversionBeFatal())
                return false;
            
            return true;
        }
        
        // 检查缺失部位转换是否会致命
        private bool WouldMissingPartConversionBeFatal()
        {
            // 获取所有缺失部位
            var missingParts = Mech.health.hediffSet.GetMissingPartsCommonAncestors();
            if (!missingParts.Any())
                return false;
            
            // 计算转换后可能增加的总伤害量
            float potentialAddedDamage = 0f;
            
            foreach (var missingPart in missingParts)
            {
                float partMaxHealth = missingPart.Part.def.GetMaxHealth(Mech);
                float injurySeverity = partMaxHealth - 1;
                if (partMaxHealth <= 1)
                    injurySeverity = 0.5f;
                
                potentialAddedDamage += injurySeverity;
            }
            
            // 获取当前总伤害量
            float currentTotalInjurySeverity = Mech.health.hediffSet.hediffs
                .Where(h => h is Hediff_Injury)
                .Sum(h => h.Severity);
            
            // 计算转换后的总伤害量
            float projectedTotalInjurySeverity = currentTotalInjurySeverity + potentialAddedDamage;
            
            // 获取致命伤害阈值
            float lethalDamageThreshold = Mech.health.LethalDamageThreshold;
            
            // 如果转换后的总伤害量超过或接近致命阈值，不安全
            return projectedTotalInjurySeverity >= lethalDamageThreshold * 0.8f;
        }
        
        // 尝试转换缺失部位
        private void TryConvertMissingParts()
        {
            // 获取所有缺失部位
            var missingParts = Mech.health.hediffSet.GetMissingPartsCommonAncestors();
            if (!missingParts.Any())
                return;
            
            // 选择最小的缺失部件进行转换（成本较低）
            Hediff_MissingPart partToRepair = null;
            float minHealth = float.MaxValue;
            
            foreach (var missingPart in missingParts)
            {
                float partHealth = missingPart.Part.def.GetMaxHealth(Mech);
                if (partHealth < minHealth)
                {
                    minHealth = partHealth;
                    partToRepair = missingPart;
                }
            }
            
            if (partToRepair != null)
            {
                // 直接转换缺失部位
                if (ConvertMissingPartToInjury(partToRepair))
                {
                }
            }
        }
        
        // 将缺失部件转换为伤害hediff
        private bool ConvertMissingPartToInjury(Hediff_MissingPart missingPart)
        {
            try
            {
                float partMaxHealth = missingPart.Part.def.GetMaxHealth(Mech);
                
                // 关键修复：确保转换后的损伤不会导致部位再次缺失
                // 设置损伤严重性为最大健康值-1，这样部位健康值至少为1
                float injurySeverity = partMaxHealth - 1;
                
                // 如果最大健康值为1，则设置为0.5，确保部位健康值大于0
                if (partMaxHealth <= 1)
                {
                    injurySeverity = 0.5f;
                }
                
                // 移除缺失部件hediff
                Mech.health.RemoveHediff(missingPart);
                
                // 添加指定的伤害hediff
                HediffDef injuryDef = MissingPartReplacementInjury;
                if (injuryDef == null)
                {
                    Log.Error($"[WULA] 找不到指定的hediff定义: {MissingPartReplacementInjury?.defName ?? "null"}");
                    return false;
                }
                
                // 创建损伤
                Hediff injury = HediffMaker.MakeHediff(injuryDef, Mech, missingPart.Part);
                injury.Severity = injurySeverity;
                
                Mech.health.AddHediff(injury);
                
                return true;
            }
            catch (System.Exception ex)
            {
                Log.Error($"[WULA] 转换缺失部件 {missingPart.Part.def.defName} 时出错: {ex}");
                return false;
            }
        }
        
        // 获取所有可修复的hediff
        private List<Hediff> GetAllRepairableHediffs()
        {
            var repairableHediffs = new List<Hediff>();
            
            if (Mech.health?.hediffSet == null)
                return repairableHediffs;
            
            // 获取所有hediff
            foreach (var hediff in Mech.health.hediffSet.hediffs)
            {
                if (CanRepairHediff(hediff))
                {
                    repairableHediffs.Add(hediff);
                }
            }
            
            return repairableHediffs;
        }
        
        // 检查hediff是否可修复
        private bool CanRepairHediff(Hediff hediff)
        {
            // 缺失部位可以修复
            if (hediff is Hediff_MissingPart)
                return true;
            
            // 伤害可以修复
            if (hediff is Hediff_Injury)
                return true;
            
            // 可治疗的hediff可以修复
            if (hediff.def.tendable && hediff.Severity > 0f)
                return true;
            
            // 跳过疾病
            if (IsDisease(hediff))
                return false;
            
            return false;
        }
        
        // 检查是否是疾病
        private bool IsDisease(Hediff hediff)
        {
            // 常见的疾病类型
            string[] diseaseKeywords = {
                "Disease", "Flu", "Plague", "Infection", "Malaria", 
                "SleepingSickness", "FibrousMechanites", "SensoryMechanites",
                "WoundInfection", "FoodPoisoning", "GutWorms", "MuscleParasites"
            };
            
            foreach (string keyword in diseaseKeywords)
            {
                if (hediff.def.defName.Contains(keyword))
                    return true;
            }
            
            return false;
        }
        
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref ticksToNextRepair, "ticksToNextRepair", 0);
        }
    }
}
