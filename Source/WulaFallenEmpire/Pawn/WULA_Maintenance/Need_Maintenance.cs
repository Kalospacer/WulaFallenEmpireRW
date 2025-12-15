// Need_Maintenance.cs
using RimWorld;
using Verse;
using System.Linq;
using System;
using System.Collections.Generic;

namespace WulaFallenEmpire
{
    public class Need_Maintenance : Need
    {
        public MaintenanceNeedExtension Extension => def.GetModExtension<MaintenanceNeedExtension>();
        
        // 上次维护的天数
        private float daysSinceLastMaintenance = 0f;
        
        // 新增：记录当前应用的 Hediff 状态
        private MaintenanceStatus currentAppliedStatus = MaintenanceStatus.Operational;
        private Hediff currentAppliedHediff = null;

        // 新增：验证计数器
        private int validationTickCounter = 0;
        private const int VALIDATION_INTERVAL_TICKS = 250; // 每250 ticks验证一次

        // 当前维护状态
        public MaintenanceStatus Status
        {
            get
            {
                if (Extension == null) return MaintenanceStatus.Operational;
                // 获取阈值乘数
                float minorFactor = GetStatValue("WULA_MaintenanceMinorBreakdownThresholdFactor");
                float majorFactor = GetStatValue("WULA_MaintenanceMajorBreakdownThresholdFactor");
                float criticalFactor = GetStatValue("WULA_MaintenanceCriticalFailureThresholdFactor");
                float minorThreshold = Extension.minorBreakdownThreshold * minorFactor;
                float majorThreshold = Extension.majorBreakdownThreshold * majorFactor;
                float criticalThreshold = Extension.criticalFailureThreshold * criticalFactor;
                if (CurLevel <= criticalThreshold) return MaintenanceStatus.CriticalFailure;
                if (CurLevel <= majorThreshold) return MaintenanceStatus.MajorBreakdown;
                if (CurLevel <= minorThreshold) return MaintenanceStatus.MinorBreakdown;
                return MaintenanceStatus.Operational;
            }
        }

        public float DaysSinceLastMaintenance => daysSinceLastMaintenance;

        // 新增：获取 StatDef 值的方法
        private float GetStatValue(string statDefName)
        {
            var statDef = DefDatabase<StatDef>.GetNamedSilentFail(statDefName);
            if (statDef != null)
            {
                return pawn.GetStatValue(statDef);
            }
            return 1.0f; // 默认值
        }

        public Need_Maintenance(Pawn pawn) : base(pawn)
        {
        }

        public override void SetInitialLevel()
        {
            CurLevel = 1.0f;
            daysSinceLastMaintenance = 0f;
            currentAppliedStatus = MaintenanceStatus.Operational;
            currentAppliedHediff = null;
            validationTickCounter = 0;
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
            
            // 新增：周期性验证 Hediff 状态
            PerformHediffValidation();
        }

        // 新增：周期性 Hediff 验证
        private void PerformHediffValidation()
        {
            validationTickCounter += 150; // NeedInterval 每次调用间隔150 ticks
            
            if (validationTickCounter >= VALIDATION_INTERVAL_TICKS)
            {
                validationTickCounter = 0;
                ValidateHediffConsistency();
            }
        }

