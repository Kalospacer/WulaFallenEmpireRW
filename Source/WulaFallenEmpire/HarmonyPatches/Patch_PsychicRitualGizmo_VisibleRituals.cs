using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Verse;
using RimWorld;

namespace WulaFallenEmpire.HarmonyPatches
{
    [HarmonyPatch(typeof(PsychicRitualGizmo), "VisibleRituals")]
    public static class Patch_PsychicRitualGizmo_VisibleRituals
    {
        [HarmonyPostfix]
        public static List<PsychicRitualDef_InvocationCircle> Postfix(List<PsychicRitualDef_InvocationCircle> __result)
        {
            if (__result == null || __result.Count == 0)
            {
                return __result;
            }

            // Create a new list containing only the rituals that DO NOT have our custom tag.
            // This is a more robust way to ensure our custom rituals are filtered out.
            return __result.Where(ritualDef =>
            {
                var extension = ritualDef.GetModExtension<RitualTagExtension>();
                // Keep the ritual if it has no extension, or if the extension tag is null/empty.
                return extension == null || string.IsNullOrEmpty(extension.ritualTag);
            }).ToList();
        }
    }
}