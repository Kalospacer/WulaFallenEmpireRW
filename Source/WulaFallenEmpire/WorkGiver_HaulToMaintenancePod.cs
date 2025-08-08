using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    public class WorkGiver_HaulToMaintenancePod : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForDef(ThingDefOf_WULA.WULA_MaintenancePod);

        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override Danger MaxPathDanger(Pawn pawn) => Danger.Deadly;

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Building building) || building.IsForbidden(pawn) || !pawn.CanReserve(building, 1, -1, null, forced))
            {
                return false;
            }

            CompMaintenancePod comp = building.GetComp<CompMaintenancePod>();
            if (comp == null || comp.State != MaintenancePodState.Idle)
            {
                return false;
            }

            // Check if it needs more components
            if (comp.storedComponents >= comp.capacity)
            {
                return false;
            }

            if (FindBestComponent(pawn, comp) == null)
            {
                JobFailReason.Is("WULA_NoComponentsToHaul".Translate());
                return false;
            }

            return true;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            Building building = (Building)t;
            CompMaintenancePod comp = building.GetComp<CompMaintenancePod>();
            
            Thing component = FindBestComponent(pawn, comp);
            if (component == null)
            {
                return null;
            }

            Job job = JobMaker.MakeJob(JobDefOf_WULA.WULA_HaulToMaintenancePod, component, t);
            job.count = Math.Min(component.stackCount, (int)(comp.capacity - comp.storedComponents));
            return job;
        }

        private Thing FindBestComponent(Pawn pawn, CompMaintenancePod podComp)
        {
            ThingFilter filter = podComp.GetStoreSettings().filter;
            
            Predicate<Thing> validator = (Thing x) => !x.IsForbidden(pawn) && pawn.CanReserve(x) && filter.Allows(x);
            
            return GenClosest.ClosestThingReachable(pawn.Position, pawn.Map, filter.BestThingRequest, PathEndMode.ClosestTouch, TraverseParms.For(pawn), 9999f, validator);
        }
    }
}