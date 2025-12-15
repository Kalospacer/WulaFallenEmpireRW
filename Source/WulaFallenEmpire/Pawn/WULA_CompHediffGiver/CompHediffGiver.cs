using System;
using System.Collections.Generic;
using Verse;
using RimWorld;

namespace WulaFallenEmpire
{
    public class CompHediffGiver : ThingComp
    {
        private bool hediffsApplied = false; // 新增：标记是否已经应用过hediff

        public CompProperties_HediffGiver Props => (CompProperties_HediffGiver)this.props;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            
            // 只有当thing是pawn时才添加hediff
            if (this.parent is Pawn pawn)
            {
                // 新增：检查是否已经应用过hediff，或者是否是读档
                if (!hediffsApplied && !respawningAfterLoad)
                {
                    AddHediffsToPawn(pawn);
                    hediffsApplied = true; // 标记为已应用
                }
            }
        }

        private void AddHediffsToPawn(Pawn pawn)
        {
            // 检查是否有hediff列表
            if (Props.hediffs == null || Props.hediffs.Count == 0)
                return;

            // 检查概率
            if (Props.addChance < 1.0f && Rand.Value > Props.addChance)
                return;

            // 为每个hediff添加到pawn
            foreach (HediffDef hediffDef in Props.hediffs)
            {
                // 检查是否允许重复添加
                if (!Props.allowDuplicates && pawn.health.hediffSet.HasHediff(hediffDef))
                    continue;

                // 添加hediff
                pawn.health.AddHediff(hediffDef);
            }
        }

        // 新增：序列化hediffsApplied标记
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref hediffsApplied, "hediffsApplied", false);
        }

        // 新增：调试方法，用于手动触发hediff添加（仅开发模式）
        public void DebugApplyHediffs()
        {
            if (this.parent is Pawn pawn && !hediffsApplied)
            {
                AddHediffsToPawn(pawn);
                hediffsApplied = true;
                WulaLog.Debug($"Debug: Applied hediffs to {pawn.Label}");
            }
        }
    }
}
