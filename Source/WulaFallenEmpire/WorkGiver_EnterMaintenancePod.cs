using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    public class WorkGiver_EnterMaintenancePod : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForDef(ThingDef.Named("WULA_MaintenancePod"));

        public override PathEndMode PathEndMode => PathEndMode.InteractionCell;

        // This method now checks the severity of the hediff.
        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            var podDef = ThingDef.Named("WULA_MaintenancePod");
            var podProps = podDef.GetCompProperties<CompProperties_MaintenancePod>();
            if (podProps?.hediffToRemove == null) return true;

            Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(podProps.hediffToRemove);
            
            // Skip if no hediff or if severity is below the configured threshold.
            if (hediff == null || hediff.Severity < podProps.minSeverityToMaintain)
            {
                return true;
            }

            return false;
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Building building) || !building.Spawned || building.IsForbidden(pawn) || !pawn.CanReserve(building, 1, -1, null, forced))
            {
                return false;
            }

            var podComp = building.GetComp<CompMaintenancePod>();
            if (podComp == null || podComp.State != MaintenancePodState.Idle || !podComp.PowerOn)
            {
                return false;
            }

            float requiredComponents = podComp.RequiredComponents(pawn);
            if (podComp.storedComponents < requiredComponents)
            {
                JobFailReason.Is("WULA_MaintenancePod_NotEnoughComponents".Translate(requiredComponents.ToString("F0")));
                return false;
            }

            return true;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            return JobMaker.MakeJob(JobDefOf.WULA_EnterMaintenancePod, t);
        }
    }
}