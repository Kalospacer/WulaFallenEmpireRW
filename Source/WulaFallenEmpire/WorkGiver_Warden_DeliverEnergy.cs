using RimWorld;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    public class WorkGiver_Warden_DeliverEnergy : WorkGiver_Scanner
    {
        private WorkGiverDefExtension_FeedWula ext;

        private WorkGiverDefExtension_FeedWula Ext
        {
            get
            {
                if (ext == null)
                {
                    ext = def.GetModExtension<WorkGiverDefExtension_FeedWula>();
                }
                return ext;
            }
        }

        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForDef(ThingDef.Named("WulaSpecies"));

        public override PathEndMode PathEndMode => PathEndMode.ClosestTouch;

        public override Danger MaxPathDanger(Pawn pawn) => Danger.Deadly;

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            Pawn prisoner = t as Pawn;

            if (prisoner == null || prisoner == pawn || !prisoner.IsPrisonerOfColony || !prisoner.guest.CanBeBroughtFood)
            {
                return false;
            }

            Need_WulaEnergy energyNeed = prisoner.needs.TryGetNeed<Need_WulaEnergy>();
            if (energyNeed == null)
            {
                return false;
            }

            if (energyNeed.CurLevelPercentage > Ext.feedThreshold)
            {
                return false;
            }

            if (WardenFeedUtility.ShouldBeFed(prisoner))
            {
                return false;
            }

            if (!pawn.CanReserveAndReach(prisoner, PathEndMode.Touch, Danger.Deadly, 1, -1, null, forced))
            {
                return false;
            }

            if (Ext == null || Ext.energySourceDef == null)
            {
                Log.ErrorOnce("WorkGiver_Warden_DeliverEnergy is missing the DefModExtension with a valid energySourceDef.", def.GetHashCode());
                return false;
            }

            if (!FindBestEnergySourceFor(pawn, prisoner, out _, out _))
            {
                JobFailReason.Is("NoFood".Translate());
                return false;
            }

            return true;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            Pawn prisoner = (Pawn)t;
            if (FindBestEnergySourceFor(pawn, prisoner, out Thing energySource, out _))
            {
                Job job = JobMaker.MakeJob(JobDefOf.DeliverFood, energySource, prisoner);
                job.count = 1;
                job.targetC = RCellFinder.SpotToChewStandingNear(prisoner, energySource);
                return job;
            }
            return null;
        }

        private bool FindBestEnergySourceFor(Pawn getter, Pawn eater, out Thing foodSource, out ThingDef foodDef)
        {
            foodSource = null;
            foodDef = null;

            if (Ext == null || Ext.energySourceDef == null)
            {
                return false;
            }

            // Check if there's already an energy source in the eater's room that the eater can reach and use.
            Thing existingEnergyInRoom = GenClosest.ClosestThingReachable(
                eater.Position, // Start search from eater's position
                eater.Map,
                ThingRequest.ForDef(Ext.energySourceDef),
                PathEndMode.OnCell,
                TraverseParms.For(eater, Danger.Deadly, TraverseMode.ByPawn, false), // Use eater's traverse parms
                9999f,
                (Thing x) => !x.IsForbidden(eater) && eater.CanReserve(x) && x.GetRoom() == eater.GetRoom()
            );

            if (existingEnergyInRoom != null)
            {
                // If there's already an energy source in the room, no need for the warden to bring another.
                return false;
            }

            // Search for an energy source anywhere, now that we've confirmed none are in the room.
            foodSource = GenClosest.ClosestThingReachable(
                getter.Position,
                getter.Map,
                ThingRequest.ForDef(Ext.energySourceDef),
                PathEndMode.OnCell,
                TraverseParms.For(getter),
                9999f,
                (Thing x) => !x.IsForbidden(getter) && getter.CanReserve(x)
            );

            if (foodSource != null)
            {
                foodDef = foodSource.def;
                return true;
            }

            return false;
        }
    }
}
