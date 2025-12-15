using HarmonyLib;
using RimWorld;
using RimWorld.Planet; // 关键修复
using System.Collections.Generic;
using Verse;

namespace WulaFallenEmpire
{
    [HarmonyPatch(typeof(RimWorld.Planet.CaravanFormingUtility), "AllSendablePawns")] // 关键修复
    public static class Patch_CaravanFormingUtility_AllSendablePawns
    {
        [HarmonyPostfix]
        public static void Postfix(Map map, ref List<Pawn> __result)
        {
            if (map == null)
            {
                return;
            }
            
            WulaLog.Debug("[WULA] Patch_CaravanFormingUtility_AllSendablePawns Postfix - Start checking for autonomous mechs...");

            // 遍历地图上所有的Pawn
            foreach (Pawn pawn in map.mapPawns.AllPawns)
            {
                // 检查是否是殖民地派系的机械体
                if (pawn.IsColonyMech)
                {
                    bool alreadyInList = __result.Contains(pawn);
                    var comp = pawn.GetComp<CompAutonomousMech>();
                    bool canBeAutonomous = comp != null && comp.CanBeAutonomous;

                    WulaLog.Debug($"[WULA] Checking Mech: {pawn.LabelCap}, Already in list: {alreadyInList}, Has CompAutonomousMech: {comp != null}, CanBeAutonomous: {canBeAutonomous}");

                    // 如果它是一个可以自主行动的机械体，但没有被原版方法包含，我们就添加它
                    if (!alreadyInList && canBeAutonomous)
                    {
                        __result.Add(pawn);
                        WulaLog.Debug($"[WULA] -> Added {pawn.LabelCap} to the list.");
                    }
                }
            }
            WulaLog.Debug("[WULA] Patch_CaravanFormingUtility_AllSendablePawns Postfix - Finished.");
        }
    }
}