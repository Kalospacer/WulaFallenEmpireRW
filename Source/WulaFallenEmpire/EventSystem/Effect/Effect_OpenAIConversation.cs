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
            // Check if API Key is configured in local settings
            if (string.IsNullOrEmpty(WulaFallenEmpireMod.settings.apiKey))
            {
                Messages.Message("AI API Key is not configured in Mod Settings. AI conversation cannot be started.", MessageTypeDefOf.RejectInput, false);
                return;
            }

            EventDef eventDef = DefDatabase<EventDef>.GetNamed(defName, false);
            if (eventDef != null)
            {
                var existing = Find.WindowStack.WindowOfType<Dialog_AIConversation>();
                if (existing != null)
                {
                    Find.WindowStack.Notify_ManuallySetFocus(existing);
                }
                else
                {
                    Find.WindowStack.Add(new Dialog_AIConversation(eventDef));
                }
            }
            else
            {
                WulaLog.Debug($"[WulaFallenEmpire] Effect_OpenAIConversation could not find EventDef named '{defName}'");
            }
        }
    }
    public class Effect_OpenWulaLink : EffectBase
    {
        public string defName;

        public override void Execute(Window dialog = null)
        {
            if (string.IsNullOrEmpty(WulaFallenEmpireMod.settings.apiKey))
            {
                Messages.Message("AI API Key is not configured in Mod Settings. AI conversation cannot be started.", MessageTypeDefOf.RejectInput, false);
                return;
            }

            EventDef eventDef = DefDatabase<EventDef>.GetNamed(defName, false);
            if (eventDef != null)
            {
                var existing = Find.WindowStack.WindowOfType<Overlay_WulaLink>();
                if (existing != null)
                {
                    existing.Expand();
                }
                else
                {
                    Find.WindowStack.Add(new Overlay_WulaLink(eventDef));
                }
            }
            else
            {
                WulaLog.Debug($"[WulaFallenEmpire] Effect_OpenWulaLink could not find EventDef named '{defName}'");
            }
        }
    }
}
