using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using Verse;
using RimWorld.QuestGen;

namespace WulaFallenEmpire
{
    public class QuestPart_GlobalResourceCheck : QuestPartActivable
    {
        // 配置参数
        public ThingDef resourceDef;
        public int requiredCount;
        public int retryDelayTicks = 60;
        
        [NoTranslate]
        public string successSignal;
        
        [NoTranslate]
        public string failSignal;
        
        public bool deductOnSuccess = true;
        public bool useInputStorage = true;
        public string debugInfo;

        // 状态变量
        public int nextRetryTick = -1;
        private bool hasSucceeded = false;
        private bool hasFailed = false;
        private int retryCount = 0;
        private const int MAX_RETRY_COUNT = 1000;

        public override void AssignDebugData()
        {
            base.AssignDebugData();
            
            resourceDef = ThingDefOf.Steel;
            requiredCount = 100;
            retryDelayTicks = 60;
            successSignal = "TaxPaymentSuccess";
            failSignal = "TaxPaymentFailed";
            deductOnSuccess = true;
            useInputStorage = true;
            debugInfo = "Debug: Tax Collection Check";
        }

        protected override void Enable(SignalArgs receivedArgs)
        {
            base.Enable(receivedArgs);
            
            // 激活时立即开始第一次检查
            nextRetryTick = Find.TickManager.TicksGame;
            WulaLog.Debug($"QuestPart_GlobalResourceCheck Enabled: Will check for {requiredCount} {resourceDef?.defName} in {(useInputStorage ? "Input" : "Output")} Storage");
        }

        public override void QuestPartTick()
        {
            base.QuestPartTick();

            // 如果任务已经结束，停止处理
            if (quest.State != QuestState.Ongoing)
            {
                return;
            }

            // 如果已经成功或失败，不再处理
            if (hasSucceeded || hasFailed)
            {
                return;
            }

            // 检查是否到了重试时间
            int currentTick = Find.TickManager.TicksGame;
            if (nextRetryTick == -1 || currentTick < nextRetryTick)
            {
                return;
            }

            // 执行资源检查
            CheckGlobalResource();
        }

        private void CheckGlobalResource()
        {
            try
            {
                // 更新下次重试时间
                nextRetryTick = Find.TickManager.TicksGame + retryDelayTicks;
                retryCount++;

                // 获取全局资源储存器
                GlobalStorageWorldComponent globalStorage = Find.World.GetComponent<GlobalStorageWorldComponent>();
                if (globalStorage == null)
                {
                    WulaLog.Debug("QuestPart_GlobalResourceCheck: GlobalStorageWorldComponent not found");
                    HandleFailure("Global storage component missing");
                    return;
                }

                if (resourceDef == null)
                {
                    WulaLog.Debug("QuestPart_GlobalResourceCheck: resourceDef is null");
                    HandleFailure("Resource definition is null");
                    return;
                }

                // 检查资源是否足够
                int currentAmount = useInputStorage ? 
                    globalStorage.GetInputStorageCount(resourceDef) : 
                    globalStorage.GetOutputStorageCount(resourceDef);
                    
                bool hasEnough = currentAmount >= requiredCount;

                WulaLog.Debug($"GlobalResourceCheck [{retryCount}]: {currentAmount}/{requiredCount} {resourceDef.defName} in {(useInputStorage ? "Input" : "Output")} Storage - Enough: {hasEnough}");

                if (hasEnough)
                {
                    // 资源足够，处理成功
                    HandleSuccess(globalStorage);
                }
                else
                {
                    // 资源不足，安排重试
                    HandleFailure($"Insufficient resources: {currentAmount}/{requiredCount}");
                }
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"GlobalResourceCheck: Exception during check - {ex}");
                HandleFailure($"Exception: {ex.Message}");
            }
        }

