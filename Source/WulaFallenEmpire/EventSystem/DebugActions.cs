using System.Collections.Generic;
using Verse;
using RimWorld;
using LudeonTK;

namespace WulaFallenEmpire
{
    public static class WulaDebugActions
    {
        [DebugAction("Wula Fallen Empire", "Open Custom UI...", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.Playing)]
        private static void OpenCustomUI()
        {
            List<DebugMenuOption> list = new List<DebugMenuOption>();
            foreach (CustomUIDef localDef in DefDatabase<CustomUIDef>.AllDefs)
            {
                // Capture the local variable for the lambda
                CustomUIDef currentDef = localDef; 
                list.Add(new DebugMenuOption(currentDef.defName, DebugMenuOptionMode.Action, delegate
                {
                    Find.WindowStack.Add(new Dialog_CustomDisplay(currentDef));
                }));
            }
            Find.WindowStack.Add(new Dialog_DebugOptionListLister(list));
        }
    }
}
