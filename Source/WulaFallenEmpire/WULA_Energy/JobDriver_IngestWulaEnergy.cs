using System.Collections.Generic;
using Verse;
using Verse.AI;
using RimWorld;

namespace WulaFallenEmpire
{
    public class JobDriver_IngestWulaEnergy : JobDriver
    {
        private bool eatingFromInventory;

        private const TargetIndex IngestibleSourceInd = TargetIndex.A;

        private Thing IngestibleSource => job.GetTarget(IngestibleSourceInd).Thing;

        private float ChewDurationMultiplier
        {
            get
            {
                Thing ingestibleSource = IngestibleSource;
                if (ingestibleSource.def.ingestible != null)
                {
                    return 1f / pawn.GetStatValue(StatDefOf.EatingSpeed);
                }
                return 1f;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref eatingFromInventory, "eatingFromInventory", defaultValue: false);
        }

        public override void Notify_Starting()
        {
            base.Notify_Starting();
            eatingFromInventory = pawn.inventory != null && pawn.inventory.Contains(IngestibleSource);
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (pawn.Faction != null)
            {
                Thing ingestibleSource = IngestibleSource;
                int maxAmountToPickup = FoodUtility.GetMaxAmountToPickup(ingestibleSource, pawn, job.count);
                if (!pawn.Reserve(ingestibleSource, job, 10, maxAmountToPickup, null, errorOnFailed))
                {
                    return false;
                }
                job.count = maxAmountToPickup;
            }
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => !IngestibleSource.Destroyed && !IngestibleSource.IngestibleNow);

            Toil chew = Toils_Ingest.ChewIngestible(pawn, ChewDurationMultiplier, IngestibleSourceInd, TargetIndex.None)
                .FailOn((Toil x) => !IngestibleSource.Spawned && (pawn.carryTracker == null || pawn.carryTracker.CarriedThing != IngestibleSource))
                .FailOnCannotTouch(IngestibleSourceInd, PathEndMode.Touch);

            foreach (Toil item in PrepareToIngestToils(chew))
            {
                yield return item;
            }

            yield return chew;

            // Custom Finalize Ingest Logic
            Toil finalizeToil = new Toil();
            finalizeToil.initAction = delegate
            {
                Pawn ingester = pawn;
                Thing ingestible = IngestibleSource;
                if (ingester == null || ingestible == null)
                {
                    return;
                }

                // If it's not an energy core, use the default vanilla method for safety, though this job should only target energy cores.
                if (ingestible.def.defName != "WULA_Charge_Cube")
                {
                    ingester.needs.food.CurLevel += FoodUtility.GetNutrition(ingester, ingestible, ingestible.def);
                }
                else
                {
                    // Our custom logic for energy core
                    // 1. Apply the charging hediff
                    Hediff hediff = HediffMaker.MakeHediff(HediffDef.Named("WULA_ChargingHediff"), ingester);
                    hediff.Severity = 1.0f;
                    ingester.health.AddHediff(hediff);

                    // 2. Spawn the used core
                    Thing usedCore = ThingMaker.MakeThing(ThingDef.Named("WULA_Charge_Cube_No_Power"));
                    GenPlace.TryPlaceThing(usedCore, ingester.Position, ingester.Map, ThingPlaceMode.Near);
                }

                // Destroy the original food item
                if (!ingestible.Destroyed)
                {
                    ingestible.Destroy();
                }
            };
            finalizeToil.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return finalizeToil;
        }

        private IEnumerable<Toil> PrepareToIngestToils(Toil chewToil)
        {
            if (eatingFromInventory)
            {
                yield return Toils_Misc.TakeItemFromInventoryToCarrier(pawn, IngestibleSourceInd);
            }
            else
            {
                yield return Toils_Goto.GotoThing(IngestibleSourceInd, PathEndMode.ClosestTouch).FailOnDespawnedNullOrForbidden(IngestibleSourceInd);
                yield return Toils_Ingest.PickupIngestible(IngestibleSourceInd, pawn);
            }
        }
    }
}
