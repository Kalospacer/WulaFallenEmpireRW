// WorkGiver_DoMaintenance.cs (修复版)
using RimWorld;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    public class WorkGiver_DoMaintenance : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => 
            ThingRequest.ForDef(ThingDef.Named("WULA_MaintenancePod"));

        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            // 检查维护舱是否可用
            if (!pawn.CanReserve(t, 1, -1, null, forced))
                return false;

            var podComp = t.TryGetComp<CompMaintenancePod>();
            if (podComp == null || podComp.State != MaintenancePodState.Idle || !podComp.PowerOn)
                return false;

            // 检查当前pawn是否有维护需求且需要维护
            return PawnNeedsMaintenance(pawn);
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            return JobMaker.MakeJob(JobDefOf_WULA.WULA_EnterMaintenancePod, t);
        }

        // 检查单个Pawn是否需要维护
        private bool PawnNeedsMaintenance(Pawn pawn)
        {
            // 检查是否有维护需求组件
            var maintenanceNeed = pawn.needs?.TryGetNeed<Need_Maintenance>();
            if (maintenanceNeed == null)
            {
                // 这个Pawn没有维护需求，不应该使用维护舱
                return false;
            }

            // 检查维护水平是否低于阈值
            return maintenanceNeed.CurLevel <= 0.3f; // 需要维护的阈值
        }
    }
}
