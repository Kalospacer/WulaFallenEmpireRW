// ITab_GlobalBills.cs (添加存储查看功能)
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
            
            // 存储查看按钮 - 放在标题旁边
            Rect storageButtonRect = new Rect(mainRect.xMax - 120f, mainRect.y, 120f, 25f);
            DoStorageButton(storageButtonRect);
            
            // 开发模式按钮区域
            if (Prefs.DevMode)
            {
                Rect devButtonRect = new Rect(mainRect.x, mainRect.y + 35f, mainRect.width, 25f);
                DoDevButtons(devButtonRect);
            }
            
            // 订单列表区域 - 调整位置
            float ordersRectY = Prefs.DevMode ? mainRect.y + 65f : mainRect.y + 35f;
            Rect ordersRect = new Rect(mainRect.x, ordersRectY, mainRect.width, mainRect.height - (Prefs.DevMode ? 110f : 80f));
            mouseoverOrder = DoOrdersListing(ordersRect);
            
            // 添加订单按钮
            Rect addButtonRect = new Rect(mainRect.x, mainRect.yMax - 35f, mainRect.width, 30f);
            if (Widgets.ButtonText(addButtonRect, "WULA_AddProductionOrder".Translate()))
            {
                Find.WindowStack.Add(new FloatMenu(GenerateRecipeOptions()));
            }
            
            // 绘制鼠标悬停信息 - 使用简单的Tooltip方式
            if (mouseoverOrder != null)
            {
                // 使用Tooltip显示材料信息，不绘制额外窗口
                // 信息已经在DoOrderRow中的TooltipHandler显示
            }
        }

        // 新增：存储查看按钮
        private void DoStorageButton(Rect rect)
        {
            // 绘制按钮
            if (Widgets.ButtonText(rect, "WULA_ViewStorage".Translate()))
            {
                // 点击按钮时也可以做一些事情，比如打开详细存储窗口
                // 暂时只显示Tooltip
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
            
            // 鼠标悬停时显示存储信息Tooltip
            if (Mouse.IsOver(rect))
            {
                TooltipHandler.TipRegion(rect, GetStorageTooltip());
            }
        }

        // 新增：获取存储信息的Tooltip
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
                    sb.AppendLine($"  {kvp.Value} {kvp.Key.label}");
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
                    sb.AppendLine($"  {kvp.Value} {kvp.Key.label}");
                }
            }
            
            // 添加存储统计信息
            sb.AppendLine();
            sb.AppendLine("WULA_StorageStats".Translate());
            sb.AppendLine($"  {inputItems.Count} {("WULA_InputItems".Translate())}");
            sb.AppendLine($"  {outputItems.Count} {("WULA_OutputItems".Translate())}");
            
            return sb.ToString();
        }

        private void DoDevButtons(Rect rect)
        {
            Rect button1Rect = new Rect(rect.x, rect.y, rect.width / 2 - 5f, rect.height);
            Rect button2Rect = new Rect(rect.x + rect.width / 2 + 5f, rect.y, rect.width / 2 - 5f, rect.height);
            
            if (Widgets.ButtonText(button1Rect, "DEV: Add Resources"))
            {
                AddTestResources();
            }
            
            if (Widgets.ButtonText(button2Rect, "DEV: Spawn Products"))
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
                Log.Message("[DEBUG] Added test resources");
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
                Log.Message($"[DEBUG] Spawned {totalSpawned} output products");
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

        private bool DoOrderRow(Rect rect, GlobalProductionOrder order)
        {
            Widgets.DrawHighlightIfMouseover(rect);
            
            // 增加内边距和行高
            float padding = 8f;
            float lineHeight = 20f;
            
            // 订单信息
            Rect infoRect = new Rect(rect.x + padding, rect.y + padding, rect.width - 100f, lineHeight);
            Widgets.Label(infoRect, order.Label);
            
            Rect descRect = new Rect(rect.x + padding, rect.y + padding + lineHeight + 2f, rect.width - 100f, lineHeight);
            Widgets.Label(descRect, order.Description);
            
            // 状态显示区域
            Rect statusRect = new Rect(rect.x + padding, rect.y + padding + (lineHeight + 2f) * 2, rect.width - 100f, lineHeight);
            
            if (order.state == GlobalProductionOrder.ProductionState.Producing)
            {
                // 进度条
                Rect progressRect = new Rect(rect.x + padding, rect.y + padding + (lineHeight + 2f) * 2, rect.width - 100f, 18f);
                Widgets.FillableBar(progressRect, order.progress);
                
                // 进度文本
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(progressRect, $"{order.progress:P0}");
                Text.Anchor = TextAnchor.UpperLeft;
            }
            else
            {
                string statusText = order.state switch
                {
                    GlobalProductionOrder.ProductionState.Waiting => "WULA_WaitingForResources".Translate(),
                    GlobalProductionOrder.ProductionState.Completed => "WULA_Completed".Translate(),
                    _ => "WULA_Unknown".Translate()
                };
                
                // 如果暂停，在状态前添加暂停标识
                if (order.paused && order.state != GlobalProductionOrder.ProductionState.Completed)
                {
                    statusText = $"[Paused] {statusText}";
                }
                
                Widgets.Label(statusRect, statusText);
            }
            
            // 控制按钮
            float buttonY = rect.y + padding;
            Rect pauseButtonRect = new Rect(rect.xMax - 90f, buttonY, 40f, 25f);
            
            string pauseButtonText = order.paused ? "WULA_Resume".Translate() : "WULA_Pause".Translate();
            if (Widgets.ButtonText(pauseButtonRect, pauseButtonText))
            {
                order.paused = !order.paused;
                
                // 暂停/恢复时更新状态
                order.UpdateState();
                
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
            
            Rect deleteButtonRect = new Rect(rect.xMax - 45f, buttonY, 40f, 25f);
            if (Widgets.ButtonText(deleteButtonRect, "WULA_Delete".Translate()))
            {
                SelTable.globalOrderStack.Delete(order);
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
            
            // 资源检查提示 - 只在等待资源且不暂停时显示红色边框
            if (!order.HasEnoughResources() && 
                order.state == GlobalProductionOrder.ProductionState.Waiting && 
                !order.paused)
            {
                TooltipHandler.TipRegion(rect, "WULA_InsufficientResources".Translate());
                GUI.color = Color.red;
                Widgets.DrawBox(rect, 2);
                GUI.color = Color.white;
            }
            
            // 添加材料信息的Tooltip - 这是核心功能
            if (Mouse.IsOver(rect))
            {
                TooltipHandler.TipRegion(rect, order.GetIngredientsTooltip());
            }
            
            return Mouse.IsOver(rect);
        }

        private List<FloatMenuOption> GenerateRecipeOptions()
        {
            var options = new List<FloatMenuOption>();
            
            foreach (var recipe in SelTable.def.AllRecipes)
            {
                if (recipe.AvailableNow && recipe.AvailableOnNow(SelTable))
                {
                    string label = recipe.LabelCap;
                    
                    options.Add(new FloatMenuOption(label, () =>
                    {
                        var newOrder = new GlobalProductionOrder
                        {
                            recipe = recipe,
                            targetCount = 1,
                            paused = true // 初始暂停
                        };
                        
                        SelTable.globalOrderStack.AddOrder(newOrder);
                        SoundDefOf.Click.PlayOneShotOnCamera();
                        Log.Message($"[DEBUG] Added order for {recipe.defName}");
                    }));
                }
            }
            
            if (options.Count == 0)
            {
                options.Add(new FloatMenuOption("WULA_NoAvailableRecipes".Translate(), null));
            }
            
            return options;
        }

        public override void TabUpdate()
        {
            base.TabUpdate();
        }
    }
}
