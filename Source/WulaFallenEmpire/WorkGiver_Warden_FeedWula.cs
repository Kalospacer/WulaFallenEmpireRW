using RimWorld;
using Verse;
using Verse.AI;
using System.Linq;

namespace WulaFallenEmpire
{
    public class WorkGiver_Warden_FeedWula : WorkGiver_Warden
    {
        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Pawn prisoner) || !ShouldFeed(pawn, prisoner))
            {
                return null;
            }

            Need_WulaEnergy energyNeed = prisoner.needs.TryGetNeed<Need_WulaEnergy>();
            var extension = def.GetModExtension<WorkGiverDefExtension_FeedWula>();
            if (energyNeed == null || energyNeed.CurLevelPercentage >= extension.feedThreshold)
            {
                return null;
            }

            if (prisoner.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamed("WULA_ChargingHediff")))
            {
                return null;
            }

            // The prisoner must be in bed to be fed by a warden. If the job is not forced, they must also be unable to move.
            if (!prisoner.InBed() || (!forced && prisoner.health.capacities.CapableOf(PawnCapacityDefOf.Moving)))
            {
                return null;
            }

            if (!TryFindBestEnergySourceFor(pawn, prisoner, out Thing energySource, out _))
            {
                return null;
            }

            Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("WULA_FeedWulaPatient"), energySource, prisoner);
            job.count = 1;
            return job;
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
