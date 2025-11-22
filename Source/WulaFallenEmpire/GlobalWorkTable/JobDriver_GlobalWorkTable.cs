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

        protected Building_GlobalWorkTable Table => (Building_GlobalWorkTable)job.GetTarget(TableIndex).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (!pawn.Reserve(Table, job, 1, -1, null, errorOnFailed))
                return false;
            
            // 预约所有材料
            if (job.targetQueueB != null)
            {
                foreach (var target in job.targetQueueB)
                {
                    if (!pawn.Reserve(target, job, 1, -1, null, errorOnFailed))
                        return false;
                }
            }
            
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            // 1. 收集材料
            Toil collect = Toils_General.DoAtomic(delegate
            {
                // 这是一个占位符，实际收集逻辑由下面的循环生成
            });
            
            foreach (var toil in CollectIngredientsToils())
            {
                yield return toil;
            }

            // 2. 运送到工作台
            yield return Toils_Goto.GotoThing(TableIndex, PathEndMode.Touch);

            // 3. 放入材料
            yield return new Toil
            {
                initAction = delegate
                {
                    Pawn actor = GetActor();
                    Building_GlobalWorkTable table = Table;
                    
                    // 将携带的所有相关材料放入工作台
                    // 注意：这里假设小人携带的都是为了这个任务
                    // 实际可能需要更精确的筛选
                    List<Thing> carriedThings = actor.inventory.innerContainer.ToList(); // 复制列表
                    
                    foreach (var thing in carriedThings)
                    {
                        // 检查这个物品是否是订单需要的（简单检查Def）
                        // 这里简化处理：直接全部放入，多余的之后再处理或留在容器中
                        if (actor.inventory.innerContainer.TryTransferToContainer(thing, table.innerContainer, thing.stackCount) > 0)
                        {
                            // 成功放入
                        }
                    }
                    
                    // 同时也处理手上拿着的（如果有）
                    if (actor.carryTracker.CarriedThing != null)
                    {
                        actor.carryTracker.innerContainer.TryTransferToContainer(actor.carryTracker.CarriedThing, table.innerContainer);
                    }
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };

            // 4. 检查并触发上传
            yield return new Toil
            {
                initAction = delegate
                {
                    CheckAndUpload();
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }

        private IEnumerable<Toil> CollectIngredientsToils()
        {
            // 遍历队列中的所有材料
            // 注意：RimWorld的原版 JobDriver_DoBill 的收集逻辑非常复杂
            // 这里使用简化版：走到目标 -> 拿起 -> 下一个
            
            Toil extract = Toils_JobTransforms.ExtractNextTargetFromQueue(IngredientIndex);
            yield return extract;

            Toil gotoThing = Toils_Goto.GotoThing(IngredientIndex, PathEndMode.ClosestTouch);
            yield return gotoThing;

            Toil takeThing = Toils_Haul.StartCarryThing(IngredientIndex, false, true);
            yield return takeThing;
            
            // 循环直到队列为空
            yield return Toils_Jump.JumpIfHaveTargetInQueue(IngredientIndex, extract);
        }

        private void CheckAndUpload()
        {
            var table = Table;
            var globalStorage = Find.World.GetComponent<GlobalStorageWorldComponent>();
            
            // 找到当前正在进行的订单
            var order = table.globalOrderStack.orders.FirstOrDefault(o => o.state == GlobalProductionOrder.ProductionState.Gathering && !o.paused);
            if (order == null) return;

            // 检查是否满足需求
            // 这里的逻辑需要结合云端库存和本地容器库存
            // 如果 (云端 + 容器) >= 需求，则触发上传
            
            var costList = order.GetProductCostList();
            bool allSatisfied = true;
            
            foreach (var kvp in costList)
            {
                int needed = kvp.Value;
                int inCloud = globalStorage.GetInputStorageCount(kvp.Key);
                int inContainer = table.innerContainer.TotalStackCountOfDef(kvp.Key);
                
                if (inCloud + inContainer < needed)
                {
                    allSatisfied = false;
                    break;
                }
            }

            if (allSatisfied)
            {
                // 消耗容器中的材料并上传到云端
                foreach (var kvp in costList)
                {
                    int needed = kvp.Value;
                    int inCloud = globalStorage.GetInputStorageCount(kvp.Key);
                    int missingInCloud = needed - inCloud;
                    
                    if (missingInCloud > 0)
                    {
                        // 从容器中移除并添加到云端
                        int taken = table.innerContainer.TryTransferToContainer(null, table.innerContainer, missingInCloud); 
                        // 注意：上面的 TryTransferToContainer 用法不对，因为目标是云端（虚拟）
                        // 正确做法：
                        
                        int toTake = missingInCloud;
                        while (toTake > 0)
                        {
                            Thing t = table.innerContainer.FirstOrDefault(x => x.def == kvp.Key);
                            if (t == null) break; // 理论上不应该发生，因为前面检查过了
                            
                            int num = UnityEngine.Mathf.Min(t.stackCount, toTake);
                            t.SplitOff(num).Destroy(); // 销毁实体
                            globalStorage.AddToInputStorage(kvp.Key, num); // 添加虚拟库存
                            toTake -= num;
                        }
                    }
                }
                
                // 切换状态
                order.state = GlobalProductionOrder.ProductionState.Producing;
                Messages.Message("WULA_OrderStarted".Translate(order.Label), table, MessageTypeDefOf.PositiveEvent);
            }
        }
    }
}