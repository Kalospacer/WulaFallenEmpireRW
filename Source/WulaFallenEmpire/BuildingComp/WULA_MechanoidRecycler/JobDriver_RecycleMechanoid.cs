using RimWorld;
using Verse;
using Verse.AI;
using System.Collections.Generic;

namespace WulaFallenEmpire
{
    public class JobDriver_RecycleMechanoid : JobDriver
    {
        private Building_MechanoidRecycler Recycler => job.targetA.Thing as Building_MechanoidRecycler;
        
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        }
        
        protected override IEnumerable<Toil> MakeNewToils()
        {
            // 前往回收器
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell);
            
            // 进入回收器
            yield return new Toil
            {
                initAction = () =>
                {
                    if (Recycler != null)
                    {
                        Recycler.AcceptMechanoid(pawn);
                    }
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }
    }
}
