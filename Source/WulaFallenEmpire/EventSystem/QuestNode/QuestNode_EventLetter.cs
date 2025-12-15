using RimWorld;
using RimWorld.QuestGen;
using System;
using Verse;

namespace WulaFallenEmpire
{
    public class QuestNode_EventLetter : QuestNode
    {
        [NoTranslate]
        public SlateRef<string> inSignal;
        
        public SlateRef<string> eventDefName;

        protected override bool TestRunInt(Slate slate)
        {
            return true;
        }

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            string signal = inSignal.GetValue(slate);
            string defName = eventDefName.GetValue(slate);

            if (defName.NullOrEmpty())
            {
                WulaLog.Debug("[WulaFallenEmpire] QuestNode_EventLetter: eventDefName is not specified.");
                return;
            }

            // 关键：使用 HardcodedSignalWithQuestID 处理信号
            string processedSignal = QuestGenUtility.HardcodedSignalWithQuestID(signal) ?? slate.Get<string>("inSignal");

            QuestPart_EventLetter questPart = new QuestPart_EventLetter();
            questPart.inSignal = processedSignal;
            questPart.eventDefName = defName;
            
            QuestGen.quest.AddPart(questPart);
        }
    }

    public class QuestPart_EventLetter : QuestPart
    {
        public string inSignal;
        public string eventDefName;

        public override void Notify_QuestSignalReceived(Signal signal)
        {
            base.Notify_QuestSignalReceived(signal);
            
            WulaLog.Debug($"[WulaFallenEmpire] QuestPart_EventLetter received signal: '{signal.tag}', waiting for: '{inSignal}'");
            
            if (signal.tag == inSignal)
            {
                WulaLog.Debug($"[WulaFallenEmpire] Signal matched! Opening EventDef: {eventDefName}");
                OpenEventDefWindow(eventDefName);
            }
        }

        private void OpenEventDefWindow(string defName)
        {
            try
            {
                EventDef eventDef = DefDatabase<EventDef>.GetNamed(defName, false);
                if (eventDef == null)
                {
                    WulaLog.Debug($"[WulaFallenEmpire] EventDef '{defName}' not found in DefDatabase.");
                    return;
                }

                if (eventDef.windowType == null)
                {
                    WulaLog.Debug($"[WulaFallenEmpire] EventDef '{defName}' has null windowType.");
                    return;
                }

                WulaLog.Debug($"[WulaFallenEmpire] Creating window instance for {defName} with type {eventDef.windowType}");
                Window window = (Window)Activator.CreateInstance(eventDef.windowType, eventDef);
                
                WulaLog.Debug($"[WulaFallenEmpire] Adding window to WindowStack");
                Find.WindowStack.Add(window);
                WulaLog.Debug($"[WulaFallenEmpire] Successfully opened EventDef window: {defName}");
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[WulaFallenEmpire] Error opening EventDef window '{defName}': {ex}");
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref inSignal, "inSignal");
            Scribe_Values.Look(ref eventDefName, "eventDefName");
        }
    }
}
