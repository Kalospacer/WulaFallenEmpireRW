using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using Verse;
using RimWorld.QuestGen;

namespace WulaFallenEmpire
{
    public class QuestNode_CheckGlobalResource : QuestNode
    {
        // 输入参数
        public SlateRef<ThingDef> resourceDef;
        public SlateRef<int> requiredCount;
        public SlateRef<int> retryDelayTicks = 60;
        
        [NoTranslate]
        public SlateRef<string> successSignal;
        
        [NoTranslate]
        public SlateRef<string> failSignal;
        
        public SlateRef<bool> deductOnSuccess = true;
        public SlateRef<bool> useInputStorage = true;

        protected override bool TestRunInt(Slate slate)
        {
            if (resourceDef == null || resourceDef.GetValue(slate) == null)
            {
                Log.Error("QuestNode_CheckGlobalResource: resourceDef is null");
                return false;
            }

            if (requiredCount.GetValue(slate) <= 0)
            {
                Log.Error("QuestNode_CheckGlobalResource: requiredCount must be positive");
                return false;
            }

            var globalStorage = Find.World.GetComponent<GlobalStorageWorldComponent>();
            if (globalStorage == null)
            {
                Log.Error("QuestNode_CheckGlobalResource: GlobalStorageWorldComponent not found");
                return false;
            }

            return true;
        }

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            Quest quest = QuestGen.quest;

            ThingDef actualResourceDef = resourceDef.GetValue(slate);
            int actualRequiredCount = requiredCount.GetValue(slate);
            int actualRetryDelay = retryDelayTicks.GetValue(slate);
            string actualSuccessSignal = QuestGenUtility.HardcodedSignalWithQuestID(successSignal.GetValue(slate));
            string actualFailSignal = QuestGenUtility.HardcodedSignalWithQuestID(failSignal.GetValue(slate));
            bool actualDeductOnSuccess = deductOnSuccess.GetValue(slate);
            bool actualUseInputStorage = useInputStorage.GetValue(slate);

            // 创建调试信息
            string debugInfo = $"Checking {actualRequiredCount} {actualResourceDef?.defName ?? "NULL"} in {(actualUseInputStorage ? "Input" : "Output")} Storage with retry delay {actualRetryDelay}";

            // 添加任务部分
            QuestPart_GlobalResourceCheck part = new QuestPart_GlobalResourceCheck
            {
                resourceDef = actualResourceDef,
                requiredCount = actualRequiredCount,
                retryDelayTicks = actualRetryDelay,
                successSignal = actualSuccessSignal,
                failSignal = actualFailSignal,
                deductOnSuccess = actualDeductOnSuccess,
                useInputStorage = actualUseInputStorage,
                debugInfo = debugInfo,
                // 关键：设置激活信号为任务接受信号
                inSignalEnable = QuestGenUtility.HardcodedSignalWithQuestID(quest.InitiateSignal)
            };

            quest.AddPart(part);

            Log.Message($"QuestNode_CheckGlobalResource: Added resource check for {actualRequiredCount} {actualResourceDef.defName} in {(actualUseInputStorage ? "Input" : "Output")} Storage");
        }
    }
}
