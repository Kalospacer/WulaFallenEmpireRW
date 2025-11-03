// GlobalProductionOrder.cs (修复版)
using RimWorld;
using System.Collections.Generic;
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
        public bool paused = true; // 初始状态为暂停
        
        // 生产状态
        public ProductionState state = ProductionState.Waiting;
        
        public enum ProductionState
        {
            Waiting,    // 等待资源
            Producing,  // 生产中
            Completed   // 完成
        }

        public string Label => recipe.LabelCap;
        public string Description => $"{currentCount}/{targetCount} {recipe.products[0].thingDef.label}"; private float _progress = 0f;
        public float progress
        {
            get => _progress;
            set
            {
                // 确保进度在有效范围内
                _progress = Mathf.Clamp01(value);

                // 如果检测到异常值，记录警告
                if (value < 0f || value > 1f)
                {
                    Log.Warning($"Progress clamped from {value} to {_progress} for {recipe?.defName ?? "unknown"}");
                }
            }
        }

        public void ExposeData()
        {
            Scribe_Defs.Look(ref recipe, "recipe");
            Scribe_Values.Look(ref targetCount, "targetCount", 1);
            Scribe_Values.Look(ref currentCount, "currentCount", 0);
            Scribe_Values.Look(ref paused, "paused", true);
            Scribe_Values.Look(ref _progress, "progress", 0f); // 序列化私有字段
            Scribe_Values.Look(ref state, "state", ProductionState.Waiting);

            // 修复：加载后验证数据
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // 使用属性设置器来钳制值
                progress = _progress;

                // 确保状态正确
                UpdateState();
            }
        }

        // 修复：改进状态更新逻辑
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
                    progress = 0f; // 开始生产时重置进度
                }
            }
            else
            {
                if (state == ProductionState.Producing)
                {
                    state = ProductionState.Waiting;
                    progress = 0f; // 资源不足时重置进度
                }
            }
        }

        // 修复：改进生产完成逻辑
        public void Produce()
        {
            var globalStorage = Find.World.GetComponent<GlobalStorageWorldComponent>();
            if (globalStorage == null)
                return;
            foreach (var product in recipe.products)
            {
                globalStorage.AddToOutputStorage(product.thingDef, product.count);
            }

            currentCount++;
            progress = 0f; // 生产完成后重置进度

            if (currentCount >= targetCount)
            {
                state = ProductionState.Completed;
            }
        }

        // 检查是否有足够资源 - 修复逻辑
        public bool HasEnoughResources()
        {
            var globalStorage = Find.World.GetComponent<GlobalStorageWorldComponent>();
            if (globalStorage == null) return false;

            // 遍历所有配料要求
            foreach (var ingredient in recipe.ingredients)
            {
                bool hasEnoughForThisIngredient = false;
                
                // 检查这个配料的所有允许物品类型
                foreach (var thingDef in ingredient.filter.AllowedThingDefs)
                {
                    int requiredCount = ingredient.CountRequiredOfFor(thingDef, recipe);
                    int availableCount = globalStorage.GetInputStorageCount(thingDef);
                    
                    if (availableCount >= requiredCount)
                    {
                        hasEnoughForThisIngredient = true;
                        break; // 这个配料有足够的资源
                    }
                }
                
                // 如果任何一个配料没有足够资源，整个配方就无法生产
                if (!hasEnoughForThisIngredient)
                    return false;
            }
            
            return true;
        }

        // 消耗资源 - 修复逻辑
        public bool ConsumeResources()
        {
            var globalStorage = Find.World.GetComponent<GlobalStorageWorldComponent>();
            if (globalStorage == null) return false;

            // 遍历所有配料要求
            foreach (var ingredient in recipe.ingredients)
            {
                bool consumedThisIngredient = false;
                
                // 尝试消耗这个配料的允许物品类型
                foreach (var thingDef in ingredient.filter.AllowedThingDefs)
                {
                    int requiredCount = ingredient.CountRequiredOfFor(thingDef, recipe);
                    int availableCount = globalStorage.GetInputStorageCount(thingDef);
                    
                    if (availableCount >= requiredCount)
                    {
                        if (globalStorage.RemoveFromInputStorage(thingDef, requiredCount))
                        {
                            consumedThisIngredient = true;
                            break; // 成功消耗这个配料
                        }
                    }
                }
                
                // 如果任何一个配料无法消耗，整个生产失败
                if (!consumedThisIngredient)
                    return false;
            }
            
            return true;
        }
        // 修复：添加获取正确工作量的方法
        public float GetWorkAmount()
        {
            if (recipe == null)
                return 1000f;

            // 如果配方有明确的工作量且大于0，使用配方的工作量
            if (recipe.workAmount > 0)
                return recipe.workAmount;

            // 否则，使用第一个产品的WorkToMake属性
            if (recipe.products != null && recipe.products.Count > 0)
            {
                ThingDef productDef = recipe.products[0].thingDef;
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

            return 1000f; // 默认工作量
        }

        // 修复：在信息显示中使用正确的工作量
        public string GetIngredientsInfo()
        {
            StringBuilder sb = new StringBuilder();
            // 添加标题
            sb.AppendLine("WULA_RequiredIngredients".Translate() + ":");
            var globalStorage = Find.World.GetComponent<GlobalStorageWorldComponent>();
            foreach (var ingredient in recipe.ingredients)
            {
                bool firstAllowedThing = true;
                foreach (var thingDef in ingredient.filter.AllowedThingDefs)
                {
                    int requiredCount = ingredient.CountRequiredOfFor(thingDef, recipe);
                    int availableCount = globalStorage?.GetInputStorageCount(thingDef) ?? 0;
                    if (firstAllowedThing)
                    {
                        sb.Append(" - ");
                        firstAllowedThing = false;
                    }
                    else
                    {
                        sb.Append(" / ");
                    }
                    sb.Append($"{requiredCount} {thingDef.label}");
                    // 添加可用数量信息
                    if (availableCount < requiredCount)
                    {
                        sb.Append($" (<color=red>{availableCount}</color>/{requiredCount})");
                    }
                    else
                    {
                        sb.Append($" ({availableCount}/{requiredCount})");
                    }
                }
                sb.AppendLine();
            }
            // 添加产品信息
            sb.AppendLine();
            sb.AppendLine("WULA_Products".Translate() + ":");
            foreach (var product in recipe.products)
            {
                sb.AppendLine($" - {product.count} {product.thingDef.label}");
            }
            // 修复：使用正确的工作量信息
            sb.AppendLine();
            sb.AppendLine("WULA_WorkAmount".Translate() + ": " + GetWorkAmount().ToStringWorkAmount());
            return sb.ToString();
        }

        // 修复：在Tooltip中也使用正确的工作量
        public string GetIngredientsTooltip()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(recipe.LabelCap);
            sb.AppendLine();
            // 材料需求
            sb.AppendLine("WULA_RequiredIngredients".Translate() + ":");
            var globalStorage = Find.World.GetComponent<GlobalStorageWorldComponent>();
            foreach (var ingredient in recipe.ingredients)
            {
                bool ingredientSatisfied = false;
                StringBuilder ingredientSB = new StringBuilder();
                foreach (var thingDef in ingredient.filter.AllowedThingDefs)
                {
                    int requiredCount = ingredient.CountRequiredOfFor(thingDef, recipe);
                    int availableCount = globalStorage?.GetInputStorageCount(thingDef) ?? 0;
                    if (ingredientSB.Length > 0)
                        ingredientSB.Append(" / ");
                    ingredientSB.Append($"{requiredCount} {thingDef.label}");
                    if (availableCount >= requiredCount)
                    {
                        ingredientSatisfied = true;
                    }
                }
                if (ingredientSatisfied)
                {
                    sb.AppendLine($" <color=green>{ingredientSB}</color>");
                }
                else
                {
                    sb.AppendLine($" <color=red>{ingredientSB}</color>");
                }
            }
            // 产品
            sb.AppendLine();
            sb.AppendLine("WULA_Products".Translate() + ":");
            foreach (var product in recipe.products)
            {
                sb.AppendLine($" {product.count} {product.thingDef.label}");
            }
            // 修复：使用正确的工作量
            sb.AppendLine();
            sb.AppendLine("WULA_WorkAmount".Translate() + ": " + GetWorkAmount().ToStringWorkAmount());
            return sb.ToString();
        }
    }
}
