using RimWorld;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    public class WorkGiver_DoMaintenance : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForDef(ThingDef.Named("WULA_MaintenancePod"));

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            return pawn.CanReserve(t, 1, -1, null, forced);
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            return JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("WULA_EnterMaintenancePod"), t);
        }
    }
}