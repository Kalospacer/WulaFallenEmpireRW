using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    public class ITab_ResourceSubmitterContents : ITab
    {
        private Vector2 scrollPosition;
        private float scrollViewHeight;
        
        private static readonly Vector2 WinSize = new Vector2(420f, 480f);
        
        protected Building_ResourceSubmitter SelSubmitter => (Building_ResourceSubmitter)base.SelThing;

        public ITab_ResourceSubmitterContents()
        {
            size = WinSize;
            labelKey = "WULA_SubmitterContents";
            tutorTag = "SubmitterContents";
        }

        protected override void FillTab()
        {
            Rect mainRect = new Rect(0f, 0f, WinSize.x, WinSize.y).ContractedBy(10f);
            
            // 标题区域
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(mainRect.x, mainRect.y, mainRect.width, 30f), "WULA_SubmitterContents".Translate());
            Text.Font = GameFont.Small;
            
            // 状态信息
            Rect statusRect = new Rect(mainRect.x, mainRect.y + 35f, mainRect.width, 40f);
            DoStatusInfo(statusRect);
            
            // 存储物品列表
            Rect itemsRect = new Rect(mainRect.x, mainRect.y + 80f, mainRect.width, mainRect.height - 120f);
            DoItemsListing(itemsRect);
            
            // 操作按钮区域
            Rect buttonsRect = new Rect(mainRect.x, mainRect.yMax - 35f, mainRect.width, 30f);
            DoActionButtons(buttonsRect);
        }

        private void DoStatusInfo(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            Rect innerRect = rect.ContractedBy(5f);
            
            // 运行状态
            string statusText = SelSubmitter.IsOperational ? 
                "WULA_Operational".Translate() : "WULA_Inoperative".Translate();
            Color statusColor = SelSubmitter.IsOperational ? Color.green : Color.red;
            
            Text.Anchor = TextAnchor.MiddleLeft;
            Rect statusLabelRect = new Rect(innerRect.x, innerRect.y, innerRect.width * 0.6f, innerRect.height);
            Widgets.Label(statusLabelRect, "WULA_Status".Translate() + ": " + statusText);
            
            // 物品数量
            var storedItems = SelSubmitter.GetStoredItems();
            int totalItems = storedItems.Count;
            int totalStacks = storedItems.Sum(item => item.stackCount);
            
            Rect countRect = new Rect(innerRect.x + innerRect.width * 0.6f, innerRect.y, innerRect.width * 0.4f, innerRect.height);
            Widgets.Label(countRect, $"{totalItems} {"WULA_Items".Translate()} ({totalStacks} {"WULA_Stacks".Translate()})");
            Text.Anchor = TextAnchor.UpperLeft;
            
            // 状态颜色指示
            GUI.color = statusColor;
            Widgets.DrawBox(new Rect(statusLabelRect.x - 15f, statusLabelRect.y + 7f, 10f, 10f), 1);
            GUI.color = Color.white;
        }

        private void DoItemsListing(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            Rect outRect = rect.ContractedBy(5f);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, scrollViewHeight);
            
            var storedItems = SelSubmitter.GetStoredItems();
            
            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
            
            float curY = 0f;
            
            // 列标题
            Rect headerRect = new Rect(0f, curY, viewRect.width, 25f);
            DoColumnHeaders(headerRect);
            curY += 30f;
            
            if (storedItems.Count == 0)
            {
                Rect emptyRect = new Rect(0f, curY, viewRect.width, 30f);
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(emptyRect, "WULA_NoItemsInStorage".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                curY += 35f;
            }
            else
            {
                // 按物品类型分组显示
                var groupedItems = storedItems
                    .GroupBy(item => item.def)
                    .OrderByDescending(g => g.Sum(item => item.stackCount))
                    .ThenBy(g => g.Key.label);
                
                foreach (var group in groupedItems)
                {
                    ThingDef thingDef = group.Key;
                    int totalCount = group.Sum(item => item.stackCount);
                    int stackCount = group.Count();
                    
                    Rect itemRect = new Rect(0f, curY, viewRect.width, 28f);
                    if (DoItemRow(itemRect, thingDef, totalCount, stackCount))
                    {
                        // 鼠标悬停时显示详细信息
                        string tooltip = GetItemTooltip(thingDef, totalCount, stackCount);
                        TooltipHandler.TipRegion(itemRect, tooltip);
                    }
                    
                    curY += 32f;
                    
                    // 分隔线
                    if (curY < viewRect.height - 5f)
                    {
                        Widgets.DrawLineHorizontal(0f, curY - 2f, viewRect.width);
                        curY += 5f;
                    }
                }
            }
            
            if (Event.current.type == EventType.Layout)
            {
                scrollViewHeight = curY;
            }
            
            Widgets.EndScrollView();
        }

        private void DoColumnHeaders(Rect rect)
        {
            float columnWidth = rect.width / 4f;
            
            // 物品名称列
            Rect nameRect = new Rect(rect.x, rect.y, columnWidth * 2f, rect.height);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(nameRect, "WULA_ItemName".Translate());
            
            // 数量列
            Rect countRect = new Rect(rect.x + columnWidth * 2f, rect.y, columnWidth, rect.height);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(countRect, "WULA_Count".Translate());
            
            // 堆叠列
            Rect stacksRect = new Rect(rect.x + columnWidth * 3f, rect.y, columnWidth, rect.height);
            Widgets.Label(stacksRect, "WULA_Stacks".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            
            // 标题下划线
            Widgets.DrawLineHorizontal(rect.x, rect.yMax - 2f, rect.width);
        }

        private bool DoItemRow(Rect rect, ThingDef thingDef, int totalCount, int stackCount)
        {
            Widgets.DrawHighlightIfMouseover(rect);
            
            float columnWidth = rect.width / 4f;
            
            // 物品图标
            Rect iconRect = new Rect(rect.x + 2f, rect.y + 2f, 24f, 24f);
            Widgets.ThingIcon(iconRect, thingDef);
            
            // 物品名称
            Rect nameRect = new Rect(rect.x + 30f, rect.y, columnWidth * 2f - 30f, rect.height);
            Text.Anchor = TextAnchor.MiddleLeft;
            string label = thingDef.LabelCap;
            if (label.Length > 25)
            {
                label = label.Substring(0, 25) + "...";
            }
            Widgets.Label(nameRect, label);
            
            // 总数量
            Rect countRect = new Rect(rect.x + columnWidth * 2f, rect.y, columnWidth, rect.height);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(countRect, totalCount.ToString());
            
            // 堆叠数量
            Rect stacksRect = new Rect(rect.x + columnWidth * 3f, rect.y, columnWidth, rect.height);
            Widgets.Label(stacksRect, stackCount.ToString());
            Text.Anchor = TextAnchor.UpperLeft;
            
            return Mouse.IsOver(rect);
        }

        private string GetItemTooltip(ThingDef thingDef, int totalCount, int stackCount)
        {
            return string.Format("WULA_ItemTooltip".Translate(), 
                thingDef.LabelCap, 
                totalCount, 
                stackCount,
                thingDef.BaseMarketValue * totalCount);
        }

        private void DoActionButtons(Rect rect)
        {
            float buttonWidth = rect.width / 2f - 5f;
            
            // 提交按钮
            Rect submitRect = new Rect(rect.x, rect.y, buttonWidth, rect.height);
            bool hasItems = SelSubmitter.GetStoredItems().Count > 0;
            bool isOperational = SelSubmitter.IsOperational;
            
            string submitLabel = "WULA_SubmitToStorage".Translate();
            string submitDesc = "WULA_SubmitToStorageDesc".Translate();
            
            if (!isOperational)
            {
                submitLabel = "WULA_DeviceInoperative".Translate();
                submitDesc = GetInoperativeReason();
            }
            else if (!hasItems)
            {
                submitLabel = "WULA_NoItemsToSubmit".Translate();
                submitDesc = "WULA_NoItemsToSubmitDesc".Translate();
            }
            
            if (Widgets.ButtonText(submitRect, submitLabel))
            {
                if (isOperational && hasItems)
                {
                    SelSubmitter.SubmitContentsToStorage();
                }
                else if (!isOperational)
                {
                    Messages.Message(GetInoperativeReason(), MessageTypeDefOf.RejectInput);
                }
                else
                {
                    Messages.Message("WULA_NoItemsToSubmit".Translate(), MessageTypeDefOf.RejectInput);
                }
            }
            
            // 工具提示
            if (Mouse.IsOver(submitRect))
            {
                TooltipHandler.TipRegion(submitRect, submitDesc);
            }
            
            // 查看全局存储按钮
            Rect storageRect = new Rect(rect.x + buttonWidth + 10f, rect.y, buttonWidth, rect.height);
            if (Widgets.ButtonText(storageRect, "WULA_ViewGlobalStorage".Translate()))
            {
                Find.WindowStack.Add(new Dialog_GlobalStorage());
            }
            
            if (Mouse.IsOver(storageRect))
            {
                TooltipHandler.TipRegion(storageRect, "WULA_ViewGlobalStorageDesc".Translate());
            }
        }

        private string GetInoperativeReason()
        {
            var submitter = SelSubmitter;
            
            if (submitter.powerComp != null && !submitter.powerComp.PowerOn)
                return "WULA_NoPower".Translate();
                
            if (submitter.refuelableComp != null && !submitter.refuelableComp.HasFuel)
                return "WULA_NoFuel".Translate();
                
            if (submitter.flickableComp != null && !submitter.flickableComp.SwitchIsOn)
                return "WULA_SwitchOff".Translate();
                
            return "WULA_UnknownReason".Translate();
        }

        public override void TabUpdate()
        {
            base.TabUpdate();
        }
    }
    
    // 简单的全局存储查看对话框
    public class Dialog_GlobalStorage : Window
    {
        private Vector2 scrollPosition;
        private float scrollViewHeight;
        
        public override Vector2 InitialSize => new Vector2(500f, 600f);
        
        public Dialog_GlobalStorage()
        {
            forcePause = false;
            doCloseX = true;
            doCloseButton = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
        }
        
        public override void DoWindowContents(Rect inRect)
        {
            var globalStorage = Find.World.GetComponent<GlobalStorageWorldComponent>();
            if (globalStorage == null)
            {
                Widgets.Label(inRect, "WULA_NoGlobalStorage".Translate());
                return;
            }
            
            Rect titleRect = new Rect(0f, 0f, inRect.width, 30f);
            Text.Font = GameFont.Medium;
            Widgets.Label(titleRect, "WULA_GlobalStorage".Translate());
            Text.Font = GameFont.Small;
            
            // 输入存储
            Rect inputRect = new Rect(0f, 40f, inRect.width, (inRect.height - 100f) / 2f);
            DoStorageSection(inputRect, globalStorage.inputStorage, "WULA_InputStorage".Translate());
            
            // 输出存储
            Rect outputRect = new Rect(0f, 40f + (inRect.height - 100f) / 2f + 10f, inRect.width, (inRect.height - 100f) / 2f);
            DoStorageSection(outputRect, globalStorage.outputStorage, "WULA_OutputStorage".Translate());
        }
        
        private void DoStorageSection(Rect rect, Dictionary<ThingDef, int> storage, string label)
        {
            Widgets.DrawMenuSection(rect);
            Rect innerRect = rect.ContractedBy(5f);
            
            // 标题
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(innerRect.x, innerRect.y, innerRect.width, 25f), label);
            Text.Font = GameFont.Small;
            
            Rect listRect = new Rect(innerRect.x, innerRect.y + 30f, innerRect.width, innerRect.height - 35f);
            DoStorageList(listRect, storage);
        }
        
        private void DoStorageList(Rect rect, Dictionary<ThingDef, int> storage)
        {
            Rect outRect = rect;
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, scrollViewHeight);
            
            var items = storage
                .Where(kvp => kvp.Value > 0)
                .OrderByDescending(kvp => kvp.Value)
                .ThenBy(kvp => kvp.Key.label)
                .ToList();
            
            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
            
            float curY = 0f;
            
            if (items.Count == 0)
            {
                Rect emptyRect = new Rect(0f, curY, viewRect.width, 30f);
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(emptyRect, "WULA_NoItems".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                curY += 35f;
            }
            else
            {
                foreach (var kvp in items)
                {
                    Rect itemRect = new Rect(0f, curY, viewRect.width, 25f);
                    DoStorageItemRow(itemRect, kvp.Key, kvp.Value);
                    curY += 28f;
                }
            }
            
            if (Event.current.type == EventType.Layout)
            {
                scrollViewHeight = curY;
            }
            
            Widgets.EndScrollView();
        }
        
        private void DoStorageItemRow(Rect rect, ThingDef thingDef, int count)
        {
            Widgets.DrawHighlightIfMouseover(rect);
            
            // 图标
            Rect iconRect = new Rect(rect.x + 2f, rect.y + 2f, 20f, 20f);
            Widgets.ThingIcon(iconRect, thingDef);
            
            // 名称
            Rect nameRect = new Rect(rect.x + 25f, rect.y, rect.width - 80f, rect.height);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(nameRect, thingDef.LabelCap);
            
            // 数量
            Rect countRect = new Rect(rect.xMax - 50f, rect.y, 50f, rect.height);
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(countRect, count.ToString());
            Text.Anchor = TextAnchor.UpperLeft;
            
            // 工具提示
            if (Mouse.IsOver(rect))
            {
                string tooltip = $"{thingDef.LabelCap}\n{count} {"WULA_Items".Translate()}\n{"WULA_Value".Translate()}: {thingDef.BaseMarketValue * count}";
                TooltipHandler.TipRegion(rect, tooltip);
            }
        }
    }
}
