// GlobalProductionOrderStack.cs (调整为每秒1工作量)
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace WulaFallenEmpire
{
    public class GlobalProductionOrderStack : IExposable
    {
        public Building_GlobalWorkTable table;
        public List<GlobalProductionOrder> orders = new List<GlobalProductionOrder>();

        // 调整为每秒1工作量 - RimWorld中1秒=60ticks
        private const float WorkPerSecond = 1f;
        private const float TicksPerSecond = 60f;
        private const float WorkPerTick = WorkPerSecond / TicksPerSecond;

        public GlobalProductionOrderStack(Building_GlobalWorkTable table)
        {
            this.table = table;
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref table, "table");
            Scribe_Collections.Look(ref orders, "orders", LookMode.Deep);
        }

        public void AddOrder(GlobalProductionOrder order)
        {
            orders.Add(order);
            
            var globalStorage = Find.World.GetComponent<GlobalStorageWorldComponent>();
            if (globalStorage != null)
            {
                globalStorage.AddProductionOrder(order);
            }
        }

        public void Delete(GlobalProductionOrder order)
        {
            orders.Remove(order);
            
            var globalStorage = Find.World.GetComponent<GlobalStorageWorldComponent>();
            if (globalStorage != null)
            {
                globalStorage.RemoveProductionOrder(order);
            }
        }

        public void ProcessOrders()
        {
            foreach (var order in orders)
            {
                // 首先更新状态
                order.UpdateState();
                
                if (order.paused || order.state == GlobalProductionOrder.ProductionState.Completed)
                    continue;

                // 生产中
                if (order.state == GlobalProductionOrder.ProductionState.Producing)
                {
                    // 计算每tick的工作量进度
                    float workAmount = order.recipe.workAmount;
                    float progressIncrement = WorkPerTick / workAmount;
                    
                    order.progress += progressIncrement;

                    // 调试信息 - 减少频率以免太吵
                    if (Find.TickManager.TicksGame % 600 == 0) // 每10秒输出一次调试信息
                    {
                        Log.Message($"[DEBUG] Order {order.recipe.defName} progress: {order.progress:P0}, " +
                                   $"workAmount: {workAmount}, increment: {progressIncrement:F6}");
                    }

                    if (order.progress >= 1f)
                    {
                        // 生产完成，消耗资源
                        if (order.ConsumeResources())
                        {
                            order.Produce();
                            order.UpdateState();
                            
                            Log.Message($"[SUCCESS] Produced {order.recipe.products[0].thingDef.defName}, " +
                                       $"count: {order.currentCount}/{order.targetCount}, " +
                                       $"workAmount: {workAmount}");
                        }
                        else
                        {
                            order.state = GlobalProductionOrder.ProductionState.Waiting;
                            order.progress = 0f;
                            Log.Message($"[WARNING] Failed to consume resources for {order.recipe.defName}");
                        }
                    }
                }
                else if (order.state == GlobalProductionOrder.ProductionState.Waiting && !order.paused)
                {
                    // 调试：检查为什么订单在等待状态
                    if (Find.TickManager.TicksGame % 1200 == 0) // 每20秒检查一次
                    {
                        Log.Message($"[DEBUG] Order {order.recipe.defName} is waiting. " +
                                   $"HasEnoughResources: {order.HasEnoughResources()}, paused: {order.paused}");
                    }
                }
            }
        }
    }
}
