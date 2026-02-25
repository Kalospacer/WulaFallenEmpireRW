using System;
using System.Collections.Generic;
using Verse;

namespace WulaFallenEmpire
{
    public class CompProperties_HediffGiver : CompProperties
    {
        // 要添加的hediff列表
        public List<HediffDef> hediffs;
        
        // 添加hediff的概率（0-1之间）
        public float addChance = 1.0f;
        
        // 是否允许重复添加相同的hediff
        public bool allowDuplicates = false;

        public CompProperties_HediffGiver()
        {
            this.compClass = typeof(CompHediffGiver);
        }
    }
}
