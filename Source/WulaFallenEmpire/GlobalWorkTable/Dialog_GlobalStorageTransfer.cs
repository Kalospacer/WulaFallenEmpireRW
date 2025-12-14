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

        private List<Tradeable_StorageTransfer> tradeables = new List<Tradeable_StorageTransfer>();

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
                Tradeable_StorageTransfer trad = tradeables[i];
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

            var byDef = new Dictionary<ThingDef, Tradeable_StorageTransfer>();

            foreach (Thing t in GetThingsInPoweredTradeBeaconRange(table.Map))
            {
                if (t?.def == null) continue;
                if (t.def.category != ThingCategory.Item) continue;

                if (!byDef.TryGetValue(t.def, out var trad))
                {
                    trad = new Tradeable_StorageTransfer();
                    byDef[t.def] = trad;
                }
                trad.AddThing(t, Transactor.Colony);
            }

            foreach (var kvp in GetGlobalStorageCounts(storage))
            {
                ThingDef def = kvp.Key;
                int count = kvp.Value;
                if (def == null || count <= 0) continue;

                if (!byDef.TryGetValue(def, out var trad))
                {
                    trad = new Tradeable_StorageTransfer();
                    byDef[def] = trad;
                }

                foreach (Thing dummy in MakeDummyStacks(def, count))
                {
                    trad.AddThing(dummy, Transactor.Trader);
                }
            }

            tradeables = byDef.Values
                .Where(t => t != null && t.ThingDef != null)
                .OrderBy(t => t.ThingDef.label)
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

        private static Dictionary<ThingDef, int> GetGlobalStorageCounts(GlobalStorageWorldComponent storage)
        {
            var counts = new Dictionary<ThingDef, int>();
            if (storage == null) return counts;

            foreach (var kvp in storage.outputStorage)
            {
                if (kvp.Key == null || kvp.Value <= 0) continue;
                counts[kvp.Key] = counts.TryGetValue(kvp.Key, out var v) ? v + kvp.Value : kvp.Value;
            }

            foreach (var kvp in storage.inputStorage)
            {
                if (kvp.Key == null || kvp.Value <= 0) continue;
                counts[kvp.Key] = counts.TryGetValue(kvp.Key, out var v) ? v + kvp.Value : kvp.Value;
            }

            return counts;
        }

        private static IEnumerable<Thing> MakeDummyStacks(ThingDef def, int count)
        {
            int remaining = count;
            int stackLimit = Mathf.Max(1, def.stackLimit);

            while (remaining > 0)
            {
                int stackCount = Mathf.Min(remaining, stackLimit);
                Thing thing = MakeThingForDef(def, stackCount);
                if (thing == null) yield break;

                yield return thing;
                remaining -= stackCount;
            }
        }

        private static Thing MakeThingForDef(ThingDef def, int stackCount)
        {
            if (def == null) return null;

            Thing thing;
            if (def.MadeFromStuff)
            {
                ThingDef stuff = GenStuff.DefaultStuffFor(def) ?? GenStuff.AllowedStuffsFor(def).FirstOrDefault();
                if (stuff == null) return null;
                thing = ThingMaker.MakeThing(def, stuff);
            }
            else
            {
                thing = ThingMaker.MakeThing(def);
            }

            thing.stackCount = stackCount;
            return thing;
        }

        private class Tradeable_StorageTransfer : Tradeable
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
                    storage.AddToOutputStorage(thing.def, thing.stackCount);
                }
                else
                {
                    storage.AddToInputStorage(thing.def, thing.stackCount);
                }

                thing.Destroy(DestroyMode.Vanish);
            }

            public void GiveSoldThingToPlayer(Thing toGive, int countToGive, Pawn playerNegotiator)
            {
                if (storage == null) return;
                if (map == null) return;
                if (toGive == null || countToGive <= 0) return;

                if (!TryRemoveFromAnyStorage(toGive.def, countToGive))
                {
                    Log.Warning($"[WULA] Global storage changed while transfer dialog open; could not remove {countToGive}x {toGive.def?.defName}.");
                    return;
                }

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

            private bool TryRemoveFromAnyStorage(ThingDef def, int count)
            {
                if (def == null || count <= 0) return false;

                int available = storage.GetInputStorageCount(def) + storage.GetOutputStorageCount(def);
                if (available < count) return false;

                int remaining = count;
                int fromOutput = Mathf.Min(remaining, storage.GetOutputStorageCount(def));
                if (fromOutput > 0)
                {
                    if (!storage.RemoveFromOutputStorage(def, fromOutput)) return false;
                    remaining -= fromOutput;
                }

                if (remaining > 0)
                {
                    if (!storage.RemoveFromInputStorage(def, remaining))
                    {
                        if (fromOutput > 0) storage.AddToOutputStorage(def, fromOutput);
                        return false;
                    }
                }

                return true;
            }
        }
    }
}
