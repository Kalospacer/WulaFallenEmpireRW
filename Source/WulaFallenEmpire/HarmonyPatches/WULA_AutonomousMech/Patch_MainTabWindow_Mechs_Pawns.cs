using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace WulaFallenEmpire
{
    [HarmonyPatch(typeof(MainTabWindow_Mechs), "Pawns", MethodType.Getter)]
    public static class Patch_MainTabWindow_Mechs_Pawns
    {
        [HarmonyPostfix]
        public static void Postfix(ref IEnumerable<Pawn> __result)
        {
            // 获取所有自主机械体
            var autonomousMechs = Find.CurrentMap.mapPawns.PawnsInFaction(Faction.OfPlayer)
                .Where(p => p.RaceProps.IsMechanoid && p.GetComp<CompAutonomousMech>()?.CanBeAutonomous == true);

            // 将自主机械体合并到结果中，并去重
            __result = __result.Concat(autonomousMechs).Distinct();
        }
    }
}