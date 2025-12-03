using System.Collections.Generic;
using RimWorld;
using Verse;
using System.Linq;
using System.Text;

namespace WulaFallenEmpire
{
    public class DamageWorker_ExtraDamage : DamageWorker
    {
        /// <summary>
        /// 重写伤害应用方法
        /// </summary>
        public override DamageResult Apply(DamageInfo dinfo, Thing victim)
        {
            // 先应用原始伤害
            DamageResult originalResult = base.Apply(dinfo, victim);
            
            // 获取额外伤害扩展
            DamageDef_ExtraDamageExtension extension = 
                dinfo.Def.GetModExtension<DamageDef_ExtraDamageExtension>();
                
            if (extension != null && victim != null && !victim.Destroyed)
            {
                // 应用额外伤害
                var extraDamageResults = ApplyExtraDamages(extension, dinfo, victim, originalResult);
                
                // 如果需要，添加战斗日志
                if (extension.showExtraLog && extraDamageResults.Count > 0)
                {
                    AddCombatLog(extension, dinfo, victim, extraDamageResults);
                }
            }
            
            return originalResult;
        }
        
        /// <summary>
        /// 应用额外伤害
        /// </summary>
        private List<DamageResult> ApplyExtraDamages(
            DamageDef_ExtraDamageExtension extension, 
            DamageInfo originalDinfo, 
            Thing victim, 
            DamageResult originalResult)
        {
            List<DamageResult> results = new List<DamageResult>();
            
            var applicableDamages = extension.GetApplicableExtraDamages(victim, originalDinfo);
            
            foreach (var extraDamage in applicableDamages)
            {
                if (ShouldApplyExtraDamage(extraDamage, originalDinfo, victim))
                {
                    DamageResult result = ApplySingleExtraDamage(extraDamage, originalDinfo, victim);
                    if (result != null)
                    {
                        results.Add(result);
                    }
                }
            }
            
            return results;
        }
        
        /// <summary>
        /// 检查是否应该应用额外伤害
        /// </summary>
        private bool ShouldApplyExtraDamage(ExtraDamageDef extraDamage, DamageInfo originalDinfo, Thing victim)
        {
            // 检查最小触发伤害
            if (originalDinfo.Amount < extraDamage.minTriggerDamage)
                return false;
                
            return true;
        }
        
        /// <summary>
        /// 应用单个额外伤害
        /// </summary>
        private DamageResult ApplySingleExtraDamage(ExtraDamageDef extraDamage, DamageInfo originalDinfo, Thing victim)
        {
            try
            {
                // 计算伤害值
                float damageAmount = extraDamage.CalculateActualAmount(originalDinfo, victim);
                if (damageAmount <= 0)
                    return null;
                    
                // 计算护甲穿透
                float armorPenetration = extraDamage.CalculateActualArmorPenetration();
                
                // 创建伤害信息
                DamageInfo extraDinfo = new DamageInfo(
                    def: extraDamage.damageDef,
                    amount: damageAmount,
                    armorPenetration: armorPenetration,
                    angle: originalDinfo.Angle,
                    instigator: originalDinfo.Instigator,
                    hitPart: GetTargetBodyPart(victim, extraDamage.targetBodyPart),
                    weapon: originalDinfo.Weapon,
                    category: DamageInfo.SourceCategory.ThingOrUnknown,
                    intendedTarget: originalDinfo.IntendedTarget
                );
                
                // 如果是真实伤害，设置特殊标志（如果需要特殊处理）
                if (extraDamage.isTrueDamage)
                {
                    // 这里可能需要特殊的处理方式
                    // 例如，可以设置伤害信息中的特殊标志
                }
                
                // 应用伤害
                DamageResult result = victim.TakeDamage(extraDinfo);
                
                // 播放效果
                PlayExtraDamageEffects(extraDamage, victim, damageAmount);
                
                return result;
            }
            catch (System.Exception ex)
            {
                Log.Warning($"应用额外伤害时出错: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// 获取目标部位
        /// </summary>
        private BodyPartRecord GetTargetBodyPart(Thing victim, BodyPartDef bodyPartDef)
        {
            if (bodyPartDef == null || !(victim is Pawn pawn))
                return null;
                
            return pawn.RaceProps.body.GetPartsWithDef(bodyPartDef).FirstOrDefault();
        }
        
        /// <summary>
        /// 播放额外伤害效果
        /// </summary>
        private void PlayExtraDamageEffects(ExtraDamageDef extraDamage, Thing victim, float damageAmount)
        {
            if (victim.Map == null)
                return;
                
            // 播放音效
            if (extraDamage.soundDef != null)
            {
                extraDamage.soundDef.PlayOneShot(new TargetInfo(victim.Position, victim.Map));
            }
            
            // 播放粒子效果
            if (extraDamage.fleckDef != null)
            {
                FleckMaker.Static(victim.DrawPos, victim.Map, extraDamage.fleckDef);
            }
            
            // 播放效果器
            if (extraDamage.effecterDef != null)
            {
                Effecter effecter = extraDamage.effecterDef.Spawn();
                effecter.Trigger(new TargetInfo(victim.Position, victim.Map), new TargetInfo(victim.Position, victim.Map));
                effecter.Cleanup();
            }
        }
        
        /// <summary>
        /// 添加战斗日志
        /// </summary>
        private void AddCombatLog(
            DamageDef_ExtraDamageExtension extension, 
            DamageInfo originalDinfo, 
            Thing victim, 
            List<DamageResult> extraResults)
        {
            if (victim is Pawn victimPawn)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"{extension.extraLabel} - 额外伤害:");
                
                foreach (var result in extraResults)
                {
                    if (result != null && result.totalDamageDealt > 0)
                    {
                        sb.AppendLine($"  {result.totalDamageDealt:F1} 伤害");
                    }
                }
                
                // 这里可以添加更详细的战斗日志逻辑
                // 例如，创建一个自定义的战斗日志条目
            }
        }
        
        /// <summary>
        /// 重写爆炸伤害处理
        /// </summary>
        protected override void ExplosionDamageThing(Explosion explosion, Thing t, List<Thing> damagedThings, List<Thing> ignoredThings, IntVec3 cell)
        {
            base.ExplosionDamageThing(explosion, t, damagedThings, ignoredThings, cell);
            
            // 检查并应用额外伤害
            DamageDef_ExtraDamageExtension extension = 
                explosion.damType.GetModExtension<DamageDef_ExtraDamageExtension>();
                
            if (extension != null && !t.Destroyed)
            {
                // 为爆炸中的每个目标应用额外伤害
                // 注意：这里需要创建适当的DamageInfo
            }
        }
        
        /// <summary>
        /// 获取伤害描述（用于UI显示）
        /// </summary>
        public string GetExtraDamageDescription(DamageDef damageDef)
        {
            DamageDef_ExtraDamageExtension extension = 
                damageDef.GetModExtension<DamageDef_ExtraDamageExtension>();
                
            if (extension == null || extension.extraDamages.Count == 0)
                return "";
                
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("额外伤害效果:");
            
            foreach (var extraDamage in extension.extraDamages)
            {
                string damageType = extraDamage.damageDef.label;
                string amountStr = extraDamage.isPercentage ? 
                    $"{extraDamage.percentageMultiplier * 100}% 原始伤害" : 
                    $"{extraDamage.amount} 点";
                    
                sb.AppendLine($"  {damageType}: {amountStr}");
                
                if (extraDamage.minTriggerDamage > 0)
                {
                    sb.AppendLine($"    触发条件: 原始伤害 > {extraDamage.minTriggerDamage}");
                }
            }
            
            return sb.ToString();
        }
    }
}
