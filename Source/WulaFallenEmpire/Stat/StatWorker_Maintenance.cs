// StatWorker_Maintenance.cs
using RimWorld;
using Verse;
using System.Collections.Generic;

namespace WulaFallenEmpire
{
    public class StatWorker_Maintenance : StatWorker
    {
        public override bool ShouldShowFor(StatRequest req)
        {
            // 只在有维护需求的机械体上显示
            if (!base.ShouldShowFor(req))
                return false;

            if (req.Thing is Pawn pawn)
            {
                return HasMaintenanceNeed(pawn);
            }

            return false;
        }

        public override float GetValueUnfinalized(StatRequest req, bool applyPostProcess = true)
        {
            // 关键修复：调用基类方法让 RimWorld 的统计系统处理所有修正
            float baseValue = base.GetValueUnfinalized(req, applyPostProcess);
            
            if (req.Thing is Pawn pawn)
            {
                var maintenanceNeed = GetMaintenanceNeed(pawn);
                var extension = GetMaintenanceExtension(pawn);
                
                if (maintenanceNeed != null && extension != null)
                {
                    return GetStatValueForMaintenance(stat.defName, maintenanceNeed, extension, baseValue);
                }
            }

            return baseValue;
        }

        public override string GetExplanationUnfinalized(StatRequest req, ToStringNumberSense numberSense)
        {
            var explanation = base.GetExplanationUnfinalized(req, numberSense);
            
            if (req.Thing is Pawn pawn)
            {
                var maintenanceNeed = GetMaintenanceNeed(pawn);
                var extension = GetMaintenanceExtension(pawn);
                
                if (maintenanceNeed != null && extension != null)
                {
                    explanation += "\n\n" + GetMaintenanceExplanation(stat.defName, maintenanceNeed, extension);
                }
            }

            return explanation;
        }

        private bool HasMaintenanceNeed(Pawn pawn)
        {
            return pawn?.needs?.TryGetNeed<Need_Maintenance>() != null;
        }

        private Need_Maintenance GetMaintenanceNeed(Pawn pawn)
        {
            return pawn?.needs?.TryGetNeed<Need_Maintenance>();
        }

        private MaintenanceNeedExtension GetMaintenanceExtension(Pawn pawn)
        {
            var maintenanceNeed = GetMaintenanceNeed(pawn);
            return maintenanceNeed?.Extension;
        }

        private float GetStatValueForMaintenance(string statDefName, Need_Maintenance need, MaintenanceNeedExtension extension, float baseValue)
        {
            // 关键修复：使用基础值而不是固定值
            switch (statDefName)
            {
                case "WULA_MaintenanceDegradationFactor":
                    return CalculateDegradationFactor(need, extension, baseValue);
                    
                case "WULA_MaintenanceStatusThresholdFactor":
                    return CalculateStatusThresholdFactor(need, extension, baseValue);
                    
                case "WULA_MaintenanceDamageToMaintenanceFactor":
                    return CalculateDamageToMaintenanceFactor(need, extension, baseValue);
                    
                case "WULA_MaintenanceMinorBreakdownThresholdFactor":
                    return CalculateMinorBreakdownThresholdFactor(need, extension, baseValue);
                    
                case "WULA_MaintenanceMajorBreakdownThresholdFactor":
                    return CalculateMajorBreakdownThresholdFactor(need, extension, baseValue);
                    
                case "WULA_MaintenanceCriticalFailureThresholdFactor":
                    return CalculateCriticalFailureThresholdFactor(need, extension, baseValue);
                    
                default:
                    return baseValue;
            }
        }

        private string GetMaintenanceExplanation(string statDefName, Need_Maintenance need, MaintenanceNeedExtension extension)
        {
            var explanation = "WULA_Maintenance_Properties".Translate();
            
            switch (statDefName)
            {
                case "WULA_MaintenanceDegradationFactor":
                    explanation += GetDegradationFactorExplanation(need, extension);
                    break;
                    
                case "WULA_MaintenanceStatusThresholdFactor":
                    explanation += GetStatusThresholdFactorExplanation(need, extension);
                    break;
                    
                case "WULA_MaintenanceDamageToMaintenanceFactor":
                    explanation += GetDamageToMaintenanceFactorExplanation(need, extension);
                    break;
                    
                case "WULA_MaintenanceMinorBreakdownThresholdFactor":
                    explanation += GetMinorBreakdownThresholdExplanation(need, extension);
                    break;
                    
                case "WULA_MaintenanceMajorBreakdownThresholdFactor":
                    explanation += GetMajorBreakdownThresholdExplanation(need, extension);
                    break;
                    
                case "WULA_MaintenanceCriticalFailureThresholdFactor":
                    explanation += GetCriticalFailureThresholdExplanation(need, extension);
                    break;
            }

            return explanation;
        }

