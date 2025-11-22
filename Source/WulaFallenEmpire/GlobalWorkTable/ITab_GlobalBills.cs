// ITab_GlobalBills.cs (移除材质选择功能)
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace WulaFallenEmpire
{
    public class ITab_GlobalBills : ITab
    {
        private float viewHeight = 1000f;
        private Vector2 scrollPosition;
        private GlobalProductionOrder mouseoverOrder;

        private static readonly Vector2 WinSize = new Vector2(420f, 480f);

        // 分类按钮状态
        private ProductionCategory? selectedCategory = null;

        protected Building_GlobalWorkTable SelTable => (Building_GlobalWorkTable)base.SelThing;


        public ITab_GlobalBills()
        {
            size = WinSize;
            labelKey = "WULA_GlobalBillsTab";
            tutorTag = "GlobalBills";
        }
        protected override void FillTab()
        {
            Rect mainRect = new Rect(0f, 0f, WinSize.x, WinSize.y).ContractedBy(10f);

            // 标题
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(mainRect.x, mainRect.y, mainRect.width, 30f), "WULA_GlobalProduction".Translate());
            Text.Font = GameFont.Small;

            // 存储查看按钮
            Rect storageButtonRect = new Rect(mainRect.xMax - 160f, mainRect.y, 120f, 25f);
            DoStorageButton(storageButtonRect);

            // 分类按钮区域
            Rect categoryButtonsRect = new Rect(mainRect.x, mainRect.y + 35f, mainRect.width, 25f);
            DoCategoryButtons(categoryButtonsRect);

            // 上帝模式按钮区域
            if (DebugSettings.godMode)
            {
                Rect godModeButtonRect = new Rect(mainRect.x, mainRect.y + 65f, mainRect.width, 25f);
                DoGodModeButtons(godModeButtonRect);
            }

            // 订单列表区域
            float ordersRectY = DebugSettings.godMode ? mainRect.y + 90f : mainRect.y + 65f;
            Rect ordersRect = new Rect(mainRect.x, ordersRectY, mainRect.width, mainRect.height - (DebugSettings.godMode ? 130f : 100f));
            mouseoverOrder = DoOrdersListing(ordersRect);

            // 添加订单按钮（现在显示选中的分类）
            Rect addButtonRect = new Rect(mainRect.x, mainRect.yMax - 35f, mainRect.width, 30f);
            DoAddOrderButton(addButtonRect);
        }
        // 新增：分类按钮
        private void DoCategoryButtons(Rect rect)
        {
            float buttonWidth = (rect.width - 10f) / 3f;

            Rect equipmentRect = new Rect(rect.x, rect.y, buttonWidth, rect.height);
            Rect weaponRect = new Rect(rect.x + buttonWidth + 5f, rect.y, buttonWidth, rect.height);
            Rect mechanoidRect = new Rect(rect.x + (buttonWidth + 5f) * 2, rect.y, buttonWidth, rect.height);

            // 装备按钮
            string equipmentLabel = selectedCategory == ProductionCategory.Equipment ?
                $"<color=yellow>{"WULA_Equipment".Translate()}</color>" :
                "WULA_Equipment".Translate();

            if (Widgets.ButtonText(equipmentRect, equipmentLabel))
            {
                selectedCategory = selectedCategory == ProductionCategory.Equipment ? null : ProductionCategory.Equipment;
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            // 武器按钮
            string weaponLabel = selectedCategory == ProductionCategory.Weapon ?
                $"<color=yellow>{"WULA_Weapon".Translate()}</color>" :
                "WULA_Weapon".Translate();

            if (Widgets.ButtonText(weaponRect, weaponLabel))
            {
                selectedCategory = selectedCategory == ProductionCategory.Weapon ? null : ProductionCategory.Weapon;
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            // 机械体按钮
            string mechanoidLabel = selectedCategory == ProductionCategory.Mechanoid ?
                $"<color=yellow>{"WULA_Mechanoid".Translate()}</color>" :
                "WULA_Mechanoid".Translate();

            if (Widgets.ButtonText(mechanoidRect, mechanoidLabel))
            {
                selectedCategory = selectedCategory == ProductionCategory.Mechanoid ? null : ProductionCategory.Mechanoid;
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
        }
        // 修改：添加订单按钮，显示当前选择的分类
        private void DoAddOrderButton(Rect rect)
        {
            string buttonLabel = selectedCategory.HasValue ?
                $"{"WULA_AddProductionOrder".Translate()} ({GetCategoryLabel(selectedCategory.Value)})" :
                "WULA_AddProductionOrder".Translate();

            if (Widgets.ButtonText(rect, buttonLabel))
            {
                Find.WindowStack.Add(new FloatMenu(GenerateRecipeOptions()));
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            // 如果没有选择分类，显示提示
            if (!selectedCategory.HasValue && Mouse.IsOver(rect))
            {
                TooltipHandler.TipRegion(rect, "WULA_SelectCategoryFirst".Translate());
            }
        }
        // 获取分类标签
        private string GetCategoryLabel(ProductionCategory category)
        {
            return category switch
            {
                ProductionCategory.Equipment => "WULA_Equipment".Translate(),
                ProductionCategory.Weapon => "WULA_Weapon".Translate(),
                ProductionCategory.Mechanoid => "WULA_Mechanoid".Translate(),
                _ => "WULA_Unknown".Translate()
            };
        }
        // 修改：根据选择的分类生成配方选项
        private List<FloatMenuOption> GenerateRecipeOptions()
        {
            var options = new List<FloatMenuOption>();

            // 如果没有选择分类，显示所有配方
            if (!selectedCategory.HasValue)
            {
                foreach (var recipe in SelTable.def.AllRecipes)
                {
                    if (recipe.AvailableNow && recipe.AvailableOnNow(SelTable))
                    {
                        options.Add(CreateRecipeOption(recipe));
                    }
                }
            }
            else
            {
                // 根据选择的分类筛选配方
                foreach (var recipe in SelTable.def.AllRecipes)
                {
                    if (recipe.AvailableNow && recipe.AvailableOnNow(SelTable) &&
                        RecipeMatchesCategory(recipe, selectedCategory.Value))
                    {
                        options.Add(CreateRecipeOption(recipe));
                    }
                }
            }

            if (options.Count == 0)
            {
                options.Add(new FloatMenuOption("WULA_NoAvailableRecipes".Translate(), null));
            }

            // 按显示优先级排序
            options.SortByDescending(opt => opt.orderInPriority);

            return options;
        }
        // 创建配方选项
        private FloatMenuOption CreateRecipeOption(RecipeDef recipe)
        {
            string label = recipe.LabelCap;

            // 添加分类标签
            var category = GetRecipeCategory(recipe);
            if (category.HasValue)
            {
                label += $" [{GetCategoryLabel(category.Value)}]";
            }

            return new FloatMenuOption(
                label: label,
                action: () =>
                {
                    var newOrder = new GlobalProductionOrder
                    {
                        recipe = recipe,
                        targetCount = 1,
                        paused = true
                    };
                    SelTable.globalOrderStack.AddOrder(newOrder);
                    SoundDefOf.Click.PlayOneShotOnCamera();
                },
                shownItemForIcon: recipe.UIIconThing,
                thingStyle: null,
                forceBasicStyle: false,
                priority: MenuOptionPriority.Default,
                mouseoverGuiAction: null,
                revalidateClickTarget: null,
                extraPartWidth: 29f,
                extraPartOnGUI: (Rect rect) =>
                {
                    return Widgets.InfoCardButton(rect.x + 5f, rect.y + (rect.height - 24f) / 2f, recipe);
                },
                revalidateWorldClickTarget: null,
                playSelectionSound: true,
                orderInPriority: -recipe.displayPriority
            );
        }
        // 检查配方是否匹配分类
        private bool RecipeMatchesCategory(RecipeDef recipe, ProductionCategory category)
        {
            var recipeCategory = GetRecipeCategory(recipe);
            return recipeCategory == category;
        }
        // 获取配方的分类
        private ProductionCategory? GetRecipeCategory(RecipeDef recipe)
        {
            if (recipe.products == null || recipe.products.Count == 0)
                return null;

            ThingDef productDef = recipe.products[0].thingDef;
            if (productDef == null)
                return null;

            // 检查产品是否有分类组件
            var categoryComp = productDef.GetCompProperties<CompProperties_ProductionCategory>();
            if (categoryComp != null)
                return categoryComp.category;

            // 如果没有分类组件，根据产品类型推断
            return InferCategoryFromThingDef(productDef);
        }
        // 根据ThingDef推断分类
        private ProductionCategory? InferCategoryFromThingDef(ThingDef thingDef)
        {
            if (thingDef.IsMeleeWeapon || thingDef.IsRangedWeapon)
                return ProductionCategory.Weapon;

            if (thingDef.IsApparel || thingDef.equipmentType != EquipmentType.None)
                return ProductionCategory.Equipment;

            if (thingDef.race != null && thingDef.race.IsMechanoid)
                return ProductionCategory.Mechanoid;

            return null;
        }
        // 其余方法保持不变（DoStorageButton, DoGodModeButtons, DoOrdersListing, DoOrderRow 等）
        private void DoStorageButton(Rect rect)
        {
            if (Widgets.ButtonText(rect, "WULA_ViewStorage".Translate()))
            {
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            if (Mouse.IsOver(rect))
            {
                TooltipHandler.TipRegion(rect, GetStorageTooltip());
            }
        }
        private string GetStorageTooltip()
        {
            var globalStorage = Find.World.GetComponent<GlobalStorageWorldComponent>();
            if (globalStorage == null)
                return "WULA_NoGlobalStorage".Translate();
            StringBuilder sb = new StringBuilder();
            // 输入存储（原材料）
            sb.AppendLine("WULA_InputStorage".Translate() + ":");
            sb.AppendLine();
            var inputItems = globalStorage.inputStorage
                .Where(kvp => kvp.Value > 0)
                .OrderByDescending(kvp => kvp.Value)
                .ThenBy(kvp => kvp.Key.label)
                .ToList();
            if (inputItems.Count == 0)
            {
                sb.AppendLine("WULA_NoItems".Translate());
            }
            else
            {
                foreach (var kvp in inputItems)
                {
                    sb.AppendLine($"  {kvp.Value} {kvp.Key.LabelCap}");
                }
            }
            sb.AppendLine();
            // 输出存储（产品）
            sb.AppendLine("WULA_OutputStorage".Translate() + ":");
            sb.AppendLine();
            var outputItems = globalStorage.outputStorage
                .Where(kvp => kvp.Value > 0)
                .OrderByDescending(kvp => kvp.Value)
                .ThenBy(kvp => kvp.Key.label)
                .ToList();
            if (outputItems.Count == 0)
            {
                sb.AppendLine("WULA_NoItems".Translate());
            }
            else
            {
                foreach (var kvp in outputItems)
                {
                    sb.AppendLine($"  {kvp.Value} {kvp.Key.LabelCap}");
                }
            }
            return sb.ToString();
        }

        // 修改：将开发者模式按钮改为上帝模式按钮
        private void DoGodModeButtons(Rect rect)
        {
            Rect button1Rect = new Rect(rect.x, rect.y, rect.width / 2 - 5f, rect.height);
            Rect button2Rect = new Rect(rect.x + rect.width / 2 + 5f, rect.y, rect.width / 2 - 5f, rect.height);

            if (Widgets.ButtonText(button1Rect, "GOD: Add Resources"))
            {
                AddTestResources();
            }

            if (Widgets.ButtonText(button2Rect, "GOD: Spawn Products"))
            {
                SpawnOutputProducts();
            }
        }

        private void AddTestResources()
        {
            var globalStorage = Find.World.GetComponent<GlobalStorageWorldComponent>();
            if (globalStorage != null)
            {
                // 添加200钢铁
                ThingDef steelDef = ThingDefOf.Steel;
                globalStorage.AddToInputStorage(steelDef, 200);

                // 添加100零部件
                ThingDef componentDef = ThingDefOf.ComponentIndustrial;
                globalStorage.AddToInputStorage(componentDef, 100);

                Messages.Message("Added 200 Steel and 100 Components to global storage", MessageTypeDefOf.PositiveEvent);
            }
        }

        private void SpawnOutputProducts()
        {
            var globalStorage = Find.World.GetComponent<GlobalStorageWorldComponent>();
            if (globalStorage != null && SelTable != null && SelTable.Spawned)
            {
                Map map = SelTable.Map;
                IntVec3 spawnCell = SelTable.Position;
                int totalSpawned = 0;

                // 复制列表以避免修改时枚举
                var outputCopy = new Dictionary<ThingDef, int>(globalStorage.outputStorage);

                foreach (var kvp in outputCopy)
                {
                    ThingDef thingDef = kvp.Key;
                    int count = kvp.Value;

                    if (count > 0)
                    {
                        // 创建物品并放置到地图上
                        while (count > 0)
                        {
                            int stackSize = Mathf.Min(count, thingDef.stackLimit);
                            Thing thing = ThingMaker.MakeThing(thingDef);
                            thing.stackCount = stackSize;

                            if (GenPlace.TryPlaceThing(thing, spawnCell, map, ThingPlaceMode.Near))
                            {
                                globalStorage.RemoveFromOutputStorage(thingDef, stackSize);
                                count -= stackSize;
                                totalSpawned += stackSize;
                            }
                            else
                            {
                                break;
                            }
                        }
                    }
                }

                Messages.Message($"Spawned {totalSpawned} items at worktable location", MessageTypeDefOf.PositiveEvent);
            }
        }

        private GlobalProductionOrder DoOrdersListing(Rect rect)
        {
            GlobalProductionOrder result = null;

            Widgets.DrawMenuSection(rect);
            Rect outRect = rect.ContractedBy(5f);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, viewHeight);

            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);

            float curY = 0f;
            for (int i = 0; i < SelTable.globalOrderStack.orders.Count; i++)
            {
                var order = SelTable.globalOrderStack.orders[i];

                // 增加订单行高度
                Rect orderRect = new Rect(0f, curY, viewRect.width, 90f);

                if (DoOrderRow(orderRect, order))
                {
                    result = order;
                }

                curY += 95f; // 增加行间距

                // 分隔线
                if (i < SelTable.globalOrderStack.orders.Count - 1)
                {
                    Widgets.DrawLineHorizontal(0f, curY - 2f, viewRect.width);
                    curY += 5f;
                }
            }

            if (Event.current.type == EventType.Layout)
            {
                viewHeight = curY;
            }

            Widgets.EndScrollView();
            return result;
        }

        // 简化：DoOrderRow 方法，移除材质选择按钮
        private bool DoOrderRow(Rect rect, GlobalProductionOrder order)
        {
            Widgets.DrawHighlightIfMouseover(rect);

            // 增加内边距和行高
            float padding = 8f;
            float lineHeight = 20f;

            // 图标区域
            float iconSize = 32f;
            Rect iconRect = new Rect(rect.x + padding, rect.y + padding, iconSize, iconSize);

            // 绘制配方图标
            if (order.recipe.UIIconThing != null)
            {
                Widgets.ThingIcon(iconRect, order.recipe.UIIconThing);
            }
            else if (order.recipe.UIIcon != null)
            {
                GUI.DrawTexture(iconRect, order.recipe.UIIcon);
            }

            // 订单信息区域
            float infoX = rect.x + padding + iconSize + 8f;
            float infoWidth = rect.width - (padding * 2 + iconSize + 8f + 100f);

            Rect infoRect = new Rect(infoX, rect.y + padding, infoWidth, lineHeight);
            Widgets.Label(infoRect, order.Label);

            Rect descRect = new Rect(infoX, rect.y + padding + lineHeight + 2f, infoWidth, lineHeight);
            Widgets.Label(descRect, order.Description);

            // 状态显示区域
            Rect statusRect = new Rect(infoX, rect.y + padding + (lineHeight + 2f) * 2, infoWidth, lineHeight);

            if (order.state == GlobalProductionOrder.ProductionState.Producing)
            {
                // 进度条
                Rect progressRect = new Rect(infoX, rect.y + padding + (lineHeight + 2f) * 2, infoWidth, 18f);
                Widgets.FillableBar(progressRect, order.progress);

                // 进度文本
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(progressRect, $"{order.progress:P0}");
                Text.Anchor = TextAnchor.UpperLeft;
            }
            else if (order.state == GlobalProductionOrder.ProductionState.Gathering)
            {
                string statusText = "WULA_GatheringMaterials".Translate();
                if (order.paused) statusText = $"[||] {statusText}";
                Widgets.Label(statusRect, statusText);
            }
            else
            {
                string statusText = order.state switch
                {
                    GlobalProductionOrder.ProductionState.Gathering => "WULA_WaitingForResources".Translate(),
                    GlobalProductionOrder.ProductionState.Completed => "WULA_Completed".Translate(),
                    _ => "WULA_Unknown".Translate()
                };

                if (order.paused && order.state != GlobalProductionOrder.ProductionState.Completed)
                {
                    statusText = $"[||] {statusText}";
                }

                Widgets.Label(statusRect, statusText);
            }

            // 控制按钮区域
            float buttonY = rect.y + padding;
            float buttonWidth = 40f;
            float buttonSpacing = 5f;

            // 计算按钮位置（从右向左排列）
            float currentX = rect.xMax;

            // 删除按钮（最右边）
            Rect deleteButtonRect = new Rect(currentX - buttonWidth, buttonY, buttonWidth, 25f);
            currentX -= (buttonWidth + buttonSpacing);

            // 暂停/恢复按钮
            Rect pauseButtonRect = new Rect(currentX - buttonWidth, buttonY, buttonWidth, 25f);
            currentX -= (buttonWidth + buttonSpacing);

            // 上帝模式：立刻完成按钮
            Rect completeButtonRect = new Rect(currentX - buttonWidth, buttonY, buttonWidth, 25f);

            // 绘制删除按钮
            if (Widgets.ButtonText(deleteButtonRect, "WULA_Delete".Translate()))
            {
                SelTable.globalOrderStack.Delete(order);
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            // 绘制暂停/恢复按钮
            string pauseButtonText = order.paused ? "WULA_Resume".Translate() : "WULA_Pause".Translate();
            if (Widgets.ButtonText(pauseButtonRect, pauseButtonText))
            {
                order.paused = !order.paused;
                order.UpdateState();
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            // 绘制上帝模式按钮（仅上帝模式下可见）
            if (DebugSettings.godMode && order.state != GlobalProductionOrder.ProductionState.Completed)
            {
                if (Widgets.ButtonText(completeButtonRect, "GOD: Complete"))
                {
                    CompleteOrderImmediately(order);
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }

                if (Mouse.IsOver(completeButtonRect))
                {
                    TooltipHandler.TipRegion(completeButtonRect, "Instantly complete this order (God Mode Only)");
                }
            }

            // 资源检查提示
            if (!order.HasEnoughResources() &&
                order.state == GlobalProductionOrder.ProductionState.Gathering &&
                !order.paused)
            {
                TooltipHandler.TipRegion(rect, "WULA_InsufficientResources".Translate());
                GUI.color = Color.red;
                Widgets.DrawBox(rect, 2);
                GUI.color = Color.white;
            }

            // 添加材料信息的Tooltip
            if (Mouse.IsOver(rect))
            {
                TooltipHandler.TipRegion(rect, order.GetIngredientsTooltip());
            }

            return Mouse.IsOver(rect);
        }

        // 新增：立刻完成订单的方法
        private void CompleteOrderImmediately(GlobalProductionOrder order)
        {
            if (order.state == GlobalProductionOrder.ProductionState.Completed)
                return;

            var globalStorage = Find.World.GetComponent<GlobalStorageWorldComponent>();
            if (globalStorage == null)
                return;

            // 检查是否有足够资源
            bool hasEnoughResources = order.HasEnoughResources();

            if (!hasEnoughResources)
            {
                // 上帝模式下，如果没有足够资源，显示确认对话框
                Find.WindowStack.Add(new Dialog_MessageBox(
                    "This order doesn't have enough resources. Complete anyway? (God Mode)",
                    "Yes, Complete Anyway",
                    () => ForceCompleteOrder(order),
                    "Cancel",
                    null,
                    "Complete Without Resources",
                    false,
                    null,
                    null
                ));
            }
            else
            {
                // 有足够资源，正常完成
                ForceCompleteOrder(order);
            }
        }

        // 强制完成订单（上帝模式）
        private void ForceCompleteOrder(GlobalProductionOrder order)
        {
            var globalStorage = Find.World.GetComponent<GlobalStorageWorldComponent>();
            if (globalStorage == null)
                return;

            // 计算需要完成的数量
            int remainingCount = order.targetCount - order.currentCount;

            if (remainingCount <= 0)
                return;

            // 尝试消耗资源（如果可能）
            bool resourcesConsumed = order.ConsumeResources();

            if (!resourcesConsumed)
            {
                Log.Message($"[GOD MODE] Could not consume resources for {order.recipe.defName}, completing without resource consumption");
            }

            // 添加产品到输出存储
            foreach (var product in order.recipe.products)
            {
                int totalCount = product.count * remainingCount;
                globalStorage.AddToOutputStorage(product.thingDef, totalCount);
            }

            // 更新订单状态
            order.currentCount = order.targetCount;
            order.state = GlobalProductionOrder.ProductionState.Completed;
            order.progress = 0f;

            // 显示完成消息
            Messages.Message($"GOD MODE: Completed order for {order.recipe.LabelCap} ({remainingCount} units)", MessageTypeDefOf.PositiveEvent);
        }

        public override void TabUpdate()
        {
            base.TabUpdate();
        }
    }
}
