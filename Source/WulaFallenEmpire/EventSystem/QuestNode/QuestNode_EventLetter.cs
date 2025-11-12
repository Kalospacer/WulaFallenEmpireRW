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
                Log.Error("[WulaFallenEmpire] QuestNode_EventLetter: eventDefName is not specified.");
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
            
            Log.Message($"[WulaFallenEmpire] QuestPart_EventLetter received signal: '{signal.tag}', waiting for: '{inSignal}'");
            
            if (signal.tag == inSignal)
            {
                Log.Message($"[WulaFallenEmpire] Signal matched! Opening EventDef: {eventDefName}");
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
                    Log.Error($"[WulaFallenEmpire] EventDef '{defName}' not found in DefDatabase.");
                    return;
                }

                if (eventDef.windowType == null)
                {
                    Log.Error($"[WulaFallenEmpire] EventDef '{defName}' has null windowType.");
                    return;
                }

                Log.Message($"[WulaFallenEmpire] Creating window instance for {defName} with type {eventDef.windowType}");
                Window window = (Window)Activator.CreateInstance(eventDef.windowType, eventDef);
                
                Log.Message($"[WulaFallenEmpire] Adding window to WindowStack");
                Find.WindowStack.Add(window);
                Log.Message($"[WulaFallenEmpire] Successfully opened EventDef window: {defName}");
            }
            catch (Exception ex)
            {
                Log.Error($"[WulaFallenEmpire] Error opening EventDef window '{defName}': {ex}");
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
