// StatWorker_Energy.cs - 处理能量相关的统计量
using RimWorld;
using Verse;
using System.Collections.Generic;

namespace WulaFallenEmpire
{
    public class StatWorker_Energy : StatWorker
    {
        public override bool ShouldShowFor(StatRequest req)
        {
            if (!base.ShouldShowFor(req))
                return false;

            // 处理 ThingDef 的情况（种族定义）
            if (req.Def is ThingDef thingDef)
            {
                // 检查是否为 WulaSpecies 种族
                return thingDef.defName == "WulaSpecies";
            }

            // 检查是否为 Pawn
            if (req.Thing is Pawn pawn)
            {
                // 检查是否有能量需求
                return HasEnergyNeed(pawn);
            }

            return false;
        }

        public override float GetValueUnfinalized(StatRequest req, bool applyPostProcess = true)
        {
            // 关键修复：调用基类方法让 RimWorld 的统计系统处理所有修正
            float baseValue = base.GetValueUnfinalized(req, applyPostProcess);
            
            if (req.Thing is Pawn pawn)
            {
                return GetStatValueForPawn(stat.defName, pawn, baseValue);
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
            
            // 添加自定义解释
            if (req.Thing is Pawn pawn)
            {
                explanation += "\n\n" + GetEnergyExplanationForPawn(stat.defName, pawn);
            }
            else if (req.Def is ThingDef thingDef && thingDef.defName == "WulaSpecies")
            {
                explanation += "\n\n" + GetEnergyExplanationForRace(stat.defName);
            }

            return explanation;
        }

        private bool HasEnergyNeed(Pawn pawn)
        {
            // 使用反射或其他方式检查是否有能量需求，或者直接返回 true 对于 WulaSpecies
            if (pawn?.def?.defName == "WulaSpecies")
                return true;
                
            // 如果有具体的能量需求类，可以在这里检查
            // return pawn?.needs?.TryGetNeed<Need_Energy>() != null;
            return false;
        }

        private float GetStatValueForPawn(string statDefName, Pawn pawn, float baseValue)
        {
            // 对于 WulaSpecies 种族，返回基于其特性的值
            if (pawn.def.defName == "WulaSpecies")
            {
                switch (statDefName)
                {
                    case "WulaEnergyMaxLevelOffset":
                        return CalculateEnergyMaxLevelOffset(pawn, baseValue);
                        
                    case "WulaEnergyFallRateFactor":
                        return CalculateEnergyFallRateFactor(pawn, baseValue);
                        
                    default:
                        return baseValue;
                }
            }

            return baseValue;
        }

        private float GetDefaultValueForStat(string statDefName, float baseValue)
        {
            // 关键修复：使用基础值而不是固定值
            switch (statDefName)
            {
                case "WulaEnergyMaxLevelOffset":
                    return baseValue; // 使用统计系统修正后的值
                    
                case "WulaEnergyFallRateFactor":
                    return baseValue; // 使用统计系统修正后的值
                    
                default:
                    return baseValue;
            }
        }

        private float CalculateEnergyMaxLevelOffset(Pawn pawn, float baseValue)
        {
            // 计算能量上限偏移量，使用基础值作为起点
            // 这里可以根据 pawn 的特性计算
            return baseValue;
        }

        private float CalculateEnergyFallRateFactor(Pawn pawn, float baseValue)
        {
            // 计算能量下降速率因子，使用基础值作为起点
            // 这里可以根据 pawn 的特性计算
            return baseValue;
        }

        private string GetEnergyExplanationForPawn(string statDefName, Pawn pawn)
        {
            var explanation = "WULA_Energy_Properties".Translate();
            
            switch (statDefName)
            {
                case "WulaEnergyMaxLevelOffset":
                    explanation += "\n" + "WULA_Energy_MaxLevelOffset_PawnExplanation".Translate();
                    break;
                    
                case "WulaEnergyFallRateFactor":
                    explanation += "\n" + "WULA_Energy_FallRateFactor_PawnExplanation".Translate();
                    break;
            }

            return explanation;
        }

        private string GetEnergyExplanationForRace(string statDefName)
        {
            var explanation = "WULA_Energy_RaceProperties".Translate();
            
            switch (statDefName)
            {
                case "WulaEnergyMaxLevelOffset":
                    explanation += "\n" + "WULA_Energy_MaxLevelOffset_RaceExplanation".Translate();
                    break;
                    
                case "WulaEnergyFallRateFactor":
                    explanation += "\n" + "WULA_Energy_FallRateFactor_RaceExplanation".Translate();
                    break;
            }

            return explanation;
        }

        public override IEnumerable<Dialog_InfoCard.Hyperlink> GetInfoCardHyperlinks(StatRequest req)
        {
            foreach (var hyperlink in base.GetInfoCardHyperlinks(req))
            {
                yield return hyperlink;
            }

            // 添加能量系统的超链接
            var energyNeedDef = DefDatabase<NeedDef>.GetNamedSilentFail("WULA_Energy");
            if (energyNeedDef != null)
            {
                yield return new Dialog_InfoCard.Hyperlink(energyNeedDef);
            }
        }
    }
}
