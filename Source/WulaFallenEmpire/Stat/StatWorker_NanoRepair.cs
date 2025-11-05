// StatWorker_NanoRepair.cs
using RimWorld;
using Verse;
using System.Collections.Generic;

namespace WulaFallenEmpire
{
    public class StatWorker_NanoRepair : StatWorker
    {
        public override bool ShouldShowFor(StatRequest req)
        {
            // 只在有 HediffCompProperties_NanoRepair 的 hediff 时显示
            if (!base.ShouldShowFor(req))
                return false;

            // 检查是否为 Pawn
            if (req.Thing is Pawn pawn)
            {
                return HasNanoRepairHediff(pawn);
            }

            return false;
        }

        public override float GetValueUnfinalized(StatRequest req, bool applyPostProcess = true)
        {
            if (req.Thing is Pawn pawn)
            {
                var nanoComp = GetNanoRepairComp(pawn);
                if (nanoComp != null)
                {
                    // 根据请求的 StatDef 返回相应的值
                    if (stat.defName == "WULA_NanoRepairCostPerHP")
                    {
                        return nanoComp.Props.repairCostPerHP;
                    }
                    else if (stat.defName == "WULA_NanoRepairCooldownAfterDamage")
                    {
                        return nanoComp.Props.repairCooldownAfterDamage;
                    }
                }
            }

            return stat.defaultBaseValue;
        }

        public override string GetExplanationUnfinalized(StatRequest req, ToStringNumberSense numberSense)
        {
            var explanation = base.GetExplanationUnfinalized(req, numberSense);
            
            if (req.Thing is Pawn pawn)
            {
                var nanoComp = GetNanoRepairComp(pawn);
                if (nanoComp != null)
                {
                    explanation += "\n\n" + GetNanoRepairExplanation(nanoComp);
                }
            }

            return explanation;
        }

        private bool HasNanoRepairHediff(Pawn pawn)
        {
            if (pawn?.health?.hediffSet?.hediffs == null)
                return false;

            foreach (var hediff in pawn.health.hediffSet.hediffs)
            {
                var comp = hediff.TryGetComp<HediffComp_NanoRepair>();
                if (comp != null)
                    return true;
            }

            return false;
        }

        private HediffComp_NanoRepair GetNanoRepairComp(Pawn pawn)
        {
            if (pawn?.health?.hediffSet?.hediffs == null)
                return null;

            foreach (var hediff in pawn.health.hediffSet.hediffs)
            {
                var comp = hediff.TryGetComp<HediffComp_NanoRepair>();
                if (comp != null)
                    return comp;
            }

            return null;
        }

        private string GetNanoRepairExplanation(HediffComp_NanoRepair nanoComp)
        {
            var props = nanoComp.Props;
            var explanation = "WULA_NanoRepair_Properties".Translate();
            
            if (stat.defName == "WULA_NanoRepairCostPerHP")
            {
                explanation += "WULA_NanoRepair_CostPerHP_Line".Translate(props.repairCostPerHP.ToStringPercent());
                explanation += "WULA_NanoRepair_MinEnergyThreshold_Line".Translate(props.minEnergyThreshold.ToStringPercent());
            }
            else if (stat.defName == "WULA_NanoRepairCooldownAfterDamage")
            {
                explanation += "WULA_NanoRepair_CooldownAfterDamage_Line".Translate(props.repairCooldownAfterDamage, (props.repairCooldownAfterDamage / 60f).ToString("F1"));
                
                string systemStatus = nanoComp.repairSystemEnabled ? 
                    "WULA_NanoRepair_SystemStatus_Enabled".Translate() : 
                    "WULA_NanoRepair_SystemStatus_Disabled".Translate();
                explanation += "WULA_NanoRepair_SystemStatus_Line".Translate(systemStatus);
            }

            return explanation;
        }

        public override IEnumerable<Dialog_InfoCard.Hyperlink> GetInfoCardHyperlinks(StatRequest req)
        {
            foreach (var hyperlink in base.GetInfoCardHyperlinks(req))
            {
                yield return hyperlink;
            }

            // 添加纳米修复系统的超链接
            var nanoHediffDef = DefDatabase<HediffDef>.GetNamedSilentFail("WULA_NanoRepairHediff");
            if (nanoHediffDef != null)
            {
                yield return new Dialog_InfoCard.Hyperlink(nanoHediffDef);
            }
        }
    }
}
