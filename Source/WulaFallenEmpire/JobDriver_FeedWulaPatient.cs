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
            // The job should fail if the patient is no longer in bed.
            this.FailOn(() => !Patient.InBed());

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
            
            // Custom Finalize Ingest Logic
            Toil finalizeToil = new Toil();
            finalizeToil.initAction = delegate
            {
                Pawn patient = Patient;
                Thing food = Food;
                if (patient == null || food == null)
                {
                    return;
                }

                // If it's not an energy core, use the default vanilla method.
                if (food.def.defName != "WULA_Charge_Cube")
                {
                    patient.needs.food.CurLevel += FoodUtility.GetNutrition(patient, food, food.def);
                }
                else
                {
                    // Our custom logic for energy core
                    // 1. Apply the charging hediff
                    Hediff hediff = HediffMaker.MakeHediff(HediffDef.Named("WULA_ChargingHediff"), patient);
                    hediff.Severity = 1.0f;
                    patient.health.AddHediff(hediff);

                    // 2. Spawn the used core
                    Thing usedCore = ThingMaker.MakeThing(ThingDef.Named("WULA_Charge_Cube_No_Power"));
                    GenPlace.TryPlaceThing(usedCore, pawn.Position, pawn.Map, ThingPlaceMode.Near);
                }

                // Destroy the food item (it has been carried by the feeder)
                if (!food.Destroyed)
                {
                    food.Destroy();
                }
            };
            finalizeToil.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return finalizeToil;
        }
    }
}
