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
        protected CompMaintenancePod PodComp => Pod.TryGetComp<CompMaintenancePod>();

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Takee, job, 1, -1, null, errorOnFailed) 
                && pawn.Reserve(Pod, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            Log.Warning($"[WulaPodDebug] JobDriver_HaulToMaintenancePod started. Hauler: {pawn.LabelShortCap}, Takee: {Takee.LabelShortCap}");
            // Standard failure conditions
            this.FailOnDestroyedOrNull(TakeeIndex);
            this.FailOnDestroyedOrNull(PodIndex);
            this.FailOnAggroMentalStateAndHostile(TakeeIndex);
            this.FailOn(() => PodComp == null);
            this.FailOn(() => !pawn.CanReach(Pod, PathEndMode.InteractionCell, Danger.Deadly));
            this.FailOn(() => !Takee.Downed);

            // Go to the pawn to be rescued
            Toil goToTakee = Toils_Goto.GotoThing(TakeeIndex, PathEndMode.ClosestTouch)
                .FailOnDespawnedNullOrForbidden(TakeeIndex)
                .FailOnDespawnedNullOrForbidden(PodIndex)
                .FailOnSomeonePhysicallyInteracting(TakeeIndex);
            goToTakee.AddPreInitAction(() => Log.Warning($"[WulaPodDebug] HaulJob: {pawn.LabelShortCap} is going to pick up {Takee.LabelShortCap}."));
            yield return goToTakee;

            // Start carrying the pawn
            Toil startCarrying = Toils_Haul.StartCarryThing(TakeeIndex, false, true, false);
            startCarrying.AddPreInitAction(() => Log.Warning($"[WulaPodDebug] HaulJob: {pawn.LabelShortCap} is now carrying {Takee.LabelShortCap}."));
            yield return startCarrying;

            // Go to the maintenance pod
            Toil goToPod = Toils_Goto.GotoThing(PodIndex, PathEndMode.InteractionCell);
            goToPod.AddPreInitAction(() => Log.Warning($"[WulaPodDebug] HaulJob: {pawn.LabelShortCap} is hauling {Takee.LabelShortCap} to the pod."));
            yield return goToPod;

            // Place the pawn inside the pod
            Toil placeInPod = ToilMaker.MakeToil("PlaceInPod");
            placeInPod.initAction = delegate
            {
                Log.Warning($"[WulaPodDebug] HaulJob: {pawn.LabelShortCap} has arrived and is placing {Takee.LabelShortCap} in the pod.");
                PodComp.StartCycle(Takee);
            };
            placeInPod.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return placeInPod;
        }
    }
}