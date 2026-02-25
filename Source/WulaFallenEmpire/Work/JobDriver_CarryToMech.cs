// File: JobDriver_CarryToMech.cs (修复count问题)
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    public class JobDriver_CarryToMech : JobDriver
    {
        private const TargetIndex TakeeIndex = TargetIndex.A;
        private const TargetIndex MechIndex = TargetIndex.B;

        protected Pawn Takee => (Pawn)job.GetTarget(TakeeIndex).Thing;
        protected Wulamechunit Mech => (Wulamechunit)job.GetTarget(MechIndex).Thing;
        protected CompMechPilotHolder MechComp => Mech?.TryGetComp<CompMechPilotHolder>();

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // 确保job.count是有效的（至少为1）
            if (job.count <= 0)
            {
                job.count = 1;
            }
            
            // 保留目标和机甲，明确指定数量为1
            return pawn.Reserve(Takee, job, 1, -1, null, errorOnFailed) 
                && pawn.Reserve(Mech, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            // 确保job.count是有效的
            if (job.count <= 0)
            {
                job.count = 1;
            }

            // 标准失败条件
            this.FailOnDestroyedOrNull(TakeeIndex);
            this.FailOnDestroyedOrNull(MechIndex);
            this.FailOn(() => MechComp == null);
            this.FailOn(() => !Takee.Downed); // 确保被搬运者是Downed状态

            // 1. 前往要被搬运的殖民者
            yield return Toils_Goto.GotoThing(TakeeIndex, PathEndMode.ClosestTouch)
                .FailOnDespawnedNullOrForbidden(TakeeIndex)
                .FailOnDespawnedNullOrForbidden(MechIndex)
                .FailOnSomeonePhysicallyInteracting(TakeeIndex);

            // 2. 开始搬运殖民者 - 使用原版的StartCarryThing方法
            yield return Toils_Haul.StartCarryThing(TakeeIndex, false, true, false);

            // 3. 携带殖民者前往机甲
            yield return Toils_Goto.GotoThing(MechIndex, PathEndMode.Touch);

            // 4. 将殖民者放入机甲
            yield return new Toil
            {
                initAction = () =>
                {
                    if (MechComp != null && Takee != null && MechComp.CanAddPilot(Takee))
                    {
                        // 放下殖民者
                        if (pawn.carryTracker.CarriedThing == Takee)
                        {
                            pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out Thing droppedThing);
                        }
                        
                        // 将殖民者添加到机甲
                        MechComp.AddPilot(Takee);
                    }
                    else
                    {
                        Log.Warning($"[WULA] 无法将殖民者添加到机甲");
                    }
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }
    }
}
