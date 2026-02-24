using System;
using System.Collections.Generic;
using Verse;
using RimWorld;

namespace WulaFallenEmpire
{
    public class CompHediffGiverByKind : ThingComp
    {
        private bool hediffsApplied = false;
        private PawnKindDef appliedPawnKind = null; // 记录应用时的PawnKind

        public CompProperties_HediffGiverByKind Props => (CompProperties_HediffGiverByKind)this.props;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            
            if (this.parent is Pawn pawn)
            {
                // 检查是否需要重新应用hediff（例如PawnKind发生变化）
                if (!hediffsApplied || ShouldReapplyHediffs(pawn))
                {
                    ApplyHediffsToPawn(pawn);
                    hediffsApplied = true;
                    appliedPawnKind = pawn.kindDef;
                }
            }
        }
        
        private bool ShouldReapplyHediffs(Pawn pawn)
        {
            // 如果PawnKind发生了变化，需要重新应用
            return appliedPawnKind != pawn.kindDef;
        }

        private void ApplyHediffsToPawn(Pawn pawn)
        {
            try
            {
                // 获取对应PawnKind的hediff配置
                float addChance;
                bool allowDuplicates;
                var hediffs = Props.GetHediffsForPawnKind(pawn.kindDef, out addChance, out allowDuplicates);
                
                if (hediffs == null || hediffs.Count == 0)
                {
                    return;
                }
                
                // 检查概率
                if (addChance < 1.0f && Rand.Value > addChance)
                {
                    return;
                }
                
                // 移除旧的hediff（如果配置了不允许重复）
                if (!allowDuplicates)
                {
                    RemoveExistingHediffs(pawn, hediffs);
                }
                
                // 添加新的hediff
                int addedCount = 0;
                foreach (HediffDef hediffDef in hediffs)
                {
                    if (!allowDuplicates && pawn.health.hediffSet.HasHediff(hediffDef))
                    {
                        continue;
                    }
                    
                    pawn.health.AddHediff(hediffDef);
                    addedCount++;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[WFE] Error applying hediffs to {pawn.LabelCap}: {ex}");
            }
        }
        
        private void RemoveExistingHediffs(Pawn pawn, List<HediffDef> hediffDefs)
        {
            foreach (var hediffDef in hediffDefs)
            {
                var existingHediff = pawn.health.hediffSet.GetFirstHediffOfDef(hediffDef);
                if (existingHediff != null)
                {
                    pawn.health.RemoveHediff(existingHediff);
                }
            }
        }
        
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref hediffsApplied, "hediffsApplied", false);
            Scribe_Defs.Look(ref appliedPawnKind, "appliedPawnKind");
        }
        
        // 调试方法：手动应用hediff
        public void DebugApplyHediffs()
        {
            if (this.parent is Pawn pawn)
            {
                ApplyHediffsToPawn(pawn);
                hediffsApplied = true;
                appliedPawnKind = pawn.kindDef;
            }
        }
        
        // 获取当前配置的hediff列表（用于调试）
        public List<HediffDef> GetCurrentHediffs()
        {
            if (this.parent is Pawn pawn)
            {
                float addChance;
                bool allowDuplicates;
                return Props.GetHediffsForPawnKind(pawn.kindDef, out addChance, out allowDuplicates);
            }
            return null;
        }
        
        // 检查当前配置信息
        public string GetConfigInfo()
        {
            if (this.parent is Pawn pawn)
            {
                float addChance;
                bool allowDuplicates;
                var hediffs = Props.GetHediffsForPawnKind(pawn.kindDef, out addChance, out allowDuplicates);
                
                string info = $"Pawn: {pawn.LabelCap}\n";
                info += $"PawnKind: {pawn.kindDef?.defName ?? "None"}\n";
                info += $"Add Chance: {addChance * 100}%\n";
                info += $"Allow Duplicates: {allowDuplicates}\n";
                
                if (hediffs != null && hediffs.Count > 0)
                {
                    info += "Hediffs:\n";
                    foreach (var hediff in hediffs)
                    {
                        info += $"  - {hediff.defName}\n";
                    }
                }
                else
                {
                    info += "No hediffs configured\n";
                }
                
                return info;
            }
            
            return "Parent is not a Pawn";
        }
    }
}
