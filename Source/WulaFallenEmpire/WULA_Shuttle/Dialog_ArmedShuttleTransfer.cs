using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace WulaFallenEmpire
{
    public class Dialog_ArmedShuttleTransfer : Window
    {
        private enum Tab
        {
            Pawns,
            Items
        }

        private const float TitleRectHeight = 35f;
        private const float BottomAreaHeight = 55f;
        private readonly Vector2 BottomButtonSize = new Vector2(160f, 40f);

        private Building_ArmedShuttleWithPocket shuttle;
        private List<TransferableOneWay> transferables;
        private TransferableOneWayWidget pawnsTransfer;
        private TransferableOneWayWidget itemsTransfer;
        private Tab tab;

        private static List<TabRecord> tabsList = new List<TabRecord>();

        public override Vector2 InitialSize => new Vector2(1024f, UI.screenHeight);
        protected override float Margin => 0f;

        public Dialog_ArmedShuttleTransfer(Building_ArmedShuttleWithPocket shuttle)
        {
            this.shuttle = shuttle;
            forcePause = true;
            absorbInputAroundWindow = true;
        }

        public override void PostOpen()
        {
            base.PostOpen();
            CalculateAndRecacheTransferables();
        }

        public override void DoWindowContents(Rect inRect)
        {
            Rect rect = new Rect(0f, 0f, inRect.width, TitleRectHeight);
            using (new TextBlock(GameFont.Medium, TextAnchor.MiddleCenter))
            {
                Widgets.Label(rect, shuttle.EnterString);
            }
            
            tabsList.Clear();
            tabsList.Add(new TabRecord("PawnsTab".Translate(), delegate
            {
                tab = Tab.Pawns;
            }, tab == Tab.Pawns));
            tabsList.Add(new TabRecord("ItemsTab".Translate(), delegate
            {
                tab = Tab.Items;
            }, tab == Tab.Items));
            
            inRect.yMin += 67f;
            Widgets.DrawMenuSection(inRect);
            TabDrawer.DrawTabs(inRect, tabsList);
            inRect = inRect.ContractedBy(17f);
            
            Widgets.BeginGroup(inRect);
            Rect rect2 = inRect.AtZero();
            DoBottomButtons(rect2);
            Rect inRect2 = rect2;
            inRect2.yMax -= 76f;
            
            bool anythingChanged = false;
            switch (tab)
            {
                case Tab.Pawns:
                    pawnsTransfer.OnGUI(inRect2, out anythingChanged);
                    break;
                case Tab.Items:
                    itemsTransfer.OnGUI(inRect2, out anythingChanged);
                    break;
            }
            Widgets.EndGroup();
        }

        private void DoBottomButtons(Rect rect)
        {
            float buttonY = rect.height - BottomAreaHeight - 17f;

            if (Widgets.ButtonText(new Rect(rect.width / 2f - BottomButtonSize.x / 2f, buttonY, BottomButtonSize.x, BottomButtonSize.y), "ResetButton".Translate()))
            {
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                CalculateAndRecacheTransferables();
            }
            if (Widgets.ButtonText(new Rect(0f, buttonY, BottomButtonSize.x, BottomButtonSize.y), "CancelButton".Translate()))
            {
                Close();
            }
            if (Widgets.ButtonText(new Rect(rect.width - BottomButtonSize.x, buttonY, BottomButtonSize.x, BottomButtonSize.y), "AcceptButton".Translate()) && TryAccept())
            {
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
                Close(doCloseSound: false);
            }
        }

        private bool TryAccept()
        {
            // 获取选中的Pawn和物品
            List<Pawn> pawnsToTransfer = TransferableUtility.GetPawnsFromTransferables(transferables);
            List<Thing> itemsToTransfer = new List<Thing>();
            foreach (TransferableOneWay transferable in transferables)
            {
                if (transferable.ThingDef.category != ThingCategory.Pawn)
                {
                    itemsToTransfer.AddRange(transferable.things.Take(transferable.CountToTransfer));
                }
            }

            // 传送Pawn到口袋空间
            int transferredPawnCount = 0;
            foreach (Pawn pawn in pawnsToTransfer)
            {
                if (shuttle.TransferPawnToPocketSpace(pawn))
                {
                    transferredPawnCount++;
                }
            }

            // 将物品添加到穿梭机的主容器 (CompTransporter.innerContainer)
            CompTransporter transporter = shuttle.GetComp<CompTransporter>();
            if (transporter == null)
            {
                Log.Error("[WULA-ERROR] Dialog_ArmedShuttleTransfer: CompTransporter is missing on shuttle!");
                return false;
            }

            int transferredItemCount = 0;
            foreach (Thing item in itemsToTransfer)
            {
                // 从当前地图移除物品
                item.DeSpawn();
                
                // 尝试添加到穿梭机主容器
                if (transporter.innerContainer.TryAdd(item))
                {
                    transferredItemCount++;
                }
                else
                {
                    // 如果容器已满，尝试丢弃在穿梭机附近
                    IntVec3 dropPos = CellFinder.RandomClosewalkCellNear(shuttle.Position, shuttle.Map, 3);
                    if (dropPos.IsValid)
                    {
                        GenPlace.TryPlaceThing(item, dropPos, shuttle.Map, ThingPlaceMode.Near);
                        Messages.Message("容器已满：{0} 被放置在穿梭机附近".Translate(item.LabelShort), shuttle, MessageTypeDefOf.CautionInput);
                    }
                    else
                    {
                        Log.Error($"[WULA-ERROR] Could not find valid drop position for item {item.LabelShort}");
                        // 实在没地方放，就让它消失吧，或者抛出异常
                        item.Destroy(); 
                    }
                }
            }
            
            if (transferredPawnCount > 0 || transferredItemCount > 0)
            {
                Messages.Message("WULA.PocketSpace.TransferSuccess".Translate(transferredPawnCount + transferredItemCount), MessageTypeDefOf.PositiveEvent);
                // 切换到口袋地图视角（如果传送了Pawn）
                if (transferredPawnCount > 0)
                {
                    Current.Game.CurrentMap = shuttle.PocketMap;
                    Find.CameraDriver.JumpToCurrentMapLoc(shuttle.PocketMap.Center);
                }
            }
            else
            {
                Messages.Message("WULA.PocketSpace.NoPawnsOrItemsSelected".Translate(), MessageTypeDefOf.RejectInput);
                return false;
            }

            return true;
        }

        private void CalculateAndRecacheTransferables()
        {
            transferables = new List<TransferableOneWay>();
            // 根据需要添加现有物品到transferables（如果穿梭机已有物品）
            // 目前，我们从头开始构建列表，只添加地图上的物品和Pawn

            AddPawnsToTransferables();
            AddItemsToTransferables();
            
            // 重新创建TransferableOneWayWidget实例
            pawnsTransfer = new TransferableOneWayWidget(null, null, null, "TransferMapPortalColonyThingCountTip".Translate(),
                drawMass: true,
                ignorePawnInventoryMass: IgnorePawnsInventoryMode.IgnoreIfAssignedToUnload,
                includePawnsMassInMassUsage: true,
                availableMassGetter: () => float.MaxValue,
                extraHeaderSpace: 0f,
                ignoreSpawnedCorpseGearAndInventoryMass: false,
                tile: shuttle.Map.Tile,
                drawMarketValue: false,
                drawEquippedWeapon: true);
            CaravanUIUtility.AddPawnsSections(pawnsTransfer, transferables);

            itemsTransfer = new TransferableOneWayWidget(transferables.Where(x => x.ThingDef.category != ThingCategory.Pawn), null, null, "TransferMapPortalColonyThingCountTip".Translate(),
                drawMass: true,
                ignorePawnInventoryMass: IgnorePawnsInventoryMode.IgnoreIfAssignedToUnload,
                includePawnsMassInMassUsage: true,
                availableMassGetter: () => float.MaxValue,
                extraHeaderSpace: 0f,
                ignoreSpawnedCorpseGearAndInventoryMass: false,
                tile: shuttle.Map.Tile);
        }

        private void AddToTransferables(Thing t)
        {
            TransferableOneWay transferableOneWay = TransferableUtility.TransferableMatching(t, transferables, TransferAsOneMode.PodsOrCaravanPacking);
            if (transferableOneWay == null)
            {
                transferableOneWay = new TransferableOneWay();
                transferables.Add(transferableOneWay);
            }
            if (transferableOneWay.things.Contains(t))
            {
                Log.Error("Tried to add the same thing twice to TransferableOneWay: " + t);
            }
            else
            {
                transferableOneWay.things.Add(t);
            }
        }

        private void AddPawnsToTransferables()
        {
            foreach (Pawn item in CaravanFormingUtility.AllSendablePawns(shuttle.Map, allowEvenIfDowned: true, allowEvenIfInMentalState: false, allowEvenIfPrisonerNotSecure: false, allowCapturableDownedPawns: false, allowLodgers: true))
            {
                AddToTransferables(item);
            }
        }

        private void AddItemsToTransferables()
        {
            // 考虑是否需要处理口袋地图中的物品
            bool isPocketMap = shuttle.Map.IsPocketMap;
            foreach (Thing item in CaravanFormingUtility.AllReachableColonyItems(shuttle.Map, isPocketMap, isPocketMap))
            {
                AddToTransferables(item);
            }
        }

        public override void OnAcceptKeyPressed()
        {
            if (TryAccept())
            {
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
                Close(doCloseSound: false);
            }
        }
    }
}