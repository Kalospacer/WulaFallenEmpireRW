// JobDriver_EnterMaintenancePod.cs (更新版)
using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    public class JobDriver_EnterMaintenancePod : JobDriver
    {
        private const TargetIndex PodIndex = TargetIndex.A;

        protected Thing Pod => job.GetTarget(PodIndex).Thing;
        protected CompMaintenancePod PodComp => Pod?.TryGetComp<CompMaintenancePod>();

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Pod, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(PodIndex);
            this.FailOn(() => PodComp == null || PodComp.State != MaintenancePodState.Idle || !PodComp.PowerOn);

            // 移动到维护舱
            yield return Toils_Goto.GotoThing(PodIndex, PathEndMode.InteractionCell);

            // 进入维护舱
            yield return new Toil
            {
                initAction = () =>
                {
                    if (PodComp != null)
                    {
                        PodComp.StartCycle(pawn);
                    }
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }
    }
}
