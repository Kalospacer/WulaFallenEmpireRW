using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Verse;
using RimWorld;

namespace WulaFallenEmpire.Patches
{
    [HarmonyPatch(typeof(ThingDefGenerator_Techprints))]
    [HarmonyPatch("ImpliedTechprintDefs")]
    public static class Patch_ThingDefGenerator_Techprints_ImpliedTechprintDefs_Postfix
    {
        private static readonly HashSet<string> BlockedTechprints = new HashSet<string>
        {
            "Techprint_WULA_Colony_License_LV1_Technology",
            "Techprint_WULA_Colony_License_LV2_Technology",
            "Techprint_WULA_Colony_License_LV3_Technology"
        };

        [HarmonyPostfix]
        public static IEnumerable<ThingDef> Postfix(IEnumerable<ThingDef> __result)
        {
            foreach (ThingDef thingDef in __result)
            {
                if (thingDef?.defName != null && BlockedTechprints.Contains(thingDef.defName))
                {
                    continue; // 跳过被阻止的科技蓝图
                }
                yield return thingDef;
            }
        }
    }
}