        // 新增：验证 Hediff 一致性
        private void ValidateHediffConsistency()
        {
            if (pawn.Dead || !pawn.Spawned)
                return;

            var expectedStatus = Status;
            var actualHediffs = GetCurrentMaintenanceHediffs();
            
            // 情况1：没有 Hediff，但应该有
            if (expectedStatus != MaintenanceStatus.Operational && actualHediffs.Count == 0)
            {
                WulaLog.Debug($"[Maintenance] Validation: {pawn.Label} should have {expectedStatus} hediff but has none. Reapplying.");
                UpdateHediffForStatus(expectedStatus);
                return;
            }
            
            // 情况2：有 Hediff，但不应该有
            if (expectedStatus == MaintenanceStatus.Operational && actualHediffs.Count > 0)
            {
                WulaLog.Debug($"[Maintenance] Validation: {pawn.Label} is operational but has maintenance hediffs. Removing all.");
                RemoveAllMaintenanceHediffs();
                currentAppliedStatus = MaintenanceStatus.Operational;
                currentAppliedHediff = null;
                return;
            }
            
            // 情况3：有多个 Hediff
            if (actualHediffs.Count > 1)
            {
                WulaLog.Debug($"[Maintenance] Validation: {pawn.Label} has multiple maintenance hediffs ({actualHediffs.Count}). Cleaning up.");
                CleanupMultipleHediffs(expectedStatus);
                return;
            }
            
            // 情况4：Hediff 类型不正确
            if (actualHediffs.Count == 1 && expectedStatus != MaintenanceStatus.Operational)
            {
                var currentHediff = actualHediffs[0];
                var expectedHediffDef = GetHediffDefForStatus(expectedStatus);
                
                if (currentHediff.def != expectedHediffDef)
                {
                    WulaLog.Debug($"[Maintenance] Validation: {pawn.Label} has wrong hediff type. Expected {expectedHediffDef?.defName}, got {currentHediff.def.defName}. Correcting.");
                    UpdateHediffForStatus(expectedStatus);
                    return;
                }
                
                // 更新当前应用的 Hediff 引用
                if (currentAppliedHediff != currentHediff)
                {
                    currentAppliedHediff = currentHediff;
                    WulaLog.Debug($"[Maintenance] Validation: Updated currentAppliedHediff reference for {pawn.Label}");
                }
            }
            
            // 情况5：状态记录不一致
            if (currentAppliedStatus != expectedStatus)
            {
                WulaLog.Debug($"[Maintenance] Validation: {pawn.Label} status mismatch. Recorded: {currentAppliedStatus}, Actual: {expectedStatus}. Synchronizing.");
                currentAppliedStatus = expectedStatus;
            }
        }

        // 新增：获取当前所有的维护 Hediff
        private List<Hediff> GetCurrentMaintenanceHediffs()
        {
            var maintenanceHediffs = new List<Hediff>();
            
            if (Extension == null)
                return maintenanceHediffs;

            var allMaintenanceHediffDefs = new List<HediffDef>
            {
                Extension.minorBreakdownHediff,
                Extension.majorBreakdownHediff,
                Extension.criticalFailureHediff
            }.Where(def => def != null).ToList();

            foreach (var hediff in pawn.health.hediffSet.hediffs)
            {
                if (allMaintenanceHediffDefs.Contains(hediff.def))
                {
                    maintenanceHediffs.Add(hediff);
                }
            }

            return maintenanceHediffs;
        }

        // 新增：移除所有维护 Hediff
        private void RemoveAllMaintenanceHediffs()
        {
            var maintenanceHediffs = GetCurrentMaintenanceHediffs();
            foreach (var hediff in maintenanceHediffs)
            {
                pawn.health.RemoveHediff(hediff);
            }
        }

        // 新增：清理多个 Hediff
        private void CleanupMultipleHediffs(MaintenanceStatus expectedStatus)
        {
            // 移除所有维护 Hediff
            RemoveAllMaintenanceHediffs();
            
            // 重新应用正确的 Hediff
            if (expectedStatus != MaintenanceStatus.Operational)
            {
                UpdateHediffForStatus(expectedStatus);
            }
            
            currentAppliedStatus = expectedStatus;
        }

        // 修改 CalculateDegradationRate 方法以使用 StatDef
        private float CalculateDegradationRate()
        {
            if (Extension == null)
                return 0f;
            float baseRate = daysSinceLastMaintenance < Extension.thresholdDays ?
                Extension.severityPerDayBeforeThreshold :
                Extension.severityPerDayAfterThreshold;
            // 应用退化乘数
            float degradationFactor = GetStatValue("WULA_MaintenanceDegradationFactor");
            return baseRate * degradationFactor;
        }

