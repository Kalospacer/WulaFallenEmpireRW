// GlobalProductionOrderStack.cs (修复版)
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace WulaFallenEmpire
{
    public class GlobalProductionOrderStack : IExposable
    {
        public Building_GlobalWorkTable table;
        public List<GlobalProductionOrder> orders = new List<GlobalProductionOrder>();

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
            
            // 添加到全局存储中统一管理
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
                if (order.paused || order.state == GlobalProductionOrder.ProductionState.Completed)
                    continue;

                // 检查资源并更新状态
                if (order.state == GlobalProductionOrder.ProductionState.Waiting)
                {
                    if (order.HasEnoughResources())
                    {
                        order.state = GlobalProductionOrder.ProductionState.Producing;
                        Log.Message($"[DEBUG] Order {order.recipe.defName} started producing");
                    }
                    else
                    {
                        continue;
                    }
                }

                // 生产中
                if (order.state == GlobalProductionOrder.ProductionState.Producing)
                {
                    // 更清晰的进度计算
                    float progressPerTick = 1f / (order.recipe.workAmount * 5f); // 调整系数以控制生产速度
                    order.progress += progressPerTick;

                    if (order.progress >= 1f)
                    {
                        // 消耗资源并生产 - 在结束时扣除资源
                        if (order.ConsumeResources())
                        {
                            order.Produce();
                        }
                        else
                        {
                            // 资源被其他订单消耗，回到等待状态
                            order.state = GlobalProductionOrder.ProductionState.Waiting;
                            order.progress = 0f;
                            Log.Message("[DEBUG] Resources consumed by another order, returning to waiting state");
                        }
                    }
                    else
                    {
                        Log.Message($"[DEBUG] Order {order.recipe.defName} progress: {order.progress:P0}");
                    }
                }
            }
        }
    }
}
