using RimWorld;
using Verse;
using Verse.AI;
using System.Linq;

namespace WulaFallenEmpire
{
    public class WorkGiver_Warden_DeliverEnergy : WorkGiver_Warden
    {
        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Pawn prisoner) || !ShouldTakeCareOfPrisoner(pawn, prisoner))
            {
                return null;
            }

            Need_WulaEnergy wulaEnergyNeed = prisoner.needs.TryGetNeed<Need_WulaEnergy>();
            if (wulaEnergyNeed == null || wulaEnergyNeed.CurLevelPercentage > def.GetModExtension<WorkGiverDefExtension_FeedWula>().feedThreshold)
            {
                return null;
            }

            if (EnergyAvailableInRoomTo(prisoner))
            {
                return null;
            }

            if (!TryFindBestEnergySourceFor(pawn, prisoner, out Thing energySource, out _))
            {
                return null;
            }

            Job job = JobMaker.MakeJob(JobDefOf.DeliverFood, energySource, prisoner);
            job.count = 1;
            return job;
        }

        private bool EnergyAvailableInRoomTo(Pawn prisoner)
        {
            if (prisoner.GetRoom() == null)
            {
                return false;
            }
            
            var allThings = prisoner.GetRoom().ContainedAndAdjacentThings;
            foreach (Thing thing in allThings)
            {
                if (thing.def.GetModExtension<ThingDefExtension_EnergySource>() != null)
                {
                    return true;
                }
            }
            return false;
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