        private void HandleSuccess(GlobalStorageWorldComponent globalStorage)
        {
            hasSucceeded = true;

            // 如果需要扣除资源
            if (deductOnSuccess)
            {
                bool deducted = useInputStorage ? 
                    globalStorage.RemoveFromInputStorage(resourceDef, requiredCount) :
                    globalStorage.RemoveFromOutputStorage(resourceDef, requiredCount);
                    
                if (!deducted)
                {
                    WulaLog.Debug($"QuestPart_GlobalResourceCheck: Failed to deduct {requiredCount} {resourceDef.defName} from {(useInputStorage ? "Input" : "Output")} Storage");
                }
            }

            WulaLog.Debug($"GlobalResourceCheck: SUCCESS - {(deductOnSuccess ? "Deducted" : "Found")} {requiredCount} {resourceDef.defName} from {(useInputStorage ? "Input" : "Output")} Storage");

            // 发送成功信号
            if (!successSignal.NullOrEmpty())
            {
                Find.SignalManager.SendSignal(new Signal(successSignal));
                WulaLog.Debug($"GlobalResourceCheck: Sent success signal '{successSignal}'");
            }

            // 完成这个任务部分
            Complete();
        }

        private void HandleFailure(string reason = "")
        {
            // 检查是否超过最大重试次数
            if (retryCount >= MAX_RETRY_COUNT)
            {
                WulaLog.Debug($"GlobalResourceCheck: Max retry count ({MAX_RETRY_COUNT}) reached for {resourceDef.defName}. Reason: {reason}");
                hasFailed = true;

                // 发送失败信号
                if (!failSignal.NullOrEmpty())
                {
                    Find.SignalManager.SendSignal(new Signal(failSignal));
                    WulaLog.Debug($"GlobalResourceCheck: Sent fail signal '{failSignal}' after max retries");
                }

                Complete();
                return;
            }

            // 安排下次重试
            ScheduleRetry(reason);
        }

        private void ScheduleRetry(string reason = "")
        {
            nextRetryTick = Find.TickManager.TicksGame + retryDelayTicks;
            
            // 记录重试信息
            WulaLog.Debug($"GlobalResourceCheck: Scheduled retry #{retryCount + 1} in {retryDelayTicks} ticks for {requiredCount} {resourceDef.defName}. Reason: {reason}");
        }

        public override void ExposeData()
        {
            base.ExposeData();
            
            Scribe_Defs.Look(ref resourceDef, "resourceDef");
            Scribe_Values.Look(ref requiredCount, "requiredCount");
            Scribe_Values.Look(ref retryDelayTicks, "retryDelayTicks");
            Scribe_Values.Look(ref successSignal, "successSignal");
            Scribe_Values.Look(ref failSignal, "failSignal");
            Scribe_Values.Look(ref deductOnSuccess, "deductOnSuccess");
            Scribe_Values.Look(ref useInputStorage, "useInputStorage");
            Scribe_Values.Look(ref debugInfo, "debugInfo");
            
            Scribe_Values.Look(ref nextRetryTick, "nextRetryTick");
            Scribe_Values.Look(ref hasSucceeded, "hasSucceeded");
            Scribe_Values.Look(ref hasFailed, "hasFailed");
            Scribe_Values.Look(ref retryCount, "retryCount");
        }

        public override string DescriptionPart
        {
            get
            {
                if (State != QuestPartState.Enabled)
                    return "WULA_TaxCollection.Waiting".Translate(); // 等待激活
                    
                string statusKey = hasSucceeded ? "WULA_TaxCollection.Status.Succeeded" :
                                   hasFailed ? "WULA_TaxCollection.Status.Failed" :
                                   "";
                
                string statusText;
                if (hasSucceeded || hasFailed)
                {
                    statusText = statusKey.Translate();
                }
                else
                {
                    // 只有在调试模式下才显示下一次检查的tick
                    if (Prefs.DevMode)
                    {
                        statusText = statusKey.Translate(retryCount, Math.Max(0, nextRetryTick - Find.TickManager.TicksGame));
                    }
                    else
                    {
                        statusText = statusKey.Translate(retryCount);
                    }
                }

                // 硬编码状态文本颜色为 #820D13
                string coloredStatusText = $"<color=#820D13>{statusText}</color>";
                
                return "WULA_TaxCollection.Status".Translate(requiredCount, resourceDef?.label ?? "NULL", coloredStatusText);
            }
        }
    }
}
