using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace WulaFallenEmpire
{
    [HarmonyPatch(typeof(Alert_SubjectHasNowOverseer), "GetReport")]
    public static class Patch_Alert_SubjectHasNowOverseer
    {
        [HarmonyPostfix]
        public static void Postfix(ref AlertReport __result)
        {
            if (!__result.active)
            {
                return;
            }

            // AlertReport 是一个结构体，没有 culprits 属性，需要通过 AllCulprits 获取
            // 并且 AlertReport 的字段是只读的或者不方便直接修改，所以我们需要重新构建
            
            List<GlobalTargetInfo> allCulprits = __result.AllCulprits.ToList();
            bool changed = false;

            for (int i = allCulprits.Count - 1; i >= 0; i--)
            {
                Pawn pawn = allCulprits[i].Thing as Pawn;
                if (pawn != null)
                {
                    var comp = pawn.GetComp<CompAutonomousMech>();
                    // 如果是自主机械体，且允许自主工作，则从警报列表中移除
                    if (comp != null && comp.CanBeAutonomous)
                    {
                        allCulprits.RemoveAt(i);
                        changed = true;
                    }
                }
            }

            // 如果列表发生了变化，重新生成 AlertReport
            if (changed)
            {
                if (allCulprits.Count > 0)
                {
                    __result = AlertReport.CulpritsAre(allCulprits);
                }
                else
                {
                    __result = AlertReport.Inactive;
                }
            }
        }
    }
}