// JobDriver_HaulToMaintenancePod.cs (修复版)
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    public class JobDriver_HaulToMaintenancePod : JobDriver
    {
        private const TargetIndex TakeeIndex = TargetIndex.A;
        private const TargetIndex PodIndex = TargetIndex.B;

        protected Pawn Takee => (Pawn)job.GetTarget(TakeeIndex).Thing;
        protected Building Pod => (Building)job.GetTarget(PodIndex).Thing;
        protected CompMaintenancePod PodComp => Pod?.TryGetComp<CompMaintenancePod>();

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // 修复：明确指定计数为1
            return pawn.Reserve(Takee, job, 1, -1, null, errorOnFailed) 
                && pawn.Reserve(Pod, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TakeeIndex);
            this.FailOnDestroyedOrNull(PodIndex);
            this.FailOn(() => PodComp == null || PodComp.State != MaintenancePodState.Idle);

            // 前往目标 pawn
            yield return Toils_Goto.GotoThing(TakeeIndex, PathEndMode.ClosestTouch);

            // 开始搬运 - 修复计数问题
            yield return new Toil
            {
                initAction = () =>
                {
                    // 明确设置搬运数量为1
                    if (pawn.carryTracker.CarriedThing == null)
                    {
                        if (Takee == null || Takee.Destroyed)
                        {
                            WulaLog.Debug("试图搬运不存在的Pawn");
                            return;
                        }

                        // 使用TryStartCarryThing并明确指定数量
                        if (pawn.carryTracker.TryStartCarry(Takee, 1) <= 0)
                        {
                            WulaLog.Debug($"无法搬运Pawn: {Takee.Label}");
                            EndJobWith(JobCondition.Incompletable);
                        }
                    }
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };

            // 前往维护舱
            yield return Toils_Goto.GotoThing(PodIndex, PathEndMode.InteractionCell);

            // 放入维护舱
            yield return new Toil
            {
                initAction = () =>
                {
                    if (PodComp != null && Takee != null)
                    {
                        // 确保Pawn被放下
                        if (pawn.carryTracker.CarriedThing == Takee)
                        {
                            pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out _);
                        }
                        
                        PodComp.StartCycle(Takee);
                    }
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }
    }
}
