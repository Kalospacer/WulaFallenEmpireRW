using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    [HarmonyPatch(typeof(Pawn_JobTracker), "StartJob")]
    public static class Patch_Pawn_JobTracker_StartJob
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn_JobTracker __instance, Pawn ___pawn)
        {
            // 检查是否是移动相关的任务
            if (___pawn?.Map == null || ___pawn.Dead || ___pawn.Downed)
                return;
                
            if (__instance.curJob == null)
                return;
                
            // 只处理移动任务
            if (__instance.curJob.def == JobDefOf.Goto || 
                __instance.curJob.def == JobDefOf.GotoWander ||
                __instance.curJob.def == JobDefOf.Follow)
            {
                // 通知区域传送器检查这个Pawn
                NotifyAreaTeleporters(___pawn);
            }
        }

        private static void NotifyAreaTeleporters(Pawn pawn)
        {
            if (pawn.Map == null)
                return;
                
            // 查找范围内的所有区域传送器
            foreach (var thing in pawn.Map.listerThings.ThingsOfDef(ThingDef.Named("WULA_Support_AreaTeleporter")))
            {
                var comp = thing.TryGetComp<ThingComp_AreaTeleporter>();
                if (comp != null && pawn.Position.DistanceTo(thing.Position) <= comp.Props.teleportRadius)
                {
                    // 强制立即检查这个Pawn
                    comp.ForceCheckPawn(pawn);
                }
            }
        }
    }

    // 为ThingComp_AreaTeleporter添加强制检查方法
    public partial class ThingComp_AreaTeleporter
    {
        public void ForceCheckPawn(Pawn pawn)
        {
            if (parent == null || !parent.Spawned || parent.Map == null)
                return;
                
            if (pawn.Position.DistanceTo(parent.Position) > Props.teleportRadius)
                return;
                
            if (!ShouldAffectPawn(pawn))
                return;
                
            TryTeleportPawn(pawn);
        }
    }
}
