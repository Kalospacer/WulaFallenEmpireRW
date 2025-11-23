using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    public class JobDriver_DroneSelfShutdown : JobDriver
    {
        public const TargetIndex RestSpotIndex = TargetIndex.A;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(base.TargetA, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);
            Toil layDown = SelfShutdown();
            layDown.PlaySoundAtStart(SoundDefOf.MechSelfShutdown);
            yield return layDown;
        }

        public static Toil SelfShutdown()
        {
            Toil layDown = ToilMaker.MakeToil("WULA_DroneSelfShutdown");
            layDown.initAction = delegate
            {
                Pawn actor = layDown.actor;
                actor.pather?.StopDead();
                JobDriver curDriver = actor.jobs.curDriver;
                actor.jobs.posture = PawnPosture.Standing;
                actor.mindState.lastBedDefSleptIn = null;
                curDriver.asleep = true;
            };
            layDown.defaultCompleteMode = ToilCompleteMode.Never;
            layDown.AddFinishAction(delegate
            {
                layDown.actor.jobs.curDriver.asleep = false;
            });
            return layDown;
        }
    }
}