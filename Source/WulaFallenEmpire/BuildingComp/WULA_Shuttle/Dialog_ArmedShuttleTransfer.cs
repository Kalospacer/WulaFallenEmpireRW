using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace WulaFallenEmpire
{
    public class Dialog_LoadPocketMap : Window
    {
        private enum Tab
        {
            Pawns,
            Items
        }

        private static readonly List<TabRecord> Tabs = new List<TabRecord>();
        private readonly Vector2 bottomButtonSize = new Vector2(160f, 40f);
        private readonly CompPocketMapPortal portal;
        private List<TransferableOneWay> transferables;
        private TransferableOneWayWidget pawnsTransfer;
        private TransferableOneWayWidget itemsTransfer;
        private Tab tab;

        public override Vector2 InitialSize => new Vector2(1024f, UI.screenHeight);
        protected override float Margin => 0f;

        public Dialog_LoadPocketMap(CompPocketMapPortal portal)
        {
            this.portal = portal;
            forcePause = true;
            absorbInputAroundWindow = true;
        }

        public override void PostOpen()
        {
            base.PostOpen();
            RecacheTransferables();
        }

        public override void DoWindowContents(Rect inRect)
        {
            using (new TextBlock(GameFont.Medium, TextAnchor.MiddleCenter))
            {
                Widgets.Label(new Rect(0f, 0f, inRect.width, 35f), "WULA.PocketSpace.Enter".Translate());
            }

            Tabs.Clear();
            Tabs.Add(new TabRecord("PawnsTab".Translate(), () => tab = Tab.Pawns, tab == Tab.Pawns));
            Tabs.Add(new TabRecord("ItemsTab".Translate(), () => tab = Tab.Items, tab == Tab.Items));
            inRect.yMin += 67f;
            Widgets.DrawMenuSection(inRect);
            TabDrawer.DrawTabs(inRect, Tabs);
            inRect = inRect.ContractedBy(17f);
            Widgets.BeginGroup(inRect);
            Rect rect = inRect.AtZero();
            DrawBottomButtons(rect);
            rect.yMax -= 76f;
            if (tab == Tab.Pawns)
            {
                pawnsTransfer.OnGUI(rect, out _);
            }
            else
            {
                itemsTransfer.OnGUI(rect, out _);
            }
            Widgets.EndGroup();
        }

        private void DrawBottomButtons(Rect rect)
        {
            float y = rect.height - 72f;
            if (Widgets.ButtonText(new Rect(rect.width / 2f - bottomButtonSize.x / 2f, y, bottomButtonSize.x, bottomButtonSize.y), "ResetButton".Translate()))
            {
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                RecacheTransferables();
            }
            if (Widgets.ButtonText(new Rect(0f, y, bottomButtonSize.x, bottomButtonSize.y), "CancelButton".Translate()))
            {
                Close();
            }
            if (Widgets.ButtonText(new Rect(rect.width - bottomButtonSize.x, y, bottomButtonSize.x, bottomButtonSize.y), "AcceptButton".Translate()) && TryAccept())
            {
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
                Close(doCloseSound: false);
            }
        }

        private bool TryAccept()
        {
            List<Pawn> pawns = TransferableUtility.GetPawnsFromTransferables(transferables);
            if (pawns.Count == 0 && transferables.All(x => x.CountToTransfer <= 0))
            {
                Messages.Message("WULA.PocketSpace.NoPawnsOrItemsSelected".Translate(), MessageTypeDefOf.RejectInput);
                return false;
            }

            portal.SetLoadList(transferables);
            PocketMapPortalUtility.MakeLord(pawns, portal);
            return true;
        }

        private void RecacheTransferables()
        {
            transferables = new List<TransferableOneWay>();
            if (portal.LoadInProgress)
            {
                transferables.AddRange(portal.LeftToLoad);
            }

            foreach (Pawn pawn in CaravanFormingUtility.AllSendablePawns(
                portal.Shuttle.Map, true, false, false, false, true))
            {
                AddToTransferables(pawn);
            }

            bool isPocketMap = portal.Shuttle.Map.IsPocketMap;
            foreach (Thing thing in CaravanFormingUtility.AllReachableColonyItems(portal.Shuttle.Map, isPocketMap, isPocketMap))
            {
                AddToTransferables(thing);
            }

            pawnsTransfer = new TransferableOneWayWidget(null, null, null,
                "TransferMapPortalColonyThingCountTip".Translate(), true,
                IgnorePawnsInventoryMode.IgnoreIfAssignedToUnload, true, () => float.MaxValue,
                0f, false, portal.Shuttle.Map.Tile, false, true);
            CaravanUIUtility.AddPawnsSections(pawnsTransfer, transferables);
            itemsTransfer = new TransferableOneWayWidget(
                transferables.Where(x => x.ThingDef.category != ThingCategory.Pawn), null, null,
                "TransferMapPortalColonyThingCountTip".Translate(), true,
                IgnorePawnsInventoryMode.IgnoreIfAssignedToUnload, true, () => float.MaxValue,
                0f, false, portal.Shuttle.Map.Tile);
        }

        private void AddToTransferables(Thing thing)
        {
            if (transferables.Any(x => x.things.Contains(thing)))
            {
                return;
            }

            TransferableOneWay transferable = TransferableUtility.TransferableMatching(
                thing, transferables, TransferAsOneMode.PodsOrCaravanPacking);
            if (transferable == null)
            {
                transferable = new TransferableOneWay();
                transferables.Add(transferable);
            }
            transferable.things.Add(thing);
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
