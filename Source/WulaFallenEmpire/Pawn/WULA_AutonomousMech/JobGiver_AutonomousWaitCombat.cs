// JobGiver_AutonomousWaitCombat.cs
using RimWorld;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    public class JobGiver_AutonomousWaitCombat : ThinkNode_JobGiver
    {
        public bool canUseRangedWeapon = true;
        
        public override float GetPriority(Pawn pawn)
        {
            // 只有在征召状态下才有高优先级
            if (pawn.drafter?.Drafted == true)
            {
                return 9.5f; // 比常规工作低，但比空闲高
            }
            return 0f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            // 只有在征召状态下才使用 Wait_Combat
            if (pawn.drafter?.Drafted == true && 
                pawn.GetComp<CompAutonomousMech>()?.CanWorkAutonomously == true)
            {
                Job job = JobMaker.MakeJob(JobDefOf.Wait_Combat);
                job.canUseRangedWeapon = canUseRangedWeapon;
                job.expiryInterval = 30; // 短时间，便于重新评估
                return job;
            }
            
            return null;
        }
    }
}
