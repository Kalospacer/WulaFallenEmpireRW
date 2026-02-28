using System.Collections.Generic;
using Verse;
using Verse.AI;
using RimWorld;

namespace WulaFallenEmpire
{
    public class JobDriver_BoardMech : JobDriver
    {
        private const TargetIndex MechIndex = TargetIndex.A;
        
        private CompMechCrewHolder CrewComp => job.targetA.Thing?.TryGetComp<CompMechCrewHolder>();
        
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // 预留目标机甲
            if (!pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed))
            {
                return false;
            }
            return true;
        }
        
        protected override IEnumerable<Toil> MakeNewToils()
        {
            // 第0步：添加失败条件
            AddFailCondition(() =>
            {
                var mech = TargetThingA as Pawn;
                if (mech == null || mech.Destroyed || mech.Dead)
                    return true;
                    
                var comp = CrewComp;
                if (comp == null || comp.IsFull || !comp.CanAddCrew(pawn))
                    return true;
                    
                if (pawn.Downed || pawn.Dead)
                    return true;
                    
                return false;
            });
            
            // 第1步：走到机甲旁边
            yield return Toils_Goto.GotoThing(MechIndex, PathEndMode.Touch);
            
            // 第2步：等待短暂时间（可选）
            yield return Toils_General.Wait(10).WithProgressBarToilDelay(MechIndex);
            
            // 第3步：登上机甲
            Toil boardToil = new Toil();
            boardToil.initAction = () =>
            {
                var mech = TargetThingA as Pawn;
                if (mech == null)
                    return;
                    
                var comp = CrewComp;
                if (comp != null && comp.CanAddCrew(pawn))
                {
                    comp.AddCrew(pawn);
                }
            };
            boardToil.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return boardToil;
        }
    }
}
