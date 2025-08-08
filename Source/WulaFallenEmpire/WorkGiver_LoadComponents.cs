using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    public class WorkGiver_LoadComponents : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForDef(ThingDef.Named("WULA_MaintenancePod"));

        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Building building) || !building.Spawned || building.IsForbidden(pawn) || !pawn.CanReserve(building, 1, -1, null, forced))
            {
                return false;
            }

            var podComp = building.GetComp<CompMaintenancePod>();
            if (podComp == null || podComp.State != MaintenancePodState.Idle)
            {
                return false;
            }

            // We define a "needed" threshold. Let's say we want to keep at least 10 components stocked.
            // This prevents pawns from hauling one component at a time.
            const int desiredStockpile = 10;
            if (podComp.storedComponents >= desiredStockpile)
            {
                return false;
            }

            if (FindBestComponent(pawn, podComp) == null)
            {
                JobFailReason.Is("WULA_NoComponentsToLoad".Translate());
                return false;
            }

            return true;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            var podComp = t.GetComp<CompMaintenancePod>();
            Thing component = FindBestComponent(pawn, podComp);
            if (component == null)
            {
                return null;
            }
            return JobMaker.MakeJob(JobDefOf.WULA_LoadComponentsToMaintenancePod, t, component);
        }

        private Thing FindBestComponent(Pawn pawn, CompMaintenancePod pod)
        {
            ThingDef componentDef = pod.Props.componentDef;
            if (componentDef == null) return null;

            Predicate<Thing> validator = (Thing x) => !x.IsForbidden(pawn) && pawn.CanReserve(x);
            return GenClosest.ClosestThingReachable(pawn.Position, pawn.Map, ThingRequest.ForDef(componentDef), PathEndMode.ClosestTouch, TraverseParms.For(pawn), 9999f, validator);
        }
    }
}