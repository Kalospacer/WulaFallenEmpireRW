using RimWorld;
using Verse;
using Verse.AI;
using System.Linq;

namespace WulaFallenEmpire
{
    public class WorkGiver_Warden_FeedWula : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.Pawn);

        public override PathEndMode PathEndMode => PathEndMode.ClosestTouch;

        public override Danger MaxPathDanger(Pawn pawn) => Danger.Deadly;

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Pawn prisoner) || pawn == prisoner)
                return false;

            if (!ShouldFeed(pawn, prisoner))
                return false;

            Need_WulaEnergy energyNeed = prisoner.needs.TryGetNeed<Need_WulaEnergy>();
            var extension = def.GetModExtension<WorkGiverDefExtension_FeedWula>();
            if (energyNeed == null || energyNeed.CurLevelPercentage >= extension.feedThreshold)
                return false;

            if (prisoner.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamed("WULA_ChargingHediff")))
                return false;

            if (!prisoner.InBed() || (!forced && prisoner.health.capacities.CapableOf(PawnCapacityDefOf.Moving)))
                return false;

            if (!pawn.CanReserveAndReach(prisoner, PathEndMode.Touch, Danger.Deadly, 1, -1, null, forced))
                return false;

            if (!TryFindBestEnergySourceFor(pawn, prisoner, out _, out _))
            {
                JobFailReason.Is("NoWulaEnergyToFeed".Translate(prisoner.LabelShort, prisoner));
                return false;
            }

            return true;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            Pawn prisoner = (Pawn)t;
            if (TryFindBestEnergySourceFor(pawn, prisoner, out Thing energySource, out _))
            {
                Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("WULA_FeedWulaPatient"), energySource, prisoner);
                job.count = 1;
                return job;
            }
            return null;
        }

        private bool ShouldFeed(Pawn warden, Pawn prisoner)
        {
            return prisoner.IsPrisonerOfColony && prisoner.guest.CanBeBroughtFood && prisoner.needs.TryGetNeed<Need_WulaEnergy>() != null;
        }

        private bool TryFindBestEnergySourceFor(Pawn getter, Pawn eater, out Thing energySource, out ThingDef energyDef)
        {
            energySource = null;
            energyDef = null;

            var allowedThings = getter.Map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver)
                .Where(x => x.def.GetModExtension<ThingDefExtension_EnergySource>() != null);

            Thing thing = GenClosest.ClosestThing_Global(eater.Position, allowedThings, 99999f, 
                t => t.IngestibleNow && !t.IsForbidden(getter) && getter.CanReserve(t));

            if (thing != null)
            {
                energySource = thing;
                energyDef = thing.def;
                return true;
            }

            return false;
        }
    }
}