        // 计算各种统计值的方法 - 现在接受基础值参数
        private float CalculateDegradationFactor(Need_Maintenance need, MaintenanceNeedExtension extension, float baseValue)
        {
            // 基础退化速率乘数，使用统计系统修正后的值
            return baseValue;
        }

        private float CalculateStatusThresholdFactor(Need_Maintenance need, MaintenanceNeedExtension extension, float baseValue)
        {
            // 状态阈值乘数，使用统计系统修正后的值
            return baseValue;
        }

        private float CalculateDamageToMaintenanceFactor(Need_Maintenance need, MaintenanceNeedExtension extension, float baseValue)
        {
            // 伤害到维护度的转换因子，使用统计系统修正后的值
            return baseValue;
        }

        private float CalculateMinorBreakdownThresholdFactor(Need_Maintenance need, MaintenanceNeedExtension extension, float baseValue)
        {
            // 轻微故障阈值乘数，使用统计系统修正后的值
            return baseValue;
        }

        private float CalculateMajorBreakdownThresholdFactor(Need_Maintenance need, MaintenanceNeedExtension extension, float baseValue)
        {
            // 严重故障阈值乘数，使用统计系统修正后的值
            return baseValue;
        }

        private float CalculateCriticalFailureThresholdFactor(Need_Maintenance need, MaintenanceNeedExtension extension, float baseValue)
        {
            // 完全故障阈值乘数，使用统计系统修正后的值
            return baseValue;
        }

        // 解释文本生成方法（保持不变）
        private string GetDegradationFactorExplanation(Need_Maintenance need, MaintenanceNeedExtension extension)
        {
            return "WULA_Maintenance_DegradationFactor_Explanation".Translate(
                extension?.severityPerDayBeforeThreshold.ToString("F3"),
                extension?.severityPerDayAfterThreshold.ToString("F3"),
                extension?.thresholdDays.ToString("F1")
            );
        }

        private string GetStatusThresholdFactorExplanation(Need_Maintenance need, MaintenanceNeedExtension extension)
        {
            return "WULA_Maintenance_StatusThresholdFactor_Explanation".Translate(
                extension?.minorBreakdownThreshold.ToStringPercent(),
                extension?.majorBreakdownThreshold.ToStringPercent(),
                extension?.criticalFailureThreshold.ToStringPercent()
            );
        }

        private string GetDamageToMaintenanceFactorExplanation(Need_Maintenance need, MaintenanceNeedExtension extension)
        {
            return "WULA_Maintenance_DamageToMaintenanceFactor_Explanation".Translate(
                (extension?.damageToMaintenanceFactor ?? 0.01f).ToStringPercent()
            );
        }

        private string GetMinorBreakdownThresholdExplanation(Need_Maintenance need, MaintenanceNeedExtension extension)
        {
            return "WULA_Maintenance_MinorBreakdownThreshold_Explanation".Translate(
                extension?.minorBreakdownThreshold.ToStringPercent()
            );
        }

        private string GetMajorBreakdownThresholdExplanation(Need_Maintenance need, MaintenanceNeedExtension extension)
        {
            return "WULA_Maintenance_MajorBreakdownThreshold_Explanation".Translate(
                extension?.majorBreakdownThreshold.ToStringPercent()
            );
        }

        private string GetCriticalFailureThresholdExplanation(Need_Maintenance need, MaintenanceNeedExtension extension)
        {
            return "WULA_Maintenance_CriticalFailureThreshold_Explanation".Translate(
                extension?.criticalFailureThreshold.ToStringPercent()
            );
        }

        public override IEnumerable<Dialog_InfoCard.Hyperlink> GetInfoCardHyperlinks(StatRequest req)
        {
            foreach (var hyperlink in base.GetInfoCardHyperlinks(req))
            {
                yield return hyperlink;
            }

            // 添加维护系统的超链接
            var maintenanceNeedDef = DefDatabase<NeedDef>.GetNamedSilentFail("WULA_Maintenance");
            if (maintenanceNeedDef != null)
            {
                yield return new Dialog_InfoCard.Hyperlink(maintenanceNeedDef);
            }
        }
    }
}
