using RimWorld;
using RimWorld.Planet;
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
        public string successSignal;
        public string failSignal;
        public bool deductOnSuccess = true;
        public bool useInputStorage = true;
        public string debugInfo;

        // 状态变量
        private int nextRetryTick = -1;
        private bool hasSucceeded = false;
        private bool hasFailed = false;
        private int retryCount = 0;
        private const int MAX_RETRY_COUNT = 1000;

        protected override void Enable(SignalArgs receivedArgs)
        {
            base.Enable(receivedArgs);
            
            // 激活时立即开始第一次检查
            nextRetryTick = Find.TickManager.TicksGame;
            Log.Message($"QuestPart_GlobalResourceCheck Enabled: Will check for {requiredCount} {resourceDef?.defName} in {(useInputStorage ? "Input" : "Output")} Storage");
        }

        public override void Notify_QuestSignalReceived(Signal signal)
        {
            base.Notify_QuestSignalReceived(signal);

            // 如果任务已经结束，停止所有操作
            if (quest.State != QuestState.Ongoing && quest.State != QuestState.NotYetAccepted)
            {
                DoCleanup();
                return;
            }
        }

        public override void QuestPartTick()
        {
            base.QuestPartTick();

            // 如果已经成功或失败，或者任务已结束，不再处理
            if (hasSucceeded || hasFailed || (quest.State != QuestState.Ongoing && quest.State != QuestState.NotYetAccepted))
            {
                DoCleanup();
                return;
            }

            // 检查是否到了重试时间
            if (Find.TickManager.TicksGame < nextRetryTick && nextRetryTick != -1)
                return;

            // 执行资源检查
            CheckGlobalResource();
        }

        private void CheckGlobalResource()
        {
            // 更新下次重试时间
            nextRetryTick = Find.TickManager.TicksGame + retryDelayTicks;
            retryCount++;

            // 获取全局资源储存器
            GlobalStorageWorldComponent globalStorage = Find.World.GetComponent<GlobalStorageWorldComponent>();
            if (globalStorage == null)
            {
                Log.Error("QuestPart_GlobalResourceCheck: GlobalStorageWorldComponent not found");
                HandleFailure("Global storage component missing");
                return;
            }

            if (resourceDef == null)
            {
                Log.Error("QuestPart_GlobalResourceCheck: resourceDef is null");
                HandleFailure("Resource definition is null");
                return;
            }

            // 检查资源是否足够
            int currentAmount = useInputStorage ? 
                globalStorage.GetInputStorageCount(resourceDef) : 
                globalStorage.GetOutputStorageCount(resourceDef);
                
            bool hasEnough = currentAmount >= requiredCount;

            Log.Message($"GlobalResourceCheck [{retryCount}]: {currentAmount}/{requiredCount} {resourceDef.defName} in {(useInputStorage ? "Input" : "Output")} Storage - Enough: {hasEnough}");

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
                    Log.Error($"QuestPart_GlobalResourceCheck: Failed to deduct {requiredCount} {resourceDef.defName} from {(useInputStorage ? "Input" : "Output")} Storage");
                }
            }

            Log.Message($"GlobalResourceCheck: SUCCESS - {(deductOnSuccess ? "Deducted" : "Found")} {requiredCount} {resourceDef.defName} from {(useInputStorage ? "Input" : "Output")} Storage");

            // 发送成功信号 - 使用 QuestGenUtility 来生成带任务前缀的信号
            if (!successSignal.NullOrEmpty())
            {
                string fullSignal = QuestGenUtility.HardcodedSignalWithQuestID(successSignal);
                Find.SignalManager.SendSignal(new Signal(fullSignal));
                Log.Message($"GlobalResourceCheck: Sent success signal '{fullSignal}'");
            }

            // 清理这个任务部分
            DoCleanup();
        }

        private void HandleFailure(string reason = "")
        {
            // 检查是否超过最大重试次数
            if (retryCount >= MAX_RETRY_COUNT)
            {
                Log.Warning($"GlobalResourceCheck: Max retry count ({MAX_RETRY_COUNT}) reached for {resourceDef.defName}. Reason: {reason}");
                hasFailed = true;

                // 发送失败信号
                if (!failSignal.NullOrEmpty())
                {
                    string fullSignal = QuestGenUtility.HardcodedSignalWithQuestID(failSignal);
                    Find.SignalManager.SendSignal(new Signal(fullSignal));
                    Log.Message($"GlobalResourceCheck: Sent fail signal '{fullSignal}' after max retries");
                }

                DoCleanup();
                return;
            }

            // 安排下次重试
            ScheduleRetry(reason);
        }

        private void ScheduleRetry(string reason = "")
        {
            nextRetryTick = Find.TickManager.TicksGame + retryDelayTicks;
            
            // 记录重试信息
            Log.Message($"GlobalResourceCheck: Scheduled retry #{retryCount + 1} in {retryDelayTicks} ticks for {requiredCount} {resourceDef.defName}. Reason: {reason}");
        }

        // 使用新名称避免与基类冲突
        private void DoCleanup()
        {
            // 标记为已完成，停止tick更新
            nextRetryTick = -1;
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

        public override string DescriptionPart
        {
            get
            {
                string status = hasSucceeded ? "SUCCEEDED" : 
                               hasFailed ? "FAILED" : 
                               $"CHECKING (retry #{retryCount}, next in {nextRetryTick - Find.TickManager.TicksGame} ticks)";
                
                return $"Tax Collection: {requiredCount} {resourceDef?.defName ?? "NULL"} - {status}";
            }
        }
    }
}
