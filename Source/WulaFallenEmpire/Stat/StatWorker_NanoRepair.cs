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

            // 处理 ThingDef 的情况（种族定义）
            if (req.Def is ThingDef thingDef)
            {
                // 检查是否为 WulaSpecies 种族
                if (thingDef.defName == "WulaSpecies")
                {
                    // 对于 WulaSpecies 种族，只在有纳米修复 hediff 时显示
                    return HasNanoRepairForRace(thingDef);
                }
                return false;
            }

            // 检查是否为 Pawn
            if (req.Thing is Pawn pawn)
            {
                return HasNanoRepairHediff(pawn);
            }

            return false;
        }

        public override float GetValueUnfinalized(StatRequest req, bool applyPostProcess = true)
        {
            // 关键修复：调用基类方法让 RimWorld 的统计系统处理所有修正
            float baseValue = base.GetValueUnfinalized(req, applyPostProcess);
            
            if (req.Thing is Pawn pawn)
            {
                var nanoComp = GetNanoRepairComp(pawn);
                if (nanoComp != null)
                {
                    return GetStatValueForNanoRepair(stat.defName, nanoComp, baseValue);
                }
            }
            else if (req.Def is ThingDef thingDef && thingDef.defName == "WulaSpecies")
            {
                // 对于 WulaSpecies 种族，返回经过修正的值
                return GetDefaultValueForStat(stat.defName, baseValue);
            }

            return baseValue;
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
            else if (req.Def is ThingDef thingDef && thingDef.defName == "WulaSpecies")
            {
                explanation += "\n\n" + GetNanoRepairExplanationForRace();
            }

            return explanation;
        }

        private bool HasNanoRepairForRace(ThingDef raceDef)
        {
            // 检查该种族是否有纳米修复 hediff
            // 这里可以添加更复杂的逻辑来检查种族是否有纳米修复能力
            return raceDef.defName == "WulaSpecies";
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

        private float GetStatValueForNanoRepair(string statDefName, HediffComp_NanoRepair nanoComp, float baseValue)
        {
            // 关键修复：使用基础值而不是固定值
            var props = nanoComp.Props;
            
            switch (statDefName)
            {
                case "WULA_NanoRepairCostPerHP":
                    return baseValue; // 使用统计系统修正后的值
                    
                case "WULA_NanoRepairCooldownAfterDamage":
                    return baseValue; // 使用统计系统修正后的值
                    
                default:
                    return baseValue;
            }
        }

        private float GetDefaultValueForStat(string statDefName, float baseValue)
        {
            // 关键修复：使用基础值而不是固定值
            switch (statDefName)
            {
                case "WULA_NanoRepairCostPerHP":
                    return baseValue; // 使用统计系统修正后的值
                    
                case "WULA_NanoRepairCooldownAfterDamage":
                    return baseValue; // 使用统计系统修正后的值
                    
                default:
                    return baseValue;
            }
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

        private string GetNanoRepairExplanationForRace()
        {
            var explanation = "WULA_NanoRepair_RaceProperties".Translate();
            
            if (stat.defName == "WULA_NanoRepairCostPerHP")
            {
                explanation += "WULA_NanoRepair_CostPerHP_RaceLine".Translate(stat.defaultBaseValue.ToStringPercent());
            }
            else if (stat.defName == "WULA_NanoRepairCooldownAfterDamage")
            {
                explanation += "WULA_NanoRepair_CooldownAfterDamage_RaceLine".Translate(
                    stat.defaultBaseValue, 
                    (stat.defaultBaseValue / 60f).ToString("F1"));
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
