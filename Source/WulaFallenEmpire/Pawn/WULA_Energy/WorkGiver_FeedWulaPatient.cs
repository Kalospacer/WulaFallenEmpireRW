using RimWorld;
using Verse;
using Verse.AI;
using System.Collections.Generic;
using System.Linq;

namespace WulaFallenEmpire
{
    public class WorkGiver_FeedWulaPatient : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.Pawn);

        public override PathEndMode PathEndMode => PathEndMode.ClosestTouch;

        public override Danger MaxPathDanger(Pawn pawn) => Danger.Deadly;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            if (pawn?.Map?.mapPawns?.AllPawns == null)
            {
                return Enumerable.Empty<Thing>();
            }

            return pawn.Map.mapPawns.AllPawns.Where(p =>
                p != null &&
                p.needs != null &&
                p.health != null &&
                p.needs.TryGetNeed<Need_WulaEnergy>() != null &&
                p.InBed());
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Pawn patient) || patient == pawn)
            {
                return false;
            }

            // 如果病患正在充能，则不需要喂食
            if (patient.health?.hediffSet == null)
            {
                return false;
            }

            if (patient.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamed("WULA_ChargingHediff")))
            {
                return false;
            }

            Need_WulaEnergy energyNeed = patient.needs?.TryGetNeed<Need_WulaEnergy>();
            var extension = def.GetModExtension<WorkGiverDefExtension_FeedWula>();
            if (energyNeed == null || extension == null || energyNeed.CurLevelPercentage >= extension.feedThreshold)
            {
                return false;
            }

            // A Wula patient should be fed if they are in bed. If the job is not forced, they must also be unable to move.
            if (!patient.InBed() || (!forced && patient.health.capacities.CapableOf(PawnCapacityDefOf.Moving)))
            {
                return false;
            }

            if (!pawn.CanReserveAndReach(patient, PathEndMode.Touch, Danger.Deadly, 1, -1, null, forced))
            {
                return false;
            }

            if (!TryFindBestEnergySourceFor(pawn, patient, out _, out _))
            {
                JobFailReason.Is("NoWulaEnergyToFeed".Translate(patient.LabelShort, patient));
                return false;
            }

            return true;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            Pawn patient = (Pawn)t;
            if (TryFindBestEnergySourceFor(pawn, patient, out Thing energySource, out _))
            {
                Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("WULA_FeedWulaPatient"), energySource, patient);
                job.count = 1;
                return job;
            }
            return null;
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
