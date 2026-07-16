using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace WulaFallenEmpire
{
    public class CompProperties_PocketMapPortal : CompProperties
    {
        public MapGeneratorDef pocketMapGenerator;
        public ThingDef exitDef;
        public IntVec2 pocketMapSize = new IntVec2(25, 25);

        public CompProperties_PocketMapPortal()
        {
            compClass = typeof(CompPocketMapPortal);
        }
    }

    public class CompPocketMapPortal : ThingComp
    {
        private static readonly Texture2D EnterTex = ContentFinder<Texture2D>.Get("Wula/UI/Commands/WULA_Enter_ArmedShuttle_Pocket");
        private static readonly Texture2D ViewTex = ContentFinder<Texture2D>.Get("Wula/UI/Commands/WULA_View_ArmedShuttle_Pocket");
        private static readonly Texture2D CancelTex = ContentFinder<Texture2D>.Get("UI/Designators/Cancel");

        private Map pocketMap;
        private Building_PocketMapExit exit;
        private bool beenEntered;
        private List<TransferableOneWay> leftToLoad;
        private PocketMapContainerProxy containerProxy;

        public CompProperties_PocketMapPortal Props => (CompProperties_PocketMapPortal)props;
        public Building_ArmedShuttleWithPocket Shuttle => (Building_ArmedShuttleWithPocket)parent;
        public Map PocketMap => pocketMap?.Parent?.HasMap == true ? pocketMap : null;
        public Building_PocketMapExit Exit => exit;
        public List<TransferableOneWay> LeftToLoad => leftToLoad;
        public bool LoadInProgress => leftToLoad != null && leftToLoad.Any(x => x.CountToTransfer > 0);
        public ThingOwner ContainerProxy => containerProxy ?? (containerProxy = new PocketMapContainerProxy(this));

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_References.Look(ref pocketMap, "pocketMap");
            Scribe_References.Look(ref exit, "pocketMapExit");
            Scribe_Values.Look(ref beenEntered, "pocketMapBeenEntered");
            Scribe_Collections.Look(ref leftToLoad, "pocketMapLeftToLoad", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                leftToLoad?.RemoveAll(x => x.AnyThing == null || x.CountToTransfer <= 0);
                BindExit();
            }
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            BindExit();
        }

        public Map GetOrCreatePocketMap()
        {
            if (PocketMap != null)
            {
                return pocketMap;
            }

            pocketMap = PocketMapUtility.GeneratePocketMap(
                new IntVec3(Props.pocketMapSize.x, 1, Props.pocketMapSize.z),
                Props.pocketMapGenerator,
                Enumerable.Empty<GenStepWithParams>(),
                Shuttle.Map);
            BindExit(createIfMissing: true);
            return pocketMap;
        }

        private void BindExit(bool createIfMissing = false)
        {
            if (PocketMap == null)
            {
                exit = null;
                return;
            }

            if (exit == null || exit.Destroyed || exit.Map != pocketMap)
            {
                exit = pocketMap.listerThings.AllThings.OfType<Building_PocketMapExit>().FirstOrDefault();
            }

            if (exit == null && createIfMissing)
            {
                Thing thing = ThingMaker.MakeThing(Props.exitDef);
                IntVec3 cell = CellFinder.StandableCellNear(pocketMap.Center, pocketMap, 5f);
                GenSpawn.Spawn(thing, cell, pocketMap);
                exit = thing as Building_PocketMapExit;
            }

            if (exit != null)
            {
                exit.parentShuttle = Shuttle;
            }
        }

        public bool IsEnterable(out string reason)
        {
            if (!Shuttle.Spawned)
            {
                reason = "WULA.PocketSpace.ShuttleNotDocked".Translate();
                return false;
            }

            reason = null;
            return true;
        }

        public IntVec3 DestinationCell
        {
            get
            {
                GetOrCreatePocketMap();
                return exit?.Position ?? IntVec3.Invalid;
            }
        }

        public void OpenLoadDialog()
        {
            if (!IsEnterable(out string reason))
            {
                Messages.Message(reason, Shuttle, MessageTypeDefOf.RejectInput);
                return;
            }

            GetOrCreatePocketMap();
            Find.WindowStack.Add(new Dialog_LoadPocketMap(this));
        }

        public void ViewPocketMap()
        {
            Map map = GetOrCreatePocketMap();
            Current.Game.CurrentMap = map;
            if (exit != null)
            {
                CameraJumper.TryJumpAndSelect(exit);
            }
        }

        public FloatMenuOption GetEnterFloatMenuOption(IEnumerable<Pawn> pawns)
        {
            List<Pawn> validPawns = pawns.Where(p => p != null && p.Spawned && p.Map == Shuttle.Map).ToList();
            if (!IsEnterable(out string reason))
            {
                return new FloatMenuOption("WULA.PocketSpace.Enter".Translate() + ": " + reason, null);
            }

            validPawns.RemoveAll(p => !p.CanReach(Shuttle.InteractionCell, PathEndMode.OnCell, Danger.Deadly)
                || !p.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation));
            if (validPawns.Count == 0)
            {
                return new FloatMenuOption("WULA.PocketSpace.Enter".Translate() + ": " + "NoPath".Translate(), null);
            }

            return new FloatMenuOption("WULA.PocketSpace.Enter".Translate(), delegate
            {
                GetOrCreatePocketMap();
                foreach (Pawn pawn in validPawns)
                {
                    Job job = JobMaker.MakeJob(Wula_JobDefOf.WULA_EnterShuttlePocketMap, Shuttle);
                    job.playerForced = true;
                    pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                }
            }, MenuOptionPriority.High);
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            yield return new Command_Action
            {
                icon = EnterTex,
                defaultLabel = "WULA.PocketSpace.Enter".Translate() + "...",
                defaultDesc = "WULA.PocketSpace.EnterDesc".Translate(),
                action = OpenLoadDialog,
                Disabled = !IsEnterable(out string reason),
                disabledReason = reason
            };

            if (Shuttle.Spawned || PocketMap != null)
            {
                Command_Action view = new Command_Action
                {
                    icon = ViewTex,
                    defaultLabel = "WULA.PocketSpace.ViewMap".Translate(),
                    defaultDesc = "WULA.PocketSpace.ViewMapDesc".Translate(),
                    action = ViewPocketMap
                };
                if (!Shuttle.Spawned && PocketMap == null)
                {
                    view.Disable("WULA.PocketSpace.ShuttleNotDocked".Translate());
                }
                yield return view;
            }

            if (LoadInProgress)
            {
                yield return new Command_Action
                {
                    icon = CancelTex,
                    defaultLabel = "CommandCancelEnterPortal".Translate(),
                    defaultDesc = "CommandCancelEnterPortalDesc".Translate(),
                    action = CancelLoad
                };
            }
        }

        public void SetLoadList(IEnumerable<TransferableOneWay> transferables)
        {
            leftToLoad = new List<TransferableOneWay>();
            foreach (TransferableOneWay source in transferables.Where(x => x.CountToTransfer > 0))
            {
                TransferableOneWay copy = new TransferableOneWay();
                copy.things.AddRange(source.things);
                copy.AdjustTo(source.CountToTransfer);
                leftToLoad.Add(copy);
            }
        }

        public int SubtractFromLoadList(Thing thing, int count)
        {
            if (leftToLoad == null)
            {
                return 0;
            }

            TransferableOneWay transferable = TransferableUtility.TransferableMatchingDesperate(
                thing, leftToLoad, TransferAsOneMode.PodsOrCaravanPacking);
            if (transferable == null || transferable.CountToTransfer <= 0)
            {
                return 0;
            }

            int removed = Mathf.Min(count, transferable.CountToTransfer);
            transferable.AdjustBy(-removed);
            transferable.things.Remove(thing);
            if (transferable.CountToTransfer <= 0)
            {
                leftToLoad.Remove(transferable);
            }
            return removed;
        }

        public bool PawnSelectedToEnter(Pawn pawn)
        {
            return leftToLoad != null && leftToLoad.Any(x => x.CountToTransfer > 0 && x.things.Contains(pawn));
        }

        public void NotifyPawnTransferred()
        {
            beenEntered = true;
        }

        public void AcceptThing(Thing thing)
        {
            GetOrCreatePocketMap();
            SubtractFromLoadList(thing, thing.stackCount);
            GenDrop.TryDropSpawn(thing, DestinationCell, pocketMap, ThingPlaceMode.Near, out _);
        }

        public void CancelLoad()
        {
            Lord lord = Shuttle.Map?.lordManager.lords.FirstOrDefault(x =>
                x.LordJob is LordJob_LoadPocketMap job && job.shuttle == Shuttle);
            if (lord != null)
            {
                Shuttle.Map.lordManager.RemoveLord(lord);
            }
            leftToLoad?.Clear();
        }

        public void EjectAndDestroyPocketMap(Map destinationMap, IntVec3 destination)
        {
            if (PocketMap == null)
            {
                return;
            }

            List<Thing> things = pocketMap.listerThings.AllThings
                .Where(x => x is Pawn || x.def.category == ThingCategory.Item)
                .ToList();
            foreach (Thing thing in things)
            {
                if (thing.Spawned)
                {
                    thing.DeSpawn();
                    GenPlace.TryPlaceThing(thing, destination, destinationMap, ThingPlaceMode.Near);
                }
            }

            Map map = pocketMap;
            pocketMap = null;
            exit = null;
            leftToLoad?.Clear();
            PocketMapUtility.DestroyPocketMap(map);
        }
    }

    public class PocketMapContainerProxy : ThingOwner
    {
        private readonly CompPocketMapPortal portal;

        public PocketMapContainerProxy(CompPocketMapPortal portal)
        {
            this.portal = portal;
        }

        public override int Count => 0;
        public override bool TryAdd(Thing item, bool canMergeWithExistingStacks = true)
        {
            portal.AcceptThing(item);
            return true;
        }
        public override int TryAdd(Thing item, int count, bool canMergeWithExistingStacks = true)
        {
            return TryAdd(item, canMergeWithExistingStacks) ? count : 0;
        }
        public override int IndexOf(Thing item) => -1;
        public override bool Remove(Thing item) => false;
        protected override Thing GetAt(int index) => null;
    }
}
