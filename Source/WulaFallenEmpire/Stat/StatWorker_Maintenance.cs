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
            if (req.Thing is Pawn pawn)
            {
                var maintenanceNeed = GetMaintenanceNeed(pawn);
                var extension = GetMaintenanceExtension(pawn);
                
                if (maintenanceNeed != null && extension != null)
                {
                    return GetStatValueForMaintenance(stat.defName, maintenanceNeed, extension);
                }
            }

            return stat.defaultBaseValue;
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

        private float GetStatValueForMaintenance(string statDefName, Need_Maintenance need, MaintenanceNeedExtension extension)
        {
            switch (statDefName)
            {
                case "WULA_MaintenanceDegradationFactor":
                    return CalculateDegradationFactor(need, extension);
                    
                case "WULA_MaintenanceStatusThresholdFactor":
                    return CalculateStatusThresholdFactor(need, extension);
                    
                case "WULA_MaintenanceDamageToMaintenanceFactor":
                    return CalculateDamageToMaintenanceFactor(need, extension);
                    
                case "WULA_MaintenanceMinorBreakdownThresholdFactor":
                    return CalculateMinorBreakdownThresholdFactor(need, extension);
                    
                case "WULA_MaintenanceMajorBreakdownThresholdFactor":
                    return CalculateMajorBreakdownThresholdFactor(need, extension);
                    
                case "WULA_MaintenanceCriticalFailureThresholdFactor":
                    return CalculateCriticalFailureThresholdFactor(need, extension);
                    
                default:
                    return stat.defaultBaseValue;
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

        // 计算各种统计值的方法
        private float CalculateDegradationFactor(Need_Maintenance need, MaintenanceNeedExtension extension)
        {
            // 基础退化速率乘数
            return 1.0f; // 默认值，可以根据需要调整
        }

        private float CalculateStatusThresholdFactor(Need_Maintenance need, MaintenanceNeedExtension extension)
        {
            // 状态阈值乘数
            return 1.0f; // 默认值，可以根据需要调整
        }

        private float CalculateDamageToMaintenanceFactor(Need_Maintenance need, MaintenanceNeedExtension extension)
        {
            // 伤害到维护度的转换因子
            return extension?.damageToMaintenanceFactor ?? 0.01f;
        }

        private float CalculateMinorBreakdownThresholdFactor(Need_Maintenance need, MaintenanceNeedExtension extension)
        {
            // 轻微故障阈值乘数
            return 1.0f; // 默认值
        }

        private float CalculateMajorBreakdownThresholdFactor(Need_Maintenance need, MaintenanceNeedExtension extension)
        {
            // 严重故障阈值乘数
            return 1.0f; // 默认值
        }

        private float CalculateCriticalFailureThresholdFactor(Need_Maintenance need, MaintenanceNeedExtension extension)
        {
            // 完全故障阈值乘数
            return 1.0f; // 默认值
        }

        // 解释文本生成方法
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
