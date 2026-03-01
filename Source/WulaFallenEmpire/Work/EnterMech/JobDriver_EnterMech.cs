// File: JobDriver_EnterMech.cs (不再保留机甲)
using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;
namespace WulaFallenEmpire
{
    public class JobDriver_EnterMech : JobDriver
    {
        private const TargetIndex MechIndex = TargetIndex.A;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            Pawn pawn = this.pawn;
            LocalTargetInfo target = this.job.GetTarget(MechIndex);

            // 不再保留机甲，这样多个殖民者可以同时被命令进入同一个机甲
            // 只需要检查殖民者是否可以到达机甲
            if (!pawn.CanReach(target, PathEndMode.Touch, Danger.Deadly))
            {
                return false;
            }
            
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            // 0. 初始检查
            AddFailCondition(() =>
            {
                var mech = TargetThingA as Wulamechunit;
                if (mech == null || mech.Destroyed)
                {
                    return true;
                }

                var comp = mech.GetComp<CompMechPilotHolder>();
                if (comp == null || comp.IsFull || !comp.CanAddPilot(pawn))
                {
                    return true;
                }

                if (pawn.Downed || pawn.Dead)
                    return true;

                return false;
            });

            // 1. 走到机甲旁边
            yield return Toils_Goto.GotoThing(MechIndex, PathEndMode.Touch);

            // 2. 检查是否仍然可以进入
            yield return Toils_General.Wait(10).WithProgressBarToilDelay(MechIndex);

            // 3. 进入机甲
            Toil enterToil = new Toil();
            enterToil.initAction = () =>
            {
                var mech = TargetThingA as Wulamechunit;
                if (mech == null)
                    return;

                var comp = mech.GetComp<CompMechPilotHolder>();
                if (comp != null && comp.CanAddPilot(pawn))
                {
                    comp.AddPilot(pawn);
                    if (mech.drafter != null && !mech.Drafted)
                    {
                        mech.drafter.Drafted = true;
                    }
                    //Messages.Message("WULA_PilotEnteredMech".Translate(pawn.LabelShort, mech.LabelShort),
                    //    MessageTypeDefOf.PositiveEvent, false);
                }
            };
            enterToil.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return enterToil;
        }
    }
}
