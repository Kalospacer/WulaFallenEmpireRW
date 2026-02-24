// CompProperties_MechDefaultPilot.cs
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace WulaFallenEmpire
{
    public class DefaultPilotEntry
    {
        public PawnKindDef pawnKind;
        public float weight = 1f; // 权重，用于随机选择
        public bool required = false; // 是否必选
        
        public DefaultPilotEntry() { }
        
        public DefaultPilotEntry(PawnKindDef pawnKind, float weight = 1f, bool required = false)
        {
            this.pawnKind = pawnKind;
            this.weight = weight;
            this.required = required;
        }
    }
    
    public class CompProperties_MechDefaultPilot : CompProperties
    {
        // 基本配置
        public bool enableForNonPlayerFaction = true;
        public bool enableForPlayerFaction = false; // 玩家阵营是否也生成默认驾驶员
        public float defaultPilotChance = 1.0f; // 默认生成几率
        
        // 驾驶员配置
        public List<DefaultPilotEntry> defaultPilots = new List<DefaultPilotEntry>();
        
        // 高级配置
        public bool spawnOnlyIfNoPilot = true; // 只在没有驾驶员时生成
        public bool replaceExistingPilots = false; // 替换现有驾驶员
        public int maxDefaultPilots = -1; // -1表示使用CompMechPilotHolder的最大容量
        
        public CompProperties_MechDefaultPilot()
        {
            this.compClass = typeof(CompMechDefaultPilot);
        }
        
        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
            {
                yield return error;
            }
            
            if (defaultPilotChance < 0f || defaultPilotChance > 1f)
            {
                yield return $"defaultPilotChance must be between 0 and 1 for {parentDef.defName}";
            }
            
            if (defaultPilots.Count == 0)
            {
                yield return $"No defaultPilots defined for {parentDef.defName}";
            }
            else
            {
                foreach (var entry in defaultPilots)
                {
                    if (entry.pawnKind == null)
                    {
                        yield return $"Null pawnKind in defaultPilots for {parentDef.defName}";
                    }
                }
            }
        }
        
        // 获取有效的默认驾驶员列表
        public List<DefaultPilotEntry> GetValidDefaultPilots()
        {
            return defaultPilots.FindAll(p => p.pawnKind != null);
        }
        
        // 计算总权重
        public float GetTotalWeight()
        {
            float total = 0f;
            foreach (var entry in defaultPilots)
            {
                if (entry.pawnKind != null && !entry.required)
                {
                    total += entry.weight;
                }
            }
            return total;
        }
        
        // 随机选择一个驾驶员类型
        public PawnKindDef SelectRandomPilotKind()
        {
            var validPilots = GetValidDefaultPilots();
            if (validPilots.Count == 0)
                return null;
            
            // 先检查必选项
            foreach (var entry in validPilots)
            {
                if (entry.required)
                    return entry.pawnKind;
            }
            
            // 如果没有必选项，按权重随机选择
            float totalWeight = GetTotalWeight();
            if (totalWeight <= 0f)
                return validPilots[0].pawnKind; // 回退到第一个
            
            float random = Rand.Range(0f, totalWeight);
            float current = 0f;
            
            foreach (var entry in validPilots)
            {
                if (entry.required)
                    continue;
                    
                current += entry.weight;
                if (random <= current)
                    return entry.pawnKind;
            }
            
            return validPilots[validPilots.Count - 1].pawnKind; // 回退到最后一个
        }
    }
}