        private void CheckStatusChanges()
        {
            if (Extension == null)
                return;

            var newStatus = Status;
            
            // 只有当状态发生变化时才更新 Hediff
            if (newStatus != currentAppliedStatus)
            {
                if (Prefs.DevMode)
                {
                    WulaLog.Debug($"[Maintenance] Status changed for {pawn.Label}: {currentAppliedStatus} -> {newStatus}");
                }
                
                UpdateHediffForStatus(newStatus);
                currentAppliedStatus = newStatus;
            }
        }

        // 修改：智能更新 Hediff
        private void UpdateHediffForStatus(MaintenanceStatus status)
        {
            // 首先移除所有维护相关的 Hediff
            RemoveAllMaintenanceHediffs();
            currentAppliedHediff = null;

            // 根据新状态添加相应的 Hediff
            HediffDef hediffDefToAdd = GetHediffDefForStatus(status);
            
            if (hediffDefToAdd != null)
            {
                currentAppliedHediff = pawn.health.AddHediff(hediffDefToAdd);
                
                // 调试日志
                if (Prefs.DevMode)
                {
                    WulaLog.Debug($"[Maintenance] Applied {hediffDefToAdd.defName} for status {status} to {pawn.Label}");
                }
            }
            else if (status == MaintenanceStatus.Operational)
            {
                // 操作状态，不需要 Hediff
                if (Prefs.DevMode)
                {
                    WulaLog.Debug($"[Maintenance] {pawn.Label} is operational, no hediff needed");
                }
            }
        }

        // 新增：获取对应状态的 HediffDef
        private HediffDef GetHediffDefForStatus(MaintenanceStatus status)
        {
            if (Extension == null)
                return null;

            switch (status)
            {
                case MaintenanceStatus.MinorBreakdown:
                    return Extension.minorBreakdownHediff;
                case MaintenanceStatus.MajorBreakdown:
                    return Extension.majorBreakdownHediff;
                case MaintenanceStatus.CriticalFailure:
                    return Extension.criticalFailureHediff;
                default:
                    return null;
            }
        }

        // 执行维护操作
        public void PerformMaintenance(float maintenanceAmount = 1.0f)
        {
            CurLevel += maintenanceAmount;
            CurLevel = ClampNeedLevel(CurLevel);
            daysSinceLastMaintenance = 0f;
            
            // 强制验证以确保状态正确
            var newStatus = Status;
            UpdateHediffForStatus(newStatus);
            currentAppliedStatus = newStatus;
            
            // 触发维护完成的效果
            OnMaintenancePerformed(maintenanceAmount);
            
            // 立即执行一次验证
            ValidateHediffConsistency();
        }

        // 修改 ApplyDamagePenalty 方法以使用 StatDef
        public void ApplyDamagePenalty(float damageAmount)
        {
            if (Extension == null) return;

            // 获取伤害转换因子
            float damageFactor = GetStatValue("WULA_MaintenanceDamageToMaintenanceFactor");
            float reduction = damageAmount * damageFactor;

            CurLevel = Math.Max(0f, CurLevel - reduction);

            // 检查状态变化
            var newStatus = Status;
            if (newStatus != currentAppliedStatus)
            {
                UpdateHediffForStatus(newStatus);
                currentAppliedStatus = newStatus;
            }
            
            // 立即执行一次验证
            ValidateHediffConsistency();
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
            Scribe_Values.Look(ref currentAppliedStatus, "currentAppliedStatus", MaintenanceStatus.Operational);
            Scribe_References.Look(ref currentAppliedHediff, "currentAppliedHediff");
            Scribe_Values.Look(ref validationTickCounter, "validationTickCounter", 0);
            
            // 修复：加载后验证状态一致性
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // 延迟执行验证，确保所有组件都已加载
                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    // 使用简单的延迟调用而不是 AddOnceOffAction
                    Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame); // 强制触发一次更新
                    ValidateHediffConsistency();
                });
            }
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
