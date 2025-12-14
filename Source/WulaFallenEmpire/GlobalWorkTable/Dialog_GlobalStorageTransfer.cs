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
        private readonly Pawn negotiator;
        private readonly GlobalStorageWorldComponent storage;
        private readonly GlobalStorageTransferTrader trader;

        private readonly QuickSearchWidget quickSearchWidget = new QuickSearchWidget();
        private Vector2 scrollPosition;
        private float viewHeight;

        private List<Tradeable> tradeables = new List<Tradeable>();

        private ITrader prevTrader;
        private Pawn prevNegotiator;
        private TradeDeal prevDeal;
        private bool prevGiftMode;

        public override Vector2 InitialSize => new Vector2(1024f, UI.screenHeight);

        public Dialog_GlobalStorageTransfer(Building_GlobalWorkTable table, Pawn negotiator)
        {
            this.table = table;
            this.negotiator = negotiator;
            storage = Find.World.GetComponent<GlobalStorageWorldComponent>();
            trader = new GlobalStorageTransferTrader(table?.Map, storage);

            doCloseX = true;
            closeOnAccept = false;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
        }

        public override void PostOpen()
        {
            base.PostOpen();

            prevTrader = TradeSession.trader;
            prevNegotiator = TradeSession.playerNegotiator;
            prevDeal = TradeSession.deal;
            prevGiftMode = TradeSession.giftMode;

            TradeSession.trader = trader;
            TradeSession.playerNegotiator = negotiator;
            TradeSession.deal = null;
            TradeSession.giftMode = false;

            RebuildTradeables();
        }

        public override void PostClose()
        {
            base.PostClose();

            TradeSession.trader = prevTrader;
            TradeSession.playerNegotiator = prevNegotiator;
            TradeSession.deal = prevDeal;
            TradeSession.giftMode = prevGiftMode;
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

            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
            float curY = 0f;
            int drawnIndex = 0;

            for (int i = 0; i < tradeables.Count; i++)
            {
                Tradeable trad = tradeables[i];
                if (trad == null || trad.ThingDef == null) continue;

                if (!quickSearchWidget.filter.Matches(trad.ThingDef))
                    continue;

                Rect rowRect = new Rect(0f, curY, viewRect.width, RowHeight);
                TradeUI.DrawTradeableRow(rowRect, trad, drawnIndex);
                curY += RowHeight;
                drawnIndex++;
            }

            if (Event.current.type == EventType.Layout)
            {
                viewHeight = Mathf.Max(curY, outRect.height);
            }

            Widgets.EndScrollView();
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
            bool changed = false;

            foreach (var trad in tradeables)
            {
                if (trad == null) continue;
                if (trad.CountToTransfer == 0) continue;

                changed = true;
                trad.ResolveTrade();
                trad.ForceTo(0);
            }

            if (changed)
            {
                SoundDefOf.ExecuteTrade.PlayOneShotOnCamera();
                RebuildTradeables();
            }
            else
            {
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            }
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
        }

        private class Tradeable_StorageTransferPawn : Tradeable_Pawn
        {
            public override bool TraderWillTrade => true;
            public override bool IsCurrency => false;
        }

        private class GlobalStorageTransferTrader : ITrader
        {
            private readonly Map map;
            private readonly GlobalStorageWorldComponent storage;
            private readonly TraderKindDef traderKind;

            public GlobalStorageTransferTrader(Map map, GlobalStorageWorldComponent storage)
            {
                this.map = map;
                this.storage = storage;

                traderKind =
                    DefDatabase<TraderKindDef>.GetNamedSilentFail("Orbital_ExoticGoods") ??
                    DefDatabase<TraderKindDef>.GetNamedSilentFail("Orbital_BulkGoods") ??
                    DefDatabase<TraderKindDef>.AllDefs.FirstOrDefault();
            }

            public TraderKindDef TraderKind => traderKind;
            public IEnumerable<Thing> Goods => Enumerable.Empty<Thing>();
            public int RandomPriceFactorSeed => 0;
            public string TraderName => "WULA_GlobalStorageTransferTitle".Translate();
            public bool CanTradeNow => true;
            public float TradePriceImprovementOffsetForPlayer => 0f;
            public Faction Faction => Faction.OfPlayer;
            public TradeCurrency TradeCurrency => TradeCurrency.Silver;

            public IEnumerable<Thing> ColonyThingsWillingToBuy(Pawn playerNegotiator) => Enumerable.Empty<Thing>();

            public void GiveSoldThingToTrader(Thing toGive, int countToGive, Pawn playerNegotiator)
            {
                if (storage == null) return;
                if (toGive == null || countToGive <= 0) return;

                Thing thing = toGive.SplitOff(countToGive);
                thing.PreTraded(TradeAction.PlayerSells, playerNegotiator, this);

                if (ShouldGoToOutputStorage(thing))
                {
                    storage.AddToOutputStorage(thing);
                }
                else
                {
                    storage.AddToInputStorage(thing);
                }
            }

            public void GiveSoldThingToPlayer(Thing toGive, int countToGive, Pawn playerNegotiator)
            {
                if (storage == null) return;
                if (map == null) return;
                if (toGive == null || countToGive <= 0) return;

                Thing thing = toGive.SplitOff(countToGive);
                thing.PreTraded(TradeAction.PlayerBuys, playerNegotiator, this);

                IntVec3 dropSpot = DropCellFinder.TradeDropSpot(map);
                TradeUtility.SpawnDropPod(dropSpot, map, thing);
            }

            private static bool ShouldGoToOutputStorage(Thing thing)
            {
                ThingDef def = thing?.def;
                if (def == null) return false;
                if (def.IsWeapon) return true;
                if (def.IsApparel) return true;
                return false;
            }
        }
    }
}
