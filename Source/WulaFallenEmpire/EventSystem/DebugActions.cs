using System.Collections.Generic;
using LudeonTK;
using Verse;
using RimWorld;

namespace WulaFallenEmpire
{
    public static class WulaDebugActions
    {
        [DebugAction("Wula Fallen Empire", "Open Custom UI...", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.Playing)]
        private static void OpenCustomUI()
        {
            List<DebugMenuOption> list = new List<DebugMenuOption>();
            foreach (EventDef localDef in DefDatabase<EventDef>.AllDefs)
            {
                EventDef currentDef = localDef;
                list.Add(new DebugMenuOption(currentDef.defName, DebugMenuOptionMode.Action, delegate
                {
                    Find.WindowStack.Add(new Dialog_CustomDisplay(currentDef));
                }));
            }
            Find.WindowStack.Add(new Dialog_DebugOptionListLister(list));
        }
    }

    public static class WulaDebugActionsVariables
    {
        [DebugAction("Wula Fallen Empire", "Manage Event Variables", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ManageEventVariables()
        {
            Find.WindowStack.Add(new Dialog_ManageEventVariables());
        }
    }
}