using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace WulaFallenEmpire
{
    [HarmonyPatch(typeof(CaravanUIUtility), "AddPawnsSections")]
    public static class Patch_CaravanUIUtility_AddPawnsSections_Postfix
    {
        [HarmonyPostfix]
        public static void Postfix(TransferableOneWayWidget widget, List<TransferableOneWay> transferables)
        {
            // 筛选出所有拥有自主组件的机械体
            var autonomousMechs = transferables
                .Where(x => {
                    if (x.ThingDef.category != ThingCategory.Pawn) return false;
                    var pawn = x.AnyThing as Pawn;
                    return pawn != null && pawn.GetComp<CompAutonomousMech>() != null;
                })
                .ToList();

            // 如果找到了任何自主机械体，就为它们添加一个新的分组
            if (autonomousMechs.Any())
            {
                widget.AddSection("WULA_AutonomousMechsSection".Translate(), autonomousMechs);
                Log.Message($"[WULA] Postfix: Added 'Autonomous Mechs' section with {autonomousMechs.Count} mechs.");
            }
        }
    }
}