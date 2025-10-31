// JobGiver_AutonomousCombat.cs
using RimWorld;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    public class JobGiver_AutonomousCombat : ThinkNode_JobGiver
    {
        public float priority = 8f;
        public int expiryInterval = 30;

        public override float GetPriority(Pawn pawn)
        {
            // 只有在征召状态下才有优先级
            if (pawn.drafter?.Drafted == true && 
                pawn.GetComp<CompAutonomousMech>()?.CanWorkAutonomously == true)
            {
                return priority;
            }
            return 0f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            // 确保是自主机械族且被征召
            var comp = pawn.GetComp<CompAutonomousMech>();
            if (comp?.CanWorkAutonomously != true || pawn.drafter?.Drafted != true)
                return null;

            // 创建自主战斗工作
            Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("WULA_AutonomousWaitCombat"));
            job.expiryInterval = expiryInterval;
            job.checkOverrideOnDamage = true;
            
            // 设置工作标签，确保不会被其他工作干扰
            pawn.mindState?.prioritizedWork?.Clear();

            return job;
        }
    }
}
