// Need_Maintenance.cs
using RimWorld;
using Verse;
using System.Linq;
using System;

namespace WulaFallenEmpire
{
    public class Need_Maintenance : Need
    {
        private MaintenanceNeedExtension Extension => def.GetModExtension<MaintenanceNeedExtension>();
        
        // 上次维护的天数
        private float daysSinceLastMaintenance = 0f;

        // 当前维护状态
        public MaintenanceStatus Status
        {
            get
            {
                if (CurLevel <= Extension?.criticalFailureThreshold) return MaintenanceStatus.CriticalFailure;
                if (CurLevel <= Extension?.majorBreakdownThreshold) return MaintenanceStatus.MajorBreakdown;
                if (CurLevel <= Extension?.minorBreakdownThreshold) return MaintenanceStatus.MinorBreakdown;
                return MaintenanceStatus.Operational;
            }
        }

        public float DaysSinceLastMaintenance => daysSinceLastMaintenance;

        public Need_Maintenance(Pawn pawn) : base(pawn)
        {
        }

        public override void SetInitialLevel()
        {
            CurLevel = 1.0f;
            daysSinceLastMaintenance = 0f;
        }

        public override void NeedInterval()
        {
            if (pawn.Dead || !pawn.Spawned)
                return;

            // 每150 ticks 更新一次（Need 的标准间隔）
            if (IsFrozen)
                return;

            // 增加天数计数
            daysSinceLastMaintenance += 150f / 60000f; // 150 ticks 占一天的比例
            
            // 计算退化速率
            float degradationRate = CalculateDegradationRate();
            
            // 应用退化
            CurLevel -= degradationRate * (150f / 60000f); // 转换为每天的比例
            
            // 确保数值在有效范围内
            CurLevel = ClampNeedLevel(CurLevel);
            
            // 检查状态变化
            CheckStatusChanges();
        }

        private float CalculateDegradationRate()
        {
            if (Extension == null)
                return 0f;

            if (daysSinceLastMaintenance < Extension.thresholdDays)
            {
                return Extension.severityPerDayBeforeThreshold;
            }
            else
            {
                return Extension.severityPerDayAfterThreshold;
            }
        }

        private void CheckStatusChanges()
        {
            if (Extension == null)
                return;

            // 检查是否需要应用故障效果
            var currentStatus = Status;
            
            // 移除旧的维护相关 Hediff
            RemoveMaintenanceHediffs();

            // 根据状态添加相应的 Hediff
            switch (currentStatus)
            {
                case MaintenanceStatus.MinorBreakdown:
                    if (Extension.minorBreakdownHediff != null)
                        pawn.health.AddHediff(Extension.minorBreakdownHediff);
                    break;
                    
                case MaintenanceStatus.MajorBreakdown:
                    if (Extension.majorBreakdownHediff != null)
                        pawn.health.AddHediff(Extension.majorBreakdownHediff);
                    break;
                    
                case MaintenanceStatus.CriticalFailure:
                    if (Extension.criticalFailureHediff != null)
                        pawn.health.AddHediff(Extension.criticalFailureHediff);
                    break;
            }
        }

        private void RemoveMaintenanceHediffs()
        {
            if (Extension == null)
                return;

            // 移除所有维护相关的 Hediff
            var hediffsToRemove = pawn.health.hediffSet.hediffs.FindAll(h => 
                h.def == Extension.minorBreakdownHediff || 
                h.def == Extension.majorBreakdownHediff || 
                h.def == Extension.criticalFailureHediff);
                
            foreach (var hediff in hediffsToRemove)
            {
                pawn.health.RemoveHediff(hediff);
            }
        }

        // 执行维护操作
        public void PerformMaintenance(float maintenanceAmount = 1.0f)
        {
            CurLevel += maintenanceAmount;
            CurLevel = ClampNeedLevel(CurLevel);
            daysSinceLastMaintenance = 0f;
            
            // 移除所有维护相关的负面效果
            RemoveMaintenanceHediffs();
            
            // 触发维护完成的效果
            OnMaintenancePerformed(maintenanceAmount);
        }

        // 应用伤害惩罚 - 简单的线性减少
        public void ApplyDamagePenalty(float damageAmount)
        {
            if (Extension == null) return;
            
            // 直接线性减少维护度
            float reduction = damageAmount * Extension.damageToMaintenanceFactor;
            CurLevel = Math.Max(0f, CurLevel - reduction);
            
            // 立即检查状态变化
            CheckStatusChanges();
            
            if (pawn.IsColonistPlayerControlled && reduction > 0.01f)
            {
                Messages.Message("WULA_MaintenanceReducedDueToDamage".Translate(pawn.LabelShort, reduction.ToStringPercent()), 
                    pawn, MessageTypeDefOf.NegativeEvent);
            }
        }

        private void OnMaintenancePerformed(float amount)
        {
            // 这里可以添加维护完成时的特殊效果
            if (pawn.IsColonistPlayerControlled)
            {
                Messages.Message("WULA_MaintenanceCompleted".Translate(pawn.LabelShort), pawn, MessageTypeDefOf.PositiveEvent);
            }
        }

        private float ClampNeedLevel(float level)
        {
            return level < 0f ? 0f : (level > 1f ? 1f : level);
        }

        public override string GetTipString()
        {
            string baseTip = base.GetTipString();
            
            string statusText = "WULA_MaintenanceStatus".Translate(Status.GetLabel(), daysSinceLastMaintenance.ToString("F1"));
            string degradationText = "WULA_DegradationRate".Translate(CalculateDegradationRate().ToString("F3"));
            
            return $"{baseTip}\n\n{statusText}\n{degradationText}";
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref daysSinceLastMaintenance, "daysSinceLastMaintenance", 0f);
        }
    }

    // 维护状态枚举
    public enum MaintenanceStatus
    {
        Operational,
        MinorBreakdown,
        MajorBreakdown,
        CriticalFailure
    }

    public static class MaintenanceStatusExtensions
    {
        public static string GetLabel(this MaintenanceStatus status)
        {
            switch (status)
            {
                case MaintenanceStatus.Operational:
                    return "WULA_Operational".Translate();
                case MaintenanceStatus.MinorBreakdown:
                    return "WULA_MinorBreakdown".Translate();
                case MaintenanceStatus.MajorBreakdown:
                    return "WULA_MajorBreakdown".Translate();
                case MaintenanceStatus.CriticalFailure:
                    return "WULA_CriticalFailure".Translate();
                default:
                    return "Unknown";
            }
        }
        
        public static string GetDescription(this MaintenanceStatus status)
        {
            switch (status)
            {
                case MaintenanceStatus.Operational:
                    return "WULA_OperationalDesc".Translate();
                case MaintenanceStatus.MinorBreakdown:
                    return "WULA_MinorBreakdownDesc".Translate();
                case MaintenanceStatus.MajorBreakdown:
                    return "WULA_MajorBreakdownDesc".Translate();
                case MaintenanceStatus.CriticalFailure:
                    return "WULA_CriticalFailureDesc".Translate();
                default:
                    return "Unknown";
            }
        }
    }
}
