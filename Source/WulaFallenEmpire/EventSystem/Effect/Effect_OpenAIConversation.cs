using System;
using RimWorld;
using Verse;
using WulaFallenEmpire.EventSystem.AI;
using WulaFallenEmpire.EventSystem.AI.UI;

namespace WulaFallenEmpire
{
    public class Effect_OpenAIConversation : EffectBase
    {
        public string defName;

        public override void Execute(Window dialog = null)
        {
            if (!RimTalkBridge.IsRimTalkActive)
            {
                Messages.Message("RimTalk mod is not active. AI conversation cannot be started.", MessageTypeDefOf.RejectInput, false);
                return;
            }

            EventDef eventDef = DefDatabase<EventDef>.GetNamed(defName, false);
            if (eventDef != null)
            {
                Find.WindowStack.Add(new Dialog_AIConversation(eventDef));
            }
            else
            {
                Log.Error($"[WulaFallenEmpire] Effect_OpenAIConversation could not find EventDef named '{defName}'");
            }
        }
    }
}