using RimWorld;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    public class JobGiver_NoPilot : ThinkNode_JobGiver
    {
        private const int WaitTime = 100;

        protected override Job TryGiveJob(Pawn pawn)
        {
            Job job = JobMaker.MakeJob(JobDefOf.Wait);
            job.expiryInterval = 100;
            return job;
        }
    }
}