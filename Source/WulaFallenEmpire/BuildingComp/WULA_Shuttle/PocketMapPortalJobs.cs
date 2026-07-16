using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace WulaFallenEmpire
{
    public static class PocketMapPortalUtility
    {
        private static readonly Dictionary<TransferableOneWay, int> AlreadyLoading =
            new Dictionary<TransferableOneWay, int>();

        public static CompPocketMapPortal PortalFor(Thing thing)
        {
            return (thing as Building_ArmedShuttleWithPocket)?.PocketPortal;
        }

        public static ThingCount FindThingToLoad(Pawn pawn, CompPocketMapPortal portal)
        {
            if (portal?.LeftToLoad == null)
            {
                return default;
            }

            AlreadyLoading.Clear();
            foreach (Pawn otherPawn in portal.Shuttle.Map.mapPawns.AllPawnsSpawned)
            {
                if (otherPawn == pawn || otherPawn.CurJobDef != Wula_JobDefOf.WULA_HaulToShuttlePocketMap
                    || !(otherPawn.jobs.curDriver is JobDriver_HaulToShuttlePocketMap driver)
                    || driver.Shuttle != portal.Shuttle)
                {
                    continue;
                }

                TransferableOneWay reserved = TransferableUtility.TransferableMatchingDesperate(
                    driver.ThingToCarry, portal.LeftToLoad, TransferAsOneMode.PodsOrCaravanPacking);
                if (reserved != null)
                {
                    AlreadyLoading[reserved] = AlreadyLoading.TryGetValue(reserved, out int count)
                        ? count + driver.initialCount
                        : driver.initialCount;
                }
            }

            foreach (TransferableOneWay transferable in portal.LeftToLoad.Where(x => x.CountToTransfer > 0))
            {
                int remaining = transferable.CountToTransfer
                    - (AlreadyLoading.TryGetValue(transferable, out int loading) ? loading : 0);
                if (remaining <= 0)
                {
                    continue;
                }

                Thing thing = GenClosest.ClosestThingReachable(
                    pawn.Position, pawn.Map, ThingRequest.ForGroup(ThingRequestGroup.HaulableEver),
                    PathEndMode.Touch, TraverseParms.For(pawn), 9999f,
                    x => transferable.things.Contains(x) && x.Spawned && pawn.CanReserve(x)
                        && !x.IsForbidden(pawn) && pawn.carryTracker.AvailableStackSpace(x.def) > 0);
                if (thing != null)
                {
                    AlreadyLoading.Clear();
                    return new ThingCount(thing, System.Math.Min(thing.stackCount, remaining));
                }
            }
            AlreadyLoading.Clear();
            return default;
        }

        public static void MakeLord(List<Pawn> pawns, CompPocketMapPortal portal)
        {
            List<Pawn> valid = pawns.Where(x => x.Spawned && !x.Downed
                && (x.IsColonist || x.IsColonyMechPlayerControlled)).ToList();
            if (valid.Count == 0)
            {
                return;
            }

            Lord lord = portal.Shuttle.Map.lordManager.lords.FirstOrDefault(x =>
                x.LordJob is LordJob_LoadPocketMap job && job.shuttle == portal.Shuttle)
                ?? LordMaker.MakeNewLord(Faction.OfPlayer, new LordJob_LoadPocketMap(portal.Shuttle), portal.Shuttle.Map);
            foreach (Pawn pawn in valid)
            {
                if (!lord.ownedPawns.Contains(pawn))
                {
                    pawn.GetLord()?.Notify_PawnLost(pawn, PawnLostCondition.ForcedToJoinOtherLord);
                    lord.AddPawn(pawn);
                    pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
                }
            }

        }
    }

    public class JobDriver_EnterShuttlePocketMap : JobDriver
    {
        private Building_ArmedShuttleWithPocket Shuttle => TargetThingA as Building_ArmedShuttleWithPocket;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => !Shuttle.PocketPortal.IsEnterable(out _));
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell);
            yield return Toils_General.Wait(90).WithProgressBarToilDelay(TargetIndex.A);
            Toil enter = ToilMaker.MakeToil("EnterShuttlePocketMap");
            enter.initAction = delegate
            {
                CompPocketMapPortal portal = Shuttle.PocketPortal;
                Map map = portal.GetOrCreatePocketMap();
                IntVec3 destination = portal.DestinationCell;
                if (!destination.IsValid || !destination.Standable(map))
                {
                    destination = CellFinder.StandableCellNear(destination, map, 5f);
                }
                if (!destination.IsValid)
                {
                    Messages.Message("UnableToEnterPortal".Translate(Shuttle.Label), Shuttle, MessageTypeDefOf.NegativeEvent);
                    return;
                }

                bool drafted = pawn.IsPlayerControlled && pawn.Drafted;
                bool fireAtWill = drafted && pawn.drafter.FireAtWill;
                pawn.DeSpawnOrDeselect();
                GenSpawn.Spawn(pawn, destination, map, Rot4.Random);
                portal.SubtractFromLoadList(pawn, 1);
                portal.NotifyPawnTransferred();
                if (drafted)
                {
                    pawn.drafter.Drafted = true;
                    pawn.drafter.FireAtWill = fireAtWill;
                }
                pawn.GetLord()?.Notify_PawnLost(pawn, PawnLostCondition.ExitedMap);
            };
            yield return enter;
        }
    }

    public class JobDriver_HaulToShuttlePocketMap : JobDriver
    {
        public int initialCount;
        public Building_ArmedShuttleWithPocket Shuttle => TargetThingB as Building_ArmedShuttleWithPocket;
        public Thing ThingToCarry => TargetThingA;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref initialCount, "initialCount");
        }

        public override void Notify_Starting()
        {
            base.Notify_Starting();
            initialCount = job.count;
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(TargetA, job, 1, job.count, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOnDespawnedOrNull(TargetIndex.B);
            this.FailOn(() => !Shuttle.PocketPortal.LoadInProgress);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch);
            yield return Toils_Haul.StartCarryThing(TargetIndex.A, false, false, false);
            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.InteractionCell);
            yield return Toils_General.Wait(90).WithProgressBarToilDelay(TargetIndex.B);
            Toil deposit = ToilMaker.MakeToil("DepositInShuttlePocketMap");
            deposit.initAction = delegate
            {
                Thing thing = pawn.carryTracker.CarriedThing;
                if (thing != null)
                {
                    pawn.carryTracker.innerContainer.TryTransferToContainer(
                        thing, Shuttle.PocketPortal.ContainerProxy, thing.stackCount);
                }
            };
            yield return deposit;
        }
    }

    public class WorkGiver_HaulToShuttlePocketMap : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest =>
            ThingRequest.ForGroup(ThingRequestGroup.BuildingArtificial);

        public override PathEndMode PathEndMode => PathEndMode.InteractionCell;

        public override Danger MaxPathDanger(Pawn pawn) => Danger.Deadly;

        public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            if (!(thing is Building_ArmedShuttleWithPocket))
            {
                return false;
            }

            CompPocketMapPortal portal = PocketMapPortalUtility.PortalFor(thing);
            return portal != null
                && portal.LoadInProgress
                && portal.IsEnterable(out _)
                && pawn.CanReach(thing, PathEndMode.InteractionCell, Danger.Deadly)
                && PocketMapPortalUtility.FindThingToLoad(pawn, portal).Thing != null;
        }

        public override Job JobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            CompPocketMapPortal portal = PocketMapPortalUtility.PortalFor(thing);
            ThingCount load = PocketMapPortalUtility.FindThingToLoad(pawn, portal);
            if (load.Thing == null)
            {
                return null;
            }

            Job job = JobMaker.MakeJob(Wula_JobDefOf.WULA_HaulToShuttlePocketMap,
                load.Thing, portal.Shuttle);
            job.count = load.Count;
            job.ignoreForbidden = forced;
            return job;
        }
    }

    public class JobGiver_LoadShuttlePocketMap : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            CompPocketMapPortal portal = PocketMapPortalUtility.PortalFor(pawn.mindState.duty.focus.Thing);
            if (portal == null || !portal.IsEnterable(out _))
            {
                return null;
            }

            ThingCount thing = PocketMapPortalUtility.FindThingToLoad(pawn, portal);
            if (thing.Thing != null)
            {
                Job haul = JobMaker.MakeJob(Wula_JobDefOf.WULA_HaulToShuttlePocketMap, thing.Thing, portal.Shuttle);
                haul.count = thing.Count;
                haul.ignoreForbidden = true;
                return haul;
            }

            if (portal.PawnSelectedToEnter(pawn))
            {
                return JobMaker.MakeJob(Wula_JobDefOf.WULA_EnterShuttlePocketMap, portal.Shuttle);
            }
            return null;
        }
    }

    public class LordJob_LoadPocketMap : LordJob
    {
        public Building_ArmedShuttleWithPocket shuttle;
        public override bool AllowStartNewGatherings => false;
        public override bool AddFleeToil => false;

        public LordJob_LoadPocketMap()
        {
        }

        public LordJob_LoadPocketMap(Building_ArmedShuttleWithPocket shuttle)
        {
            this.shuttle = shuttle;
        }

        public override void ExposeData()
        {
            Scribe_References.Look(ref shuttle, "shuttle");
        }

        public override StateGraph CreateGraph()
        {
            StateGraph graph = new StateGraph();
            graph.StartingToil = new LordToil_LoadPocketMap(shuttle);
            graph.AddToil(new LordToil_End());
            return graph;
        }
    }

    public class LordToil_LoadPocketMap : LordToil
    {
        private readonly Building_ArmedShuttleWithPocket shuttle;
        public override bool AllowSatisfyLongNeeds => false;

        public LordToil_LoadPocketMap(Building_ArmedShuttleWithPocket shuttle)
        {
            this.shuttle = shuttle;
        }

        public override void UpdateAllDuties()
        {
            foreach (Pawn pawn in lord.ownedPawns)
            {
                pawn.mindState.duty = new PawnDuty(Wula_DutyDefOf.WULA_LoadShuttlePocketMap)
                {
                    focus = shuttle
                };
            }
        }
    }
}
