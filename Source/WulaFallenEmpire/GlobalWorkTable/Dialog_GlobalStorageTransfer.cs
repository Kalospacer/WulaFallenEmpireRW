using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace WulaFallenEmpire
{
    public class Dialog_GlobalStorageTransfer : Window
    {
        private const float RowHeight = 30f;
        private const float TitleHeight = 45f;
        private const float TopAreaHeight = 58f;

        private readonly Building_GlobalWorkTable table;
        private readonly GlobalStorageWorldComponent storage;

        private readonly QuickSearchWidget quickSearchWidget = new QuickSearchWidget();
        private Vector2 scrollPosition;
        private float viewHeight;

        private List<Tradeable> tradeables = new List<Tradeable>();

        public override Vector2 InitialSize => new Vector2(1024f, UI.screenHeight);

        public Dialog_GlobalStorageTransfer(Building_GlobalWorkTable table, Pawn negotiator)
        {
            this.table = table;
            storage = Find.World.GetComponent<GlobalStorageWorldComponent>();

            doCloseX = true;
            closeOnAccept = false;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
        }

        public override void PostOpen()
        {
            base.PostOpen();
            RebuildTradeables();
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (table == null || table.DestroyedOrNull() || table.Map == null || storage == null)
            {
                Close();
                return;
            }

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, TitleHeight), "WULA_GlobalStorageTransferTitle".Translate());
            Text.Font = GameFont.Small;

            Rect topRect = new Rect(0f, TitleHeight, inRect.width, TopAreaHeight);
            DrawTopArea(topRect);

            float bottomAreaHeight = 45f;
            Rect listRect = new Rect(0f, topRect.yMax + 6f, inRect.width, inRect.height - topRect.yMax - bottomAreaHeight - 8f);
            DrawTradeablesList(listRect);

            Rect bottomRect = new Rect(0f, inRect.height - bottomAreaHeight, inRect.width, bottomAreaHeight);
            DrawBottomButtons(bottomRect);
        }

        private void DrawTopArea(Rect rect)
        {
            Rect searchRect = new Rect(rect.xMax - 260f, rect.y + 2f, 260f, 24f);
            quickSearchWidget.OnGUI(searchRect, onFilterChange: () => { }, onClear: () => { });

            Rect hintRect = new Rect(rect.x, rect.y, rect.width - 270f, rect.height);
            Widgets.Label(hintRect,
                "WULA_GlobalStorageTransferHint".Translate());
        }

        private void DrawTradeablesList(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            Rect outRect = rect.ContractedBy(5f);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, viewHeight);

            float curY = 0f;
            int drawnIndex = 0;

            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
            try
            {
                for (int i = 0; i < tradeables.Count; i++)
                {
                    Tradeable trad = tradeables[i];
                    if (trad == null || trad.ThingDef == null) continue;

                    PruneTradeableThingLists(trad);
                    if (!trad.HasAnyThing) continue;

                    if (!quickSearchWidget.filter.Matches(trad.ThingDef))
                        continue;

                    Rect rowRect = new Rect(0f, curY, viewRect.width, RowHeight);
                    DrawStorageTransferRow(rowRect, trad, drawnIndex);
                    curY += RowHeight;
                    drawnIndex++;
                }

                if (Event.current.type == EventType.Layout)
                {
                    viewHeight = Mathf.Max(curY, outRect.height);
                }
            }
            finally
            {
                GenUI.ResetLabelAlign();
                Widgets.EndScrollView();
            }
        }

        private static void DrawStorageTransferRow(Rect rect, Tradeable trad, int index)
        {
            if (index % 2 == 1)
            {
                Widgets.DrawLightHighlight(rect);
            }

            Text.Font = GameFont.Small;
            Widgets.BeginGroup(rect);
            try
            {
                float width = rect.width;

                int globalCount = SafeCountHeldBy(trad, Transactor.Trader);
                if (globalCount != 0 && trad.IsThing)
                {
                    Rect countRect = new Rect(width - TradeUI.CountColumnWidth, 0f, TradeUI.CountColumnWidth, rect.height);
                    Widgets.DrawHighlightIfMouseover(countRect);
                    Text.Anchor = TextAnchor.MiddleRight;
                    Rect labelRect = countRect.ContractedBy(5f, 0f);
                    Widgets.Label(labelRect, globalCount.ToStringCached());
                    TooltipHandler.TipRegionByKey(countRect, "TraderCount");
                }

                width -= TradeUI.CountColumnWidth + TradeUI.PriceColumnWidth;

                Rect adjustRect = new Rect(width - TradeUI.AdjustColumnWidth, 0f, TradeUI.AdjustColumnWidth, rect.height);
                int min = -SafeCountHeldBy(trad, Transactor.Colony);
                int max = SafeCountHeldBy(trad, Transactor.Trader);
                TransferableUIUtility.DoCountAdjustInterface(adjustRect, trad, index, min, max, flash: false);
                width -= TradeUI.AdjustColumnWidth;

                int beaconCount = SafeCountHeldBy(trad, Transactor.Colony);
                if (beaconCount != 0)
                {
                    Rect countRect = new Rect(width - TradeUI.CountColumnWidth, 0f, TradeUI.CountColumnWidth, rect.height);
                    Widgets.DrawHighlightIfMouseover(countRect);
                    Text.Anchor = TextAnchor.MiddleLeft;
                    Rect labelRect = countRect.ContractedBy(5f, 0f);
                    Widgets.Label(labelRect, beaconCount.ToStringCached());
                    TooltipHandler.TipRegionByKey(countRect, "ColonyCount");
                }

                width -= TradeUI.CountColumnWidth + TradeUI.PriceColumnWidth;

                Rect infoRect = new Rect(0f, 0f, width, rect.height);
                TransferableUIUtility.DrawTransferableInfo(trad, infoRect, Color.white);
            }
            finally
            {
                GenUI.ResetLabelAlign();
                Widgets.EndGroup();
            }
        }

        private static int SafeCountHeldBy(Tradeable trad, Transactor transactor)
        {
            if (trad == null) return 0;

            List<Thing> list = (transactor == Transactor.Colony) ? trad.thingsColony : trad.thingsTrader;
            if (list == null || list.Count == 0) return 0;

            int count = 0;
            for (int i = 0; i < list.Count; i++)
            {
                Thing t = list[i];
                if (t == null || t.Destroyed) continue;
                count += t.stackCount;
            }

            return count;
        }

        private static void PruneTradeableThingLists(Tradeable trad)
        {
            if (trad == null) return;
            trad.thingsColony?.RemoveAll(t => t == null || t.Destroyed);
            trad.thingsTrader?.RemoveAll(t => t == null || t.Destroyed);
        }

        private void DrawBottomButtons(Rect rect)
        {
            float buttonWidth = 160f;
            Rect executeRect = new Rect(rect.xMax - buttonWidth, rect.y + 2f, buttonWidth, rect.height - 4f);
            Rect resetRect = new Rect(executeRect.x - buttonWidth - 10f, executeRect.y, buttonWidth, executeRect.height);

            if (Widgets.ButtonText(resetRect, "WULA_ResetTransfer".Translate()))
            {
                foreach (var t in tradeables)
                {
                    t?.ForceTo(0);
                }
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            }

            if (Widgets.ButtonText(executeRect, "WULA_ExecuteTransfer".Translate()))
            {
                ExecuteTransfers();
            }
        }

        private void ExecuteTransfers()
        {
            if (storage == null || table?.Map == null)
                return;

            bool changed = false;
            Map map = table.Map;
            IntVec3 dropSpot = DropCellFinder.TradeDropSpot(map);

            for (int i = 0; i < tradeables.Count; i++)
            {
                Tradeable trad = tradeables[i];
                if (trad == null) continue;
                if (trad.CountToTransfer == 0) continue;

                PruneTradeableThingLists(trad);
                if (!trad.HasAnyThing) continue;

                int storeCount = trad.CountToTransferToDestination; // 信标 -> 全局（CountToTransfer<0）
                int takeCount = trad.CountToTransferToSource;       // 全局 -> 信标（CountToTransfer>0）

                if (storeCount > 0)
                {
                    changed |= TransferToGlobalStorage(trad, storeCount);
                }
                else if (takeCount > 0)
                {
                    changed |= TransferFromGlobalStorage(trad, takeCount, map, dropSpot);
                }

                trad.ForceTo(0);
            }

            if (!changed)
            {
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                return;
            }

            SoundDefOf.ExecuteTrade.PlayOneShotOnCamera();
            RebuildTradeables();
        }

        private bool TransferToGlobalStorage(Tradeable trad, int count)
        {
            if (trad == null || count <= 0 || storage == null) return false;

            bool changed = false;
            TransferableUtility.TransferNoSplit(trad.thingsColony, count, (Thing thing, int countToTransfer) =>
            {
                if (thing == null || thing.Destroyed || countToTransfer <= 0) return;

                Thing split = thing.SplitOff(countToTransfer);
                if (split == null) return;

                if (ShouldGoToOutputStorage(split))
                {
                    storage.AddToOutputStorage(split);
                }
                else
                {
                    storage.AddToInputStorage(split);
                }

                changed = true;
            });

            return changed;
        }

        private bool TransferFromGlobalStorage(Tradeable trad, int count, Map map, IntVec3 dropSpot)
        {
            if (trad == null || count <= 0 || storage == null || map == null) return false;

            bool changed = false;
            TransferableUtility.TransferNoSplit(trad.thingsTrader, count, (Thing thing, int countToTransfer) =>
            {
                if (thing == null || thing.Destroyed || countToTransfer <= 0) return;

                Thing split = thing.SplitOff(countToTransfer);
                if (split == null) return;

                if (split.holdingOwner != null)
                {
                    split.holdingOwner.Remove(split);
                }
                if (split.Spawned)
                {
                    split.DeSpawn();
                }

                TradeUtility.SpawnDropPod(dropSpot, map, split);
                changed = true;
            });

            return changed;
        }

        private static bool ShouldGoToOutputStorage(Thing thing)
        {
            ThingDef def = thing?.def;
            if (def == null) return false;
            if (def.IsWeapon) return true;
            if (def.IsApparel) return true;
            return false;
        }

        private void RebuildTradeables()
        {
            tradeables.Clear();

            void AddThingToTradeables(Thing thing, Transactor transactor)
            {
                if (thing == null) return;

                Tradeable trad = TransferableUtility.TradeableMatching(thing, tradeables);
                if (trad == null)
                {
                    trad = thing is Pawn ? new Tradeable_StorageTransferPawn() : new Tradeable_StorageTransfer();
                    tradeables.Add(trad);
                }
                trad.AddThing(thing, transactor);
            }

            foreach (Thing t in GetThingsInPoweredTradeBeaconRange(table.Map))
            {
                if (t?.def == null) continue;
                if (t is Corpse) continue;
                if (t.def.category != ThingCategory.Item) continue;
                AddThingToTradeables(t, Transactor.Colony);
            }

            if (storage?.inputContainer != null)
            {
                foreach (Thing t in storage.inputContainer)
                {
                    if (t == null) continue;
                    AddThingToTradeables(t, Transactor.Trader);
                }
            }

            if (storage?.outputContainer != null)
            {
                foreach (Thing t in storage.outputContainer)
                {
                    if (t == null) continue;
                    AddThingToTradeables(t, Transactor.Trader);
                }
            }

            tradeables = tradeables
                .Where(t => t != null && t.HasAnyThing)
                .OrderBy(t => t.ThingDef?.label ?? "")
                .ToList();
        }

        private static IEnumerable<Thing> GetThingsInPoweredTradeBeaconRange(Map map)
        {
            if (map == null) yield break;

            HashSet<Thing> yielded = new HashSet<Thing>();
            foreach (var beacon in Building_OrbitalTradeBeacon.AllPowered(map))
            {
                foreach (var cell in beacon.TradeableCells)
                {
                    List<Thing> things = cell.GetThingList(map);
                    for (int i = 0; i < things.Count; i++)
                    {
                        Thing t = things[i];
                        if (t == null) continue;
                        if (!yielded.Add(t)) continue;
                        yield return t;
                    }
                }
            }
        }

        private class Tradeable_StorageTransfer : Tradeable
        {
            public override bool TraderWillTrade => true;
            public override bool IsCurrency => false;
            public override bool Interactive => true;
            public override TransferablePositiveCountDirection PositiveCountDirection => TransferablePositiveCountDirection.Source;
        }

        private class Tradeable_StorageTransferPawn : Tradeable_Pawn
        {
            public override bool TraderWillTrade => true;
            public override bool IsCurrency => false;
            public override bool Interactive => true;
            public override TransferablePositiveCountDirection PositiveCountDirection => TransferablePositiveCountDirection.Source;
        }
    }
}
