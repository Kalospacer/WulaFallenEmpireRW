// GlobalProductionOrder.cs (修复成本计算，使用产物的costList)
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    public class GlobalProductionOrder : IExposable
    {
        public RecipeDef recipe;
        public int targetCount = 1;
        public int currentCount = 0;
        public bool paused = true;
        
        // 生产状态
        public ProductionState state = ProductionState.Waiting;
        
        public enum ProductionState
        {
            Waiting,    // 等待资源
            Producing,  // 生产中
            Completed   // 完成
        }

        public string Label => recipe.LabelCap;
        public string Description => $"{currentCount}/{targetCount} {recipe.products[0].thingDef.label}";
        
        private float _progress = 0f;
        public float progress
        {
            get => _progress;
            set
            {
                _progress = Mathf.Clamp01(value);
                if (value < 0f || value > 1f)
                {
                    Log.Warning($"Progress clamped from {value} to {_progress} for {recipe?.defName ?? "unknown"}");
                }
            }
        }

        // 修正：获取产物的ThingDef
        public ThingDef ProductDef => recipe?.products?.Count > 0 ? recipe.products[0].thingDef : null;

        public void ExposeData()
        {
            Scribe_Defs.Look(ref recipe, "recipe");
            Scribe_Values.Look(ref targetCount, "targetCount", 1);
            Scribe_Values.Look(ref currentCount, "currentCount", 0);
            Scribe_Values.Look(ref paused, "paused", true);
            Scribe_Values.Look(ref _progress, "progress", 0f);
            Scribe_Values.Look(ref state, "state", ProductionState.Waiting);

            // 修复：加载后验证数据
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                progress = _progress;
                UpdateState();
            }
        }

        // 新增：获取产物的成本列表
        private Dictionary<ThingDef, int> GetProductCostList()
        {
            var costDict = new Dictionary<ThingDef, int>();
            
            if (ProductDef?.costList != null)
            {
                foreach (var cost in ProductDef.costList)
                {
                    if (costDict.ContainsKey(cost.thingDef))
                        costDict[cost.thingDef] += cost.count;
                    else
                        costDict[cost.thingDef] = cost.count;
                }
            }
            
            return costDict;
        }

        // 修复：HasEnoughResources 方法，使用产物的costList
        public bool HasEnoughResources()
        {
            var globalStorage = Find.World.GetComponent<GlobalStorageWorldComponent>();
            if (globalStorage == null) return false;

            // 首先检查产物的costList（对于武器等物品）
            var productCostList = GetProductCostList();
            if (productCostList.Count > 0)
            {
                foreach (var kvp in productCostList)
                {
                    int requiredCount = kvp.Value;
                    int availableCount = globalStorage.GetInputStorageCount(kvp.Key);
                    
                    if (availableCount < requiredCount)
                        return false;
                }
                return true;
            }
            
            // 如果没有costList，则回退到配方的ingredients（对于加工类配方）
            foreach (var ingredient in recipe.ingredients)
            {
                bool hasEnoughForThisIngredient = false;
                
                foreach (var thingDef in ingredient.filter.AllowedThingDefs)
                {
                    int requiredCount = ingredient.CountRequiredOfFor(thingDef, recipe);
                    int availableCount = globalStorage.GetInputStorageCount(thingDef);
                    
                    if (availableCount >= requiredCount)
                    {
                        hasEnoughForThisIngredient = true;
                        break;
                    }
                }
                
                if (!hasEnoughForThisIngredient)
                    return false;
            }
            
            return true;
        }

        // 修复：ConsumeResources 方法，使用产物的costList
        public bool ConsumeResources()
        {
            var globalStorage = Find.World.GetComponent<GlobalStorageWorldComponent>();
            if (globalStorage == null) return false;

            // 首先消耗产物的costList（对于武器等物品）
            var productCostList = GetProductCostList();
            if (productCostList.Count > 0)
            {
                foreach (var kvp in productCostList)
                {
                    if (!globalStorage.RemoveFromInputStorage(kvp.Key, kvp.Value))
                        return false;
                }
                return true;
            }
            
            // 如果没有costList，则消耗配方的ingredients（对于加工类配方）
            foreach (var ingredient in recipe.ingredients)
            {
                bool consumedThisIngredient = false;
                
                foreach (var thingDef in ingredient.filter.AllowedThingDefs)
                {
                    int requiredCount = ingredient.CountRequiredOfFor(thingDef, recipe);
                    int availableCount = globalStorage.GetInputStorageCount(thingDef);
                    
                    if (availableCount >= requiredCount)
                    {
                        if (globalStorage.RemoveFromInputStorage(thingDef, requiredCount))
                        {
                            consumedThisIngredient = true;
                            break;
                        }
                    }
                }
                
                if (!consumedThisIngredient)
                    return false;
            }
            
            return true;
        }

        // 修复：GetIngredientsTooltip 方法，显示正确的成本信息
        public string GetIngredientsTooltip()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(recipe.LabelCap);
            sb.AppendLine();

            var globalStorage = Find.World.GetComponent<GlobalStorageWorldComponent>();

            // 首先显示产物的costList（对于武器等物品）
            var productCostList = GetProductCostList();
            if (productCostList.Count > 0)
            {
                sb.AppendLine("WULA_FixedIngredients".Translate() + ":");
                
                foreach (var kvp in productCostList)
                {
                    ThingDef thingDef = kvp.Key;
                    int requiredCount = kvp.Value;
                    int availableCount = globalStorage?.GetInputStorageCount(thingDef) ?? 0;

                    string itemDisplay = $"{requiredCount} {thingDef.LabelCap}";

                    if (availableCount >= requiredCount)
                    {
                        sb.AppendLine($" <color=green>{itemDisplay}</color>");
                    }
                    else
                    {
                        sb.AppendLine($" <color=red>{itemDisplay}</color>");
                    }
                }
            }
            else
            {
                // 如果没有costList，显示配方的ingredients（对于加工类配方）
                sb.AppendLine("WULA_FixedIngredients".Translate() + ":");
                
                foreach (var ingredient in recipe.ingredients)
                {
                    foreach (var thingDef in ingredient.filter.AllowedThingDefs)
                    {
                        int requiredCount = ingredient.CountRequiredOfFor(thingDef, recipe);
                        int availableCount = globalStorage?.GetInputStorageCount(thingDef) ?? 0;

                        string itemDisplay = $"{requiredCount} {thingDef.LabelCap}";

                        if (availableCount >= requiredCount)
                        {
                            sb.AppendLine($" <color=green>{itemDisplay}</color>");
                        }
                        else
                        {
                            sb.AppendLine($" <color=red>{itemDisplay}</color>");
                        }
                    }
                }
            }

            // 产品
            sb.AppendLine();
            sb.AppendLine("WULA_Products".Translate() + ":");
            foreach (var product in recipe.products)
            {
                sb.AppendLine($" {product.count} {product.thingDef.LabelCap}");
            }

            // 工作量信息
            sb.AppendLine();
            sb.AppendLine("WULA_WorkAmount".Translate() + ": " + GetWorkAmount().ToStringWorkAmount());

            return sb.ToString();
        }

        // 其余方法保持不变...
        public void UpdateState()
        {
            if (state == ProductionState.Completed)
                return;

            if (currentCount >= targetCount)
            {
                state = ProductionState.Completed;
                progress = 0f;
                return;
            }

            if (HasEnoughResources())
            {
                if (state == ProductionState.Waiting && !paused)
                {
                    state = ProductionState.Producing;
                    progress = 0f;
                }
            }
            else
            {
                if (state == ProductionState.Producing)
                {
                    state = ProductionState.Waiting;
                    progress = 0f;
                }
            }
        }

        public void Produce()
        {
            var globalStorage = Find.World.GetComponent<GlobalStorageWorldComponent>();
            if (globalStorage == null)
                return;
            
            foreach (var product in recipe.products)
            {
                if (product.thingDef.race != null)
                {
                    globalStorage.AddToOutputStorage(product.thingDef, product.count);
                }
                else
                {
                    globalStorage.AddToOutputStorage(product.thingDef, product.count);
                }
            }

            currentCount++;
            progress = 0f;

            if (currentCount >= targetCount)
            {
                state = ProductionState.Completed;
            }
        }

        public float GetWorkAmount()
        {
            if (recipe == null)
                return 1000f;

            if (recipe.workAmount > 0)
                return recipe.workAmount;

            if (recipe.products != null && recipe.products.Count > 0)
            {
                ThingDef productDef = recipe.products[0].thingDef;
                if (productDef != null)
                {
                    float workToMake = productDef.GetStatValueAbstract(StatDefOf.WorkToMake);
                    if (workToMake > 0)
                        return workToMake;

                    float marketValue = productDef.GetStatValueAbstract(StatDefOf.MarketValue);
                    if (marketValue > 0)
                        return marketValue * 10f;
                }
            }

            return 1000f;
        }
    }
}
