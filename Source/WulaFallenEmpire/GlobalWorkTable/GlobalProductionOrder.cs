// GlobalProductionOrder.cs (修正材质属性读取)
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
        
        // 材质选择：存储配方选择的材质（只有支持材质的配方才有）
        public ThingDef chosenStuff = null;
        
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

        // 新增：检查订单是否已经开始生产（一旦开始就不能修改材质）
        public bool HasStartedProduction => state == ProductionState.Producing || currentCount > 0;

        // 修正：检查产物是否支持材质选择
        public bool SupportsStuffChoice
        {
            get
            {
                if (recipe?.products == null || recipe.products.Count == 0)
                    return false;
                    
                var productDef = recipe.products[0].thingDef;
                if (productDef == null)
                    return false;
                    
                // 检查产物是否有stuffCategories且costStuffCount > 0
                return productDef.stuffCategories != null && 
                       productDef.stuffCategories.Count > 0 && 
                       productDef.costStuffCount > 0;
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
            Scribe_Defs.Look(ref chosenStuff, "chosenStuff");

            // 修复：加载后验证数据
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                progress = _progress;
                UpdateState();
                
                // 确保材质选择有效
                if (SupportsStuffChoice && chosenStuff == null)
                {
                    InitializeStuffChoice();
                }
            }
        }

        // 修正：初始化材质选择
        public void InitializeStuffChoice()
        {
            if (!SupportsStuffChoice) return;
            
            var availableStuff = GetAvailableStuffForProduct();
            
            if (availableStuff.Count > 0)
            {
                chosenStuff = availableStuff[0];
            }
        }

        // 修正：获取产物的可用材质列表
        public List<ThingDef> GetAvailableStuffForProduct()
        {
            var availableStuff = new List<ThingDef>();
            
            if (ProductDef?.stuffCategories != null)
            {
                foreach (var stuffCategory in ProductDef.stuffCategories)
                {
                    var stuffInCategory = DefDatabase<ThingDef>.AllDefs
                        .Where(def => def.IsStuff && def.stuffProps?.categories != null && def.stuffProps.categories.Contains(stuffCategory))
                        .ToList();
                    
                    availableStuff.AddRange(stuffInCategory);
                }
            }
            
            return availableStuff.Distinct().ToList();
        }

        // 修正：HasEnoughResources 方法，考虑选择的材质
        public bool HasEnoughResources()
        {
            var globalStorage = Find.World.GetComponent<GlobalStorageWorldComponent>();
            if (globalStorage == null) return false;

            // 检查固定消耗（costList）
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
            
            // 检查材质消耗（如果支持材质选择）
            if (SupportsStuffChoice && chosenStuff != null)
            {
                int requiredStuffCount = ProductDef.costStuffCount;
                int availableStuffCount = globalStorage.GetInputStorageCount(chosenStuff);
                
                if (availableStuffCount < requiredStuffCount)
                    return false;
            }
            
            return true;
        }

        // 修正：ConsumeResources 方法，考虑选择的材质
        public bool ConsumeResources()
        {
            var globalStorage = Find.World.GetComponent<GlobalStorageWorldComponent>();
            if (globalStorage == null) return false;

            // 消耗固定资源（costList）
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
            
            // 消耗材质（如果支持材质选择）
            if (SupportsStuffChoice && chosenStuff != null)
            {
                int requiredStuffCount = ProductDef.costStuffCount;
                
                if (!globalStorage.RemoveFromInputStorage(chosenStuff, requiredStuffCount))
                    return false;
            }
            
            return true;
        }

        // 修正：GetIngredientsTooltip 方法，显示固定消耗和可选材质
        public string GetIngredientsTooltip()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(recipe.LabelCap);
            sb.AppendLine();

            // 固定消耗（costList）
            sb.AppendLine("WULA_FixedIngredients".Translate() + ":");
            var globalStorage = Find.World.GetComponent<GlobalStorageWorldComponent>();

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

            // 材质消耗（如果支持材质选择）
            if (SupportsStuffChoice)
            {
                sb.AppendLine();
                sb.AppendLine("WULA_StuffMaterial".Translate() + ":");
                
                if (chosenStuff != null)
                {
                    int requiredStuffCount = ProductDef.costStuffCount;
                    int availableStuffCount = globalStorage?.GetInputStorageCount(chosenStuff) ?? 0;
                    
                    string stuffDisplay = $"{requiredStuffCount} {chosenStuff.LabelCap}";
                    
                    if (availableStuffCount >= requiredStuffCount)
                    {
                        sb.AppendLine($" <color=green>{stuffDisplay} (Selected)</color>");
                    }
                    else
                    {
                        sb.AppendLine($" <color=red>{stuffDisplay} (Selected)</color>");
                    }
                }
                else
                {
                    sb.AppendLine($" <color=yellow>{"WULA_NoStuffSelected".Translate()}</color>");
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
            
            // 添加材质选择状态信息
            if (HasStartedProduction && SupportsStuffChoice)
            {
                sb.AppendLine();
                sb.AppendLine("<color=yellow>Material choice is locked because production has started.</color>");
            }

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
