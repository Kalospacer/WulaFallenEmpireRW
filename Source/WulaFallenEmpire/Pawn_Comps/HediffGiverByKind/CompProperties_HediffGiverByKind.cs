using System;
using System.Collections.Generic;
using Verse;

namespace WulaFallenEmpire
{
    [Serializable]
    public class PawnKindHediffEntry
    {
        public PawnKindDef pawnKind;
        public List<HediffDef> hediffs;
        public float addChance = 1.0f; // 特定PawnKind的添加概率
        public bool allowDuplicates = false; // 是否允许重复添加
        
        public PawnKindHediffEntry()
        {
        }
        
        public PawnKindHediffEntry(PawnKindDef pawnKind, List<HediffDef> hediffs)
        {
            this.pawnKind = pawnKind;
            this.hediffs = hediffs;
        }
    }
    
    public class CompProperties_HediffGiverByKind : CompProperties
    {
        // 默认的hediff列表（当没有找到匹配的PawnKind时使用）
        public List<HediffDef> defaultHediffs;
        public float defaultAddChance = 1.0f;
        public bool defaultAllowDuplicates = false;
        
        // PawnKind特定的hediff配置
        public List<PawnKindHediffEntry> pawnKindHediffs;
        
        // 是否启用调试日志
        public bool debugMode = false;
        
        // 获取指定PawnKind的hediff配置
        public PawnKindHediffEntry GetHediffEntryForPawnKind(PawnKindDef pawnKind)
        {
            if (pawnKindHediffs == null || pawnKind == null)
                return null;
                
            foreach (var entry in pawnKindHediffs)
            {
                if (entry.pawnKind == pawnKind)
                    return entry;
            }
            
            return null;
        }
        
        // 获取要添加的hediff列表
        public List<HediffDef> GetHediffsForPawnKind(PawnKindDef pawnKind, out float addChance, out bool allowDuplicates)
        {
            var entry = GetHediffEntryForPawnKind(pawnKind);
            
            if (entry != null)
            {
                addChance = entry.addChance;
                allowDuplicates = entry.allowDuplicates;
                return entry.hediffs;
            }
            
            // 使用默认配置
            addChance = defaultAddChance;
            allowDuplicates = defaultAllowDuplicates;
            return defaultHediffs;
        }
        
        public CompProperties_HediffGiverByKind()
        {
            this.compClass = typeof(CompHediffGiverByKind);
        }
    }
}
