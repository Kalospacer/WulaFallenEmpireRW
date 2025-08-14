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
            Log.Warning($"[WulaPodDebug] JobDriver_EnterMaintenancePod started for pawn: {pawn.LabelShortCap}");
            this.FailOnDespawnedNullOrForbidden(PodIndex);
            this.FailOnBurningImmobile(PodIndex);

            var podComp = Pod.TryGetComp<CompMaintenancePod>();
            this.FailOn(() => podComp == null || podComp.State != MaintenancePodState.Idle || !podComp.PowerOn);

            // Go to the pod's interaction cell
            Toil goToPod = Toils_Goto.GotoThing(PodIndex, PathEndMode.InteractionCell);
            goToPod.AddPreInitAction(() => Log.Warning($"[WulaPodDebug] EnterJob: Pawn {pawn.LabelShortCap} is going to the pod."));
            yield return goToPod;

            // Enter the pod
            Toil enterToil = new Toil
            {
                initAction = () =>
                {
                    Log.Warning($"[WulaPodDebug] EnterJob: Pawn {pawn.LabelShortCap} has arrived and is entering the pod.");
                    podComp.StartCycle(pawn);
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
            yield return enterToil;
        }
    }
}