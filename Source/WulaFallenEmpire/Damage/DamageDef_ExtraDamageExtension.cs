using System.Collections.Generic;
using Verse;
using RimWorld;

namespace WulaFallenEmpire
{
    public class DamageDef_ExtraDamageExtension : DefModExtension
    {
        /// <summary>
        /// 额外伤害列表
        /// </summary>
        public List<ExtraDamageDef> extraDamages = new List<ExtraDamageDef>();
        
        /// <summary>
        /// 条件触发器列表
        /// </summary>
        public List<DamageCondition> conditions = new List<DamageCondition>();
        
        /// <summary>
        /// 是否显示额外的战斗日志信息
        /// </summary>
        public bool showExtraLog = true;
        
        /// <summary>
        /// 额外伤害标签
        /// </summary>
        public string extraLabel = "";
        
        /// <summary>
        /// 获取所有适用于特定目标的额外伤害
        /// </summary>
        public List<ExtraDamageDef> GetApplicableExtraDamages(Thing target, DamageInfo originalDinfo)
        {
            List<ExtraDamageDef> result = new List<ExtraDamageDef>();
            
            foreach (var extraDamage in extraDamages)
            {
                if (IsConditionMet(extraDamage.conditions, target, originalDinfo))
                {
                    result.Add(extraDamage);
                }
            }
            
            return result;
        }
        
        /// <summary>
        /// 检查条件是否满足
        /// </summary>
        private bool IsConditionMet(List<DamageCondition> conditions, Thing target, DamageInfo originalDinfo)
        {
            if (conditions == null || conditions.Count == 0)
                return true;
                
            foreach (var condition in conditions)
            {
                if (!condition.IsMet(target, originalDinfo))
                    return false;
            }
            
            return true;
        }
    }
    
    /// <summary>
    /// 额外伤害定义
    /// </summary>
    public class ExtraDamageDef
    {
        /// <summary>
        /// 伤害类型
        /// </summary>
        public DamageDef damageDef;
        
        /// <summary>
        /// 伤害数值（如果是百分比，基于原始伤害）
        /// </summary>
        public float amount = 10f;
        
        /// <summary>
        /// 是否为百分比伤害（相对于原始伤害）
        /// </summary>
        public bool isPercentage = false;
        
        /// <summary>
        /// 百分比倍数（当isPercentage为true时有效）
        /// </summary>
        public float percentageMultiplier = 0.5f;
        
        /// <summary>
        /// 护甲穿透值
        /// </summary>
        public float armorPenetration = -1f; // -1表示使用damageDef的默认值
        
        /// <summary>
        /// 命中部位（可选）
        /// </summary>
        public BodyPartDef targetBodyPart;
        
        /// <summary>
        /// 伤害应用条件
        /// </summary>
        public List<DamageCondition> conditions = new List<DamageCondition>();
        
        /// <summary>
        /// 伤害应用后的效果器
        /// </summary>
        public EffecterDef effecterDef;
        
        /// <summary>
        /// 伤害应用后的粒子
        /// </summary>
        public FleckDef fleckDef;
        
        /// <summary>
        /// 伤害应用后的音效
        /// </summary>
        public SoundDef soundDef;
        
        /// <summary>
        /// 最小触发伤害阈值
        /// </summary>
        public float minTriggerDamage = 0f;
        
        /// <summary>
        /// 是否可以被护甲抵抗
        /// </summary>
        public bool canBeBlockedByArmor = true;
        
        /// <summary>
        /// 是否为真实伤害（忽略所有抗性）
        /// </summary>
        public bool isTrueDamage = false;
        
        /// <summary>
        /// 计算实际伤害值
        /// </summary>
        public float CalculateActualAmount(DamageInfo originalDinfo, Thing target)
        {
            if (isPercentage)
            {
                return originalDinfo.Amount * percentageMultiplier;
            }
            return amount;
        }
        
        /// <summary>
        /// 计算实际护甲穿透值
        /// </summary>
        public float CalculateActualArmorPenetration()
        {
            if (armorPenetration >= 0)
                return armorPenetration;
            return damageDef.defaultArmorPenetration;
        }
    }
    
    /// <summary>
    /// 伤害条件
    /// </summary>
    public class DamageCondition
    {
        /// <summary>
        /// 目标类型
        /// </summary>
        public ConditionTarget targetType = ConditionTarget.Any;
        
        /// <summary>
        /// 特定种族列表（当targetType为SpecificRaces时有效）
        /// </summary>
        public List<ThingDef> specificRaces = new List<ThingDef>();
        
        /// <summary>
        /// 目标血量百分比阈值
        /// </summary>
        public FloatRange healthPercentRange = new FloatRange(0f, 1f);
        
        /// <summary>
        /// 目标是否必须存活
        /// </summary>
        public bool targetMustBeAlive = true;
        
        /// <summary>
        /// 目标是否必须有特定标签
        /// </summary>
        public List<string> requiredTags = new List<string>();
        
        /// <summary>
        /// 原始伤害类型限制
        /// </summary>
        public List<DamageDef> requiredOriginalDamageTypes = new List<DamageDef>();
        
        /// <summary>
        /// 原始伤害必须大于
        /// </summary>
        public float originalDamageMustBeGreaterThan = 0f;
        
        /// <summary>
        /// 检查条件是否满足
        /// </summary>
        public bool IsMet(Thing target, DamageInfo originalDinfo)
        {
            // 检查目标类型
            if (!CheckTargetType(target))
                return false;
                
            // 检查血量百分比
            if (!CheckHealthPercentage(target))
                return false;
                
            // 检查存活状态
            if (targetMustBeAlive && target is Pawn pawn && (pawn.Dead || pawn.Downed))
                return false;
                
            // 检查标签
            if (!CheckTags(target))
                return false;
                
            // 检查原始伤害类型
            if (!CheckOriginalDamageType(originalDinfo))
                return false;
                
            // 检查原始伤害大小
            if (originalDinfo.Amount <= originalDamageMustBeGreaterThan)
                return false;
                
            return true;
        }
        
        private bool CheckTargetType(Thing target)
        {
            switch (targetType)
            {
                case ConditionTarget.Any:
                    return true;
                case ConditionTarget.Pawn:
                    return target is Pawn;
                case ConditionTarget.Building:
                    return target is Building;
                default:
                    return true;
            }
        }
        
        private bool CheckHealthPercentage(Thing target)
        {
            if (target is Pawn pawn)
            {
                float healthPercent = pawn.health.summaryHealth.SummaryHealthPercent;
                return healthPercentRange.Includes(healthPercent);
            }
            return true;
        }
        
        private bool CheckTags(Thing target)
        {
            if (requiredTags == null || requiredTags.Count == 0)
                return true;
            
            return false;
        }
        
        private bool CheckOriginalDamageType(DamageInfo originalDinfo)
        {
            if (requiredOriginalDamageTypes == null || requiredOriginalDamageTypes.Count == 0)
                return true;
                
            return requiredOriginalDamageTypes.Contains(originalDinfo.Def);
        }
    }
    
    /// <summary>
    /// 条件目标类型
    /// </summary>
    public enum ConditionTarget
    {
        Any,
        Pawn,
        Animal,
        Humanlike,
        Mechanoid,
        Building,
        SpecificRaces
    }
}
