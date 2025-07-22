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
            yield return Toils_Ingest.FinalizeIngest(pawn, IngestibleSourceInd);
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
