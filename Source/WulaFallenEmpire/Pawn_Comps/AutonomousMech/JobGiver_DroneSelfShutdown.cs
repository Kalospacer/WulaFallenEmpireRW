using RimWorld;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    public class JobGiver_DroneSelfShutdown : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (!RCellFinder.TryFindNearbyMechSelfShutdownSpot(pawn.Position, pawn, pawn.Map, out var result, allowForbidden: true))
            {
                result = pawn.Position;
            }
            Job job = JobMaker.MakeJob(JobDefOf.SelfShutdown, result);
            job.forceSleep = true;
            return job;
        }
    }
}