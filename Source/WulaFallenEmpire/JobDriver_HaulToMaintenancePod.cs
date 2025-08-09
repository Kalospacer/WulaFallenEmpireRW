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
            // Standard failure conditions
            this.FailOnDestroyedOrNull(TakeeIndex);
            this.FailOnDestroyedOrNull(PodIndex);
            this.FailOnAggroMentalStateAndHostile(TakeeIndex);
            this.FailOn(() => PodComp == null); // Fail if the pod doesn't have our component
            this.FailOn(() => !pawn.CanReach(Pod, PathEndMode.InteractionCell, Danger.Deadly));
            this.FailOn(() => !Takee.Downed); // Can only haul downed pawns

            // Go to the pawn to be rescued
            yield return Toils_Goto.GotoThing(TakeeIndex, PathEndMode.ClosestTouch)
                .FailOnDespawnedNullOrForbidden(TakeeIndex)
                .FailOnDespawnedNullOrForbidden(PodIndex)
                .FailOnSomeonePhysicallyInteracting(TakeeIndex);

            // Start carrying the pawn
            yield return Toils_Haul.StartCarryThing(TakeeIndex, false, true, false);

            // Go to the maintenance pod
            yield return Toils_Goto.GotoThing(PodIndex, PathEndMode.InteractionCell);

            // Place the pawn inside the pod
            Toil placeInPod = ToilMaker.MakeToil("PlaceInPod");
            placeInPod.initAction = delegate
            {
                // The Comp will handle despawning the pawn and starting the cycle
                PodComp.StartCycle(Takee);
            };
            placeInPod.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return placeInPod;
        }
    }
}