using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    public class JobDriver_EnterMaintenancePod : JobDriver
    {
        private const TargetIndex PodIndex = TargetIndex.A;

        protected Thing Pod => job.GetTarget(PodIndex).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Pod, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(PodIndex);
            this.FailOnBurningImmobile(PodIndex);

            var podComp = Pod.TryGetComp<CompMaintenancePod>();
            this.FailOn(() => podComp == null || podComp.State != MaintenancePodState.Idle || !podComp.PowerOn);

            // Go to the pod's interaction cell
            yield return Toils_Goto.GotoThing(PodIndex, PathEndMode.InteractionCell);

            // Enter the pod
            yield return new Toil
            {
                initAction = () =>
                {
                    podComp.StartCycle(pawn);
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }
    }
}