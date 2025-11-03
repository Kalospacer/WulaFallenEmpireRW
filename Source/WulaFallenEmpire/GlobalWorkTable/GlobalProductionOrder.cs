// GlobalProductionOrder.cs (修复版)
using RimWorld;
using System.Collections.Generic;
using System.Text;
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

        // 生产一个产品
        public void Produce()
        {
            var globalStorage = Find.World.GetComponent<GlobalStorageWorldComponent>();
            if (globalStorage == null) return;

            foreach (var product in recipe.products)
            {
                globalStorage.AddToOutputStorage(product.thingDef, product.count);
            }
            
            currentCount++;
            progress = 0f;
            
            if (currentCount >= targetCount)
            {
                state = ProductionState.Completed;
            }
        }

        // 新增方法：检查并更新状态
        public void UpdateState()
        {
            if (state == ProductionState.Completed) return;
            
            if (HasEnoughResources())
            {
                if (state == ProductionState.Waiting && !paused)
                {
                    state = ProductionState.Producing;
                }
            }
            else
            {
                if (state == ProductionState.Producing)
                {
                    state = ProductionState.Waiting;
                    progress = 0f; // 重置进度
                }
            }
        }

        // 获取配方材料信息的字符串
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

            // 添加工作量信息
            sb.AppendLine();
            sb.AppendLine("WULA_WorkAmount".Translate() + ": " + recipe.workAmount.ToStringWorkAmount());

            return sb.ToString();
        }
        // 获取简化的材料信息（用于Tooltip）
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

            return sb.ToString();
        }
    }
}
