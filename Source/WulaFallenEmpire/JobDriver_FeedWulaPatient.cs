using System.Collections.Generic;
using Verse;
using Verse.AI;
using RimWorld;

namespace WulaFallenEmpire
{
    public class JobDriver_FeedWulaPatient : JobDriver
    {
        private const TargetIndex FoodSourceInd = TargetIndex.A;
        private const TargetIndex PatientInd = TargetIndex.B;

        protected Thing Food => job.GetTarget(FoodSourceInd).Thing;
        protected Pawn Patient => (Pawn)job.GetTarget(PatientInd).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (!pawn.Reserve(Patient, job, 1, -1, null, errorOnFailed))
            {
                return false;
            }
            if (!pawn.Reserve(Food, job, 1, -1, null, errorOnFailed))
            {
                return false;
            }
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(PatientInd);
            this.FailOn(() => !FeedPatientUtility.ShouldBeFed(Patient));

            if (pawn.inventory != null && pawn.inventory.Contains(Food))
            {
                yield return Toils_Misc.TakeItemFromInventoryToCarrier(pawn, FoodSourceInd);
            }
            else
            {
                yield return Toils_Goto.GotoThing(FoodSourceInd, PathEndMode.ClosestTouch).FailOnForbidden(FoodSourceInd);
                yield return Toils_Ingest.PickupIngestible(FoodSourceInd, pawn);
            }

            yield return Toils_Goto.GotoThing(PatientInd, PathEndMode.Touch);
            yield return Toils_Ingest.ChewIngestible(Patient, 1.5f, FoodSourceInd, TargetIndex.None).FailOnCannotTouch(PatientInd, PathEndMode.Touch);
            yield return Toils_Ingest.FinalizeIngest(Patient, FoodSourceInd);
        }
    }
}
