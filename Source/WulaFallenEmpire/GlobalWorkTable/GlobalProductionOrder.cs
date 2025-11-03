// GlobalProductionOrder.cs (修复版)
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace WulaFallenEmpire
{
    public class GlobalProductionOrder : IExposable
    {
        public RecipeDef recipe;
        public int targetCount = 1;
        public int currentCount = 0;
        public bool paused = true; // 初始状态为暂停
        public float progress = 0f;
        
        // 生产状态
        public ProductionState state = ProductionState.Waiting;
        
        public enum ProductionState
        {
            Waiting,    // 等待资源
            Producing,  // 生产中
            Completed   // 完成
        }

        public void ExposeData()
        {
            Scribe_Defs.Look(ref recipe, "recipe");
            Scribe_Values.Look(ref targetCount, "targetCount", 1);
            Scribe_Values.Look(ref currentCount, "currentCount", 0);
            Scribe_Values.Look(ref paused, "paused", true);
            Scribe_Values.Look(ref progress, "progress", 0f);
            Scribe_Values.Look(ref state, "state", ProductionState.Waiting);
        }

        public string Label => recipe.LabelCap;
        public string Description => $"{currentCount}/{targetCount} {recipe.products[0].thingDef.label}";

        // 检查是否有足够资源 - 修复逻辑，只检查costList
        public bool HasEnoughResources()
        {
            var globalStorage = Find.World.GetComponent<GlobalStorageWorldComponent>();
            if (globalStorage == null) 
            {
                Log.Warning("GlobalStorageWorldComponent not found");
                return false;
            }

            // 只检查costList，不检查ingredients
            if (recipe.costList != null && recipe.costList.Count > 0)
            {
                foreach (var cost in recipe.costList)
                {
                    int required = cost.count;
                    int available = globalStorage.GetInputStorageCount(cost.thingDef);
                    
                    Log.Message($"[DEBUG] Checking {cost.thingDef.defName}: required={required}, available={available}");
                    
                    if (available < required)
                    {
                        Log.Message($"[DEBUG] Insufficient {cost.thingDef.defName}");
                        return false;
                    }
                }
                Log.Message("[DEBUG] All resources available");
                return true;
            }
            else
            {
                Log.Warning($"Recipe {recipe.defName} has no costList");
                return false;
            }
        }

        // 消耗资源 - 修复逻辑，只消耗costList
        public bool ConsumeResources()
        {
            var globalStorage = Find.World.GetComponent<GlobalStorageWorldComponent>();
            if (globalStorage == null) return false;

            // 只消耗costList中的资源
            if (recipe.costList != null)
            {
                foreach (var cost in recipe.costList)
                {
                    if (!globalStorage.RemoveFromInputStorage(cost.thingDef, cost.count))
                    {
                        Log.Warning($"Failed to consume {cost.count} {cost.thingDef.defName}");
                        return false;
                    }
                    Log.Message($"[DEBUG] Consumed {cost.count} {cost.thingDef.defName}");
                }
                return true;
            }
            return false;
        }

        // 生产一个产品
        public void Produce()
        {
            var globalStorage = Find.World.GetComponent<GlobalStorageWorldComponent>();
            if (globalStorage == null) return;

            foreach (var product in recipe.products)
            {
                globalStorage.AddToOutputStorage(product.thingDef, product.count);
                Log.Message($"[DEBUG] Produced {product.count} {product.thingDef.defName}");
            }
            
            currentCount++;
            progress = 0f;
            
            if (currentCount >= targetCount)
            {
                state = ProductionState.Completed;
                Log.Message("[DEBUG] Order completed");
            }
        }
    }
}
