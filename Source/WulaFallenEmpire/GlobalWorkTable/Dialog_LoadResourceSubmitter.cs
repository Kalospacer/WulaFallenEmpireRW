using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    public class Dialog_LoadResourceSubmitter : Window
    {
        private CompResourceSubmitter submitterComp;
        private Vector2 scrollPosition;
        private float scrollViewHeight;
        private List<TransferableOneWay> transferables;
        
        public override Vector2 InitialSize => new Vector2(800f, 600f);
        
        public Dialog_LoadResourceSubmitter(CompResourceSubmitter submitterComp)
        {
            this.submitterComp = submitterComp;
            forcePause = true;
            doCloseX = true;
            doCloseButton = true;
            absorbInputAroundWindow = true;
            
            transferables = new List<TransferableOneWay>();
            RefreshTransferables();
        }
        
        public override void DoWindowContents(Rect inRect)
        {
            Rect titleRect = new Rect(0f, 0f, inRect.width, 35f);
            Text.Font = GameFont.Medium;
            Widgets.Label(titleRect, "WULA_LoadResourceSubmitter".Translate());
            Text.Font = GameFont.Small;
            
            // 简化的物品列表
            Rect listRect = new Rect(0f, 40f, inRect.width, inRect.height - 100f);
            DoSimpleTransferableList(listRect);
            
            // 按钮区域
            Rect buttonRect = new Rect(0f, inRect.height - 55f, inRect.width, 30f);
            DoButtons(buttonRect);
            
            // 状态信息
            Rect statusRect = new Rect(0f, inRect.height - 25f, inRect.width, 25f);
            DoStatusInfo(statusRect);
        }
        
        private void DoSimpleTransferableList(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            Rect outRect = rect.ContractedBy(10f);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, scrollViewHeight);
            
            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
            
            float curY = 0f;
            
            // 列标题
            Rect headerRect = new Rect(0f, curY, viewRect.width, 25f);
            DoColumnHeaders(headerRect);
            curY += 30f;
            
            if (transferables.Count == 0)
            {
                Rect emptyRect = new Rect(0f, curY, viewRect.width, 30f);
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(emptyRect, "WULA_NoItemsAvailable".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                curY += 35f;
            }
            else
            {
                foreach (var transferable in transferables)
                {
                    if (transferable.things.Count == 0) continue;
                    
                    Thing sampleThing = transferable.things[0];
                    Rect rowRect = new Rect(0f, curY, viewRect.width, 30f);
                    
                    if (DoTransferableRow(rowRect, transferable, sampleThing))
                    {
                        TooltipHandler.TipRegion(rowRect, GetThingTooltip(sampleThing, transferable.CountToTransfer));
                    }
                    
                    curY += 35f;
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
            
            // 可用数量列
            Rect availableRect = new Rect(rect.x + columnWidth * 2f, rect.y, columnWidth, rect.height);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(availableRect, "WULA_Available".Translate());
            
            // 装载数量列
            Rect loadRect = new Rect(rect.x + columnWidth * 3f, rect.y, columnWidth, rect.height);
            Widgets.Label(loadRect, "WULA_ToLoad".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            
            // 标题下划线
            Widgets.DrawLineHorizontal(rect.x, rect.yMax - 2f, rect.width);
        }
        
        private bool DoTransferableRow(Rect rect, TransferableOneWay transferable, Thing sampleThing)
        {
            Widgets.DrawHighlightIfMouseover(rect);
            
            float columnWidth = rect.width / 4f;
            
            // 图标
            Rect iconRect = new Rect(rect.x + 2f, rect.y + 2f, 26f, 26f);
            Widgets.ThingIcon(iconRect, sampleThing);
            
            // 名称
            Rect nameRect = new Rect(rect.x + 32f, rect.y, columnWidth * 2f - 32f, rect.height);
            Text.Anchor = TextAnchor.MiddleLeft;
            string label = sampleThing.LabelCap;
            if (label.Length > 25)
            {
                label = label.Substring(0, 25) + "...";
            }
            Widgets.Label(nameRect, label);
            
            // 可用数量
            int availableCount = transferable.things.Sum(t => t.stackCount);
            Rect availableRect = new Rect(rect.x + columnWidth * 2f, rect.y, columnWidth, rect.height);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(availableRect, availableCount.ToString());
            
            // 装载数量调整
            Rect adjustRect = new Rect(rect.x + columnWidth * 3f, rect.y, columnWidth, rect.height);
            DoCountAdjust(adjustRect, transferable, availableCount);
            
            Text.Anchor = TextAnchor.UpperLeft;
            
            return Mouse.IsOver(rect);
        }
        
        private void DoCountAdjust(Rect rect, TransferableOneWay transferable, int availableCount)
        {
            int currentCount = transferable.CountToTransfer;
            
            Rect labelRect = new Rect(rect.x, rect.y, 40f, rect.height);
            Rect minusRect = new Rect(rect.x + 45f, rect.y, 25f, rect.height);
            Rect plusRect = new Rect(rect.x + 75f, rect.y, 25f, rect.height);
            Rect maxRect = new Rect(rect.x + 105f, rect.y, 35f, rect.height);
            
            Text.Anchor = TextAnchor.MiddleCenter;
            
            // 当前数量
            Widgets.Label(labelRect, currentCount.ToString());
            
            // 减少按钮
            if (Widgets.ButtonText(minusRect, "-") && currentCount > 0)
            {
                transferable.AdjustBy(-1);
            }
            
            // 增加按钮
            if (Widgets.ButtonText(plusRect, "+") && currentCount < availableCount)
            {
                transferable.AdjustBy(1);
            }
            
            // 最大按钮
            if (Widgets.ButtonText(maxRect, "WULA_Max".Translate()) && availableCount > 0)
            {
                Find.WindowStack.Add(new Dialog_Slider(
                    "WULA_SetLoadCount".Translate(transferable.AnyThing.LabelCap),
                    0, availableCount, 
                    value => transferable.AdjustTo(value),
                    currentCount
                ));
            }
            
            Text.Anchor = TextAnchor.UpperLeft;
        }
        
        private void DoButtons(Rect rect)
        {
            float buttonWidth = rect.width / 2f - 5f;
            
            // 加载所有按钮
            Rect loadAllRect = new Rect(rect.x, rect.y, buttonWidth, rect.height);
            if (Widgets.ButtonText(loadAllRect, "WULA_LoadAll".Translate()))
            {
                foreach (var transferable in transferables)
                {
                    transferable.AdjustTo(transferable.things.Sum(t => t.stackCount));
                }
            }
            
            // 清除所有按钮
            Rect clearAllRect = new Rect(rect.x + buttonWidth + 10f, rect.y, buttonWidth, rect.height);
            if (Widgets.ButtonText(clearAllRect, "WULA_ClearAll".Translate()))
            {
                foreach (var transferable in transferables)
                {
                    transferable.AdjustTo(0);
                }
            }
        }
        
        private void DoStatusInfo(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            Rect innerRect = rect.ContractedBy(5f);
            
            Text.Anchor = TextAnchor.MiddleLeft;
            
            // 计算总质量
            float totalMass = 0f;
            foreach (var transferable in transferables)
            {
                if (transferable.CountToTransfer > 0)
                {
                    float thingMass = transferable.AnyThing.GetStatValue(StatDefOf.Mass);
                    totalMass += thingMass * transferable.CountToTransfer;
                }
            }
            
            // 质量信息
            string massText = "WULA_Mass".Translate() + ": " + totalMass.ToString("F1") + " / " + submitterComp.Props.massCapacity.ToString("F1") + " kg";
            if (totalMass > submitterComp.Props.massCapacity)
            {
                massText = massText.Colorize(ColorLibrary.RedReadable);
            }
            
            Widgets.Label(innerRect, massText);
            Text.Anchor = TextAnchor.UpperLeft;
        }
        
        private string GetThingTooltip(Thing thing, int count)
        {
            float mass = thing.GetStatValue(StatDefOf.Mass);
            float value = thing.MarketValue * count;
            return $"{thing.LabelCap}\n{"WULA_Mass".Translate()}: {mass:F2} kg\n{"WULA_Value".Translate()}: {value}\n{"WULA_Description".Translate()}: {thing.def.description}";
        }
        
        private void RefreshTransferables()
        {
            transferables.Clear();
            
            // 获取地图上所有可搬运的物品
            foreach (Thing thing in submitterComp.parent.Map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableAlways))
            {
                if ((thing.IsInValidStorage() || thing.Spawned) && !thing.Position.Fogged(thing.Map))
                {
                    AddToTransferables(thing);
                }
            }
            
            // 按物品类型排序
            transferables.SortBy(t => t.AnyThing.def.label);
        }
        
        private void AddToTransferables(Thing thing)
        {
            if (thing.def.category == ThingCategory.Item)
            {
                TransferableOneWay transferable = TransferableUtility.TransferableMatching(thing, transferables, TransferAsOneMode.PodsOrCaravanPacking);
                if (transferable == null)
                {
                    transferable = new TransferableOneWay();
                    transferables.Add(transferable);
                    transferable.things.Add(thing);
                }
                else
                {
                    transferable.things.Add(thing);
                }
            }
        }
        
        public override void PostClose()
        {
            base.PostClose();
            
            // 应用装载设置到提交器
            if (submitterComp != null)
            {
                submitterComp.leftToLoad?.Clear();
                
                foreach (TransferableOneWay transferable in transferables)
                {
                    if (transferable.CountToTransfer > 0)
                    {
                        submitterComp.AddToTheToLoadList(transferable, transferable.CountToTransfer);
                    }
                }
                
                // 开始装载工作
                StartLoadingJobs();
            }
        }
        
        private void StartLoadingJobs()
        {
            if (submitterComp?.parent?.Map == null) return;
            
            foreach (TransferableOneWay transferable in transferables)
            {
                if (transferable.CountToTransfer > 0)
                {
                    foreach (Thing thing in transferable.things)
                    {
                        if (transferable.CountToTransfer <= 0) break;
                        
                        // 创建搬运到提交器的工作
                        Job job = JobMaker.MakeJob(JobDefOf.HaulToContainer, thing, submitterComp.parent);
                        job.count = Mathf.Min(thing.stackCount, transferable.CountToTransfer);
                        job.haulMode = HaulMode.ToContainer;
                        
                        // 寻找殖民者执行工作
                        Pawn pawn = FindBestPawnForJob(job);
                        if (pawn != null)
                        {
                            pawn.jobs.TryTakeOrderedJob(job);
                            transferable.AdjustBy(-job.count);
                        }
                    }
                }
            }
            
            Messages.Message("WULA_LoadingJobsCreated".Translate(), MessageTypeDefOf.PositiveEvent);
        }
        
        private Pawn FindBestPawnForJob(Job job)
        {
            return submitterComp.parent.Map.mapPawns.FreeColonistsSpawned
                .Where(p => p.workSettings.WorkIsActive(WorkTypeDefOf.Hauling))
                .FirstOrDefault(p => p.CanReserveAndReach(job.targetA, PathEndMode.ClosestTouch, Danger.Some));
        }
    }
}
