using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    public class JobDriver_GlobalWorkTable : JobDriver
    {
        private const TargetIndex TableIndex = TargetIndex.A;
        private const TargetIndex IngredientIndex = TargetIndex.B;
        private const TargetIndex IngredientPlaceCellIndex = TargetIndex.C;

        protected Building_GlobalWorkTable Table => (Building_GlobalWorkTable)job.GetTarget(TableIndex).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (!pawn.Reserve(Table, job, 1, -1, null, errorOnFailed))
                return false;
            
            pawn.ReserveAsManyAsPossible(job.GetTargetQueue(IngredientIndex), job);
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TableIndex);
            this.FailOnForbidden(TableIndex);

            // 1. 前往工作台
            yield return Toils_Goto.GotoThing(TableIndex, PathEndMode.InteractionCell);

            // 2. 收集材料 (使用原版 JobDriver_DoBill 的逻辑)
            // 参数: ingredientInd, billGiverInd, ingredientPlaceCellInd, subtractNumTakenFromJobCount, failIfStackCountLessThanJobCount, placeInBillGiver
            foreach (Toil toil in JobDriver_DoBill.CollectIngredientsToils(IngredientIndex, TableIndex, IngredientPlaceCellIndex, false, true, true))
            {
                yield return toil;
            }

            // 3. 检查并触发上传
            yield return new Toil
            {
                initAction = delegate
                {
                    CheckAndUpload();
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }

        private void CheckAndUpload()
        {
            var table = Table;

            // 找到当前正在进行的订单
            var order = table.globalOrderStack.orders.FirstOrDefault(o => o.state == GlobalProductionOrder.ProductionState.Gathering && !o.paused);
            if (order == null) return;

            var beforeState = order.state;
            table.TryAutoGatherFromBeaconsAndContainer();

            if (beforeState == GlobalProductionOrder.ProductionState.Gathering &&
                order.state == GlobalProductionOrder.ProductionState.Producing)
            {
                Messages.Message("WULA_OrderStarted".Translate(order.Label), table, MessageTypeDefOf.PositiveEvent);
            }
        }
    }
}
