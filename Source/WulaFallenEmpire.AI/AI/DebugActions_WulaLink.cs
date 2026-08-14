using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using LudeonTK;
using WulaFallenEmpire.EventSystem.AI.UI;

namespace WulaFallenEmpire.EventSystem.AI
{
    public static class DebugActions_WulaLink
    {
        [DebugAction("WulaLink", "Open WulaLink UI", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.Playing)]
        public static void OpenWulaLink()
        {
            // Find a suitable event def or create a generic one
            EventDef def = DefDatabase<EventDef>.AllDefs.FirstOrDefault();
            if (def == null)
            {
                Messages.Message("No EventDef found to initialize WulaLink.", MessageTypeDefOf.RejectInput, false);
                return;
            }

            Find.WindowStack.Add(new Overlay_WulaLink(def));
        }
    }
}
