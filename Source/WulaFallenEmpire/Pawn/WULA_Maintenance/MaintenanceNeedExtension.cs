// MaintenanceNeedExtension.cs
using Verse;

namespace WulaFallenEmpire
{
    public class MaintenanceNeedExtension : DefModExtension
    {
        // 基础退化设置
        public float severityPerDayBeforeThreshold = 0.05f;
        public float severityPerDayAfterThreshold = 0.1f;
        public float thresholdDays = 5f;

        // 状态阈值
        public float minorBreakdownThreshold = 0.3f;
        public float majorBreakdownThreshold = 0.1f;
        public float criticalFailureThreshold = 0.01f;

        // 伤害相关设置
        public float damageToMaintenanceFactor = 0.01f;

        // 维护效果相关的 HediffDefs
        public HediffDef minorBreakdownHediff = null;
        public HediffDef majorBreakdownHediff = null;
        public HediffDef criticalFailureHediff = null;
    }

}
