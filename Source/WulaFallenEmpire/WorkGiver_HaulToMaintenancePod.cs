using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    public class WorkGiver_HaulToMaintenancePod : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForDef(ThingDefOf_WULA.WULA_MaintenancePod);

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            return pawn.Map.listerBuildings.AllBuildingsColonistOfDef(ThingDefOf_WULA.WULA_MaintenancePod);
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Building pod) || !pawn.CanReserve(pod, 1, -1, null, forced))
            {
                return false;
            }

            var podComp = pod.GetComp<CompMaintenancePod>();
            if (podComp == null || podComp.State != MaintenancePodState.Idle || !podComp.PowerOn)
            {
                return false;
            }

            Pawn patient = FindPatientFor(pawn, podComp);
            return patient != null && pawn.CanReserve(patient, 1, -1, null, forced);
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            var pod = (Building)t;
            var podComp = pod.GetComp<CompMaintenancePod>();
            Pawn patient = FindPatientFor(pawn, podComp);
            if (patient == null)
            {
                return null;
            }
            return JobMaker.MakeJob(JobDefOf_WULA.WULA_HaulToMaintenancePod, patient, pod);
        }

        private Pawn FindPatientFor(Pawn rescuer, CompMaintenancePod podComp)
        {
            return rescuer.Map.mapPawns.AllPawnsSpawned
                .Where(p => p.def == ThingDefOf_WULA.Wula &&
                               p.Faction == rescuer.Faction &&
                               !p.IsForbidden(rescuer) &&
                               p.Downed && // Key condition: pawn cannot walk
                               p.health.hediffSet.HasHediff(podComp.Props.hediffToRemove) &&
                               podComp.RequiredComponents(p) <= podComp.parent.GetComp<CompRefuelable>().Fuel &&
                               rescuer.CanReserve(p))
                .OrderBy(p => p.Position.DistanceTo(rescuer.Position))
                .FirstOrDefault();
        }
    }
}