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

            // 检查是否有足够的燃料（零部件）
            // 如果是强制工作（玩家右键），我们允许通过检查，让 JobDriver 去处理（可能会提示燃料不足）
            // 这样玩家能知道为什么不能工作，而不是默默失败
            if (!forced)
            {
                float requiredFuel = podComp.GetRequiredComponentsFor(pawn);
                var refuelable = t.TryGetComp<CompRefuelable>();
                if (refuelable != null && refuelable.Fuel < requiredFuel)
                {
                    JobFailReason.Is("WULA_MaintenancePod_NotEnoughComponents".Translate(requiredFuel.ToString("F0")));
                    return false;
                }
            }

            // 检查当前pawn是否有维护需求且需要维护
            return PawnNeedsMaintenance(pawn);
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            return JobMaker.MakeJob(Wula_JobDefOf.WULA_EnterMaintenancePod, t);
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
