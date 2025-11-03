using RimWorld;
using System.Collections.Generic;
using Verse;
using UnityEngine;

namespace WulaFallenEmpire
{
    public class GlobalProductionOrderStack : IExposable
    {
        public Building_GlobalWorkTable table;
        public List<GlobalProductionOrder> orders = new List<GlobalProductionOrder>();

        // 修复：明确的工作量定义
        private const float WorkPerSecond = 60f; // 每秒60工作量（标准RimWorld速度）
        private const float TicksPerSecond = 60f;
        private const float WorkPerTick = WorkPerSecond / TicksPerSecond; // 每tick 1工作量

        public GlobalProductionOrderStack(Building_GlobalWorkTable table)
        {
            this.table = table;
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref table, "table");
            Scribe_Collections.Look(ref orders, "orders", LookMode.Deep);
            
            // 修复：加载后验证和修复数据
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                FixAllOrders();
            }
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
            // 修复：使用倒序遍历避免修改集合问题
            for (int i = orders.Count - 1; i >= 0; i--)
            {
                var order = orders[i];
                
                // 首先更新状态
                order.UpdateState();
                
                if (order.paused || order.state == GlobalProductionOrder.ProductionState.Completed)
                    continue;

                // 生产中
                if (order.state == GlobalProductionOrder.ProductionState.Producing)
                {
                    ProcessProducingOrder(order, i);
                }
                else if (order.state == GlobalProductionOrder.ProductionState.Waiting && !order.paused)
                {
                    ProcessWaitingOrder(order);
                }
            }
        }
        
        private void ProcessProducingOrder(GlobalProductionOrder order, int index)
        {
            // 修复：使用正确的方法获取工作量
            float workAmount = GetWorkAmountForOrder(order);
            
            // 防止除零错误
            if (workAmount <= 0)
            {
                Log.Error($"Invalid workAmount ({workAmount}) for recipe {order.recipe.defName}");
                order.state = GlobalProductionOrder.ProductionState.Waiting;
                order.progress = 0f;
                return;
            }
            
            // 修复：正确计算进度增量
            float progressIncrement = WorkPerTick / workAmount;
            
            // 修复：确保进度不会变成负数
            float newProgress = Mathf.Max(0f, order.progress + progressIncrement);
            order.progress = newProgress;

            // 调试信息
            if (Find.TickManager.TicksGame % 300 == 0) // 每5秒输出一次
            {
                Log.Message($"[DEBUG] Order {order.recipe.defName}: " +
                           $"progress={order.progress:P2}, " +
                           $"workAmount={workAmount}, " +
                           $"increment={progressIncrement:E4}, " +
                           $"state={order.state}");
            }

            // 修复：使用精确比较完成条件
            if (order.progress >= 1.0f)
            {
                CompleteProduction(order, index);
            }
        }
        
        // 修复：新增方法 - 正确获取订单的工作量
        private float GetWorkAmountForOrder(GlobalProductionOrder order)
        {
            if (order?.recipe == null)
                return 1000f; // 默认值
            
            // 如果配方有明确的工作量且大于0，使用配方的工作量
            if (order.recipe.workAmount > 0)
                return order.recipe.workAmount;
            
            // 否则，使用第一个产品的WorkToMake属性
            if (order.recipe.products != null && order.recipe.products.Count > 0)
            {
                ThingDef productDef = order.recipe.products[0].thingDef;
                if (productDef != null)
                {
                    // 获取产品的WorkToMake统计值
                    float workToMake = productDef.GetStatValueAbstract(StatDefOf.WorkToMake);
                    if (workToMake > 0)
                        return workToMake;
                    
                    // 如果WorkToMake也是0或无效，使用产品的市场价值作为估算
                    float marketValue = productDef.GetStatValueAbstract(StatDefOf.MarketValue);
                    if (marketValue > 0)
                        return marketValue * 10f; // 基于市场价值的估算
                }
            }
            
            // 最后的回退方案
            Log.Warning($"Could not determine work amount for recipe {order.recipe.defName}, using default value");
            return 1000f; // 默认工作量
        }
        
        private void ProcessWaitingOrder(GlobalProductionOrder order)
        {
            // 检查是否应该开始生产
            if (order.HasEnoughResources())
            {
                order.state = GlobalProductionOrder.ProductionState.Producing;
                order.progress = 0f;
                
                if (Find.TickManager.TicksGame % 600 == 0) // 每10秒记录一次
                {
                    Log.Message($"[INFO] Order {order.recipe.defName} started producing");
                }
            }
            else if (Find.TickManager.TicksGame % 1200 == 0) // 每20秒检查一次
            {
                Log.Message($"[DEBUG] Order {order.recipe.defName} is waiting. " +
                           $"HasEnoughResources: {order.HasEnoughResources()}");
            }
        }
        
        private void CompleteProduction(GlobalProductionOrder order, int index)
        {
            // 修复：生产完成，消耗资源
            if (order.ConsumeResources())
            {
                order.Produce();
                order.UpdateState();
                
                Log.Message($"[SUCCESS] Produced {order.recipe.products[0].thingDef.defName}, " +
                           $"count: {order.currentCount}/{order.targetCount}");
                
                // 重置进度
                order.progress = 0f;
                
                // 如果订单完成，移除它
                if (order.state == GlobalProductionOrder.ProductionState.Completed)
                {
                    orders.RemoveAt(index);
                    Log.Message($"[COMPLETE] Order {order.recipe.defName} completed and removed");
                }
            }
            else
            {
                // 修复：资源不足，回到等待状态
                order.state = GlobalProductionOrder.ProductionState.Waiting;
                order.progress = 0f;
                Log.Message($"[WARNING] Failed to consume resources for {order.recipe.defName}, resetting");
            }
        }
        
        // 修复：全面数据修复方法
        private void FixAllOrders()
        {
            for (int i = orders.Count - 1; i >= 0; i--)
            {
                var order = orders[i];
                
                // 修复进度值
                if (float.IsNaN(order.progress) || float.IsInfinity(order.progress))
                {
                    order.progress = 0f;
                    Log.Warning($"Fixed invalid progress for {order.recipe?.defName ?? "unknown"}");
                }
                else if (order.progress < 0f)
                {
                    order.progress = 0f;
                    Log.Warning($"Fixed negative progress for {order.recipe?.defName ?? "unknown"}");
                }
                else if (order.progress > 1f)
                {
                    order.progress = 1f;
                    Log.Warning($"Fixed excessive progress for {order.recipe?.defName ?? "unknown"}");
                }
                
                // 修复状态
                if (order.recipe == null)
                {
                    Log.Warning($"Removing order with null recipe");
                    orders.RemoveAt(i);
                    continue;
                }
                
                // 强制更新状态
                order.UpdateState();
            }
        }
        
        public void FixNegativeProgress()
        {
            FixAllOrders();
        }
    }
}
