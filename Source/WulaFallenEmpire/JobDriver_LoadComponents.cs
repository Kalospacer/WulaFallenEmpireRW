using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    public class JobDriver_LoadComponents : JobDriver
    {
        private const TargetIndex PodIndex = TargetIndex.A;
        private const TargetIndex ComponentIndex = TargetIndex.B;

        protected Thing Pod => job.GetTarget(PodIndex).Thing;
        protected Thing Component => job.GetTarget(ComponentIndex).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (pawn.Reserve(Pod, job, 1, -1, null, errorOnFailed))
            {
                return pawn.Reserve(Component, job, 1, -1, null, errorOnFailed);
            }
            return false;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(PodIndex);
            this.FailOnBurningImmobile(PodIndex);
            
            var podComp = Pod.TryGetComp<CompMaintenancePod>();
            this.FailOn(() => podComp == null || podComp.State != MaintenancePodState.Idle);

            // Go and get the components
            yield return Toils_Goto.GotoThing(ComponentIndex, PathEndMode.OnCell).FailOnSomeonePhysicallyInteracting(ComponentIndex);
            yield return Toils_Haul.StartCarryThing(ComponentIndex);

            // Carry them to the pod
            yield return Toils_Goto.GotoThing(PodIndex, PathEndMode.InteractionCell);

            // Load the components
            yield return Toils_General.WaitWith(60, TargetIndex.A, true, true, false, PodIndex);
            yield return new Toil
            {
                initAction = () =>
                {
                    podComp.AddComponents(this.GetActor().carryTracker.CarriedThing);
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }
    }
}