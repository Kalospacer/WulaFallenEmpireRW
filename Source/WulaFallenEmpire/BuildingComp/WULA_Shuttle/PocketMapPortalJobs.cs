using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace WulaFallenEmpire
{
    public static class PocketMapPortalUtility
    {
        private static readonly HashSet<Thing> NeededThings = new HashSet<Thing>();
        private static readonly Dictionary<TransferableOneWay, int> AlreadyLoading =
            new Dictionary<TransferableOneWay, int>();

        public static CompPocketMapPortal PortalFor(Thing thing)
        {
            return (thing as Building_ArmedShuttleWithPocket)?.PocketPortal;
        }

        public static bool HasJobOnPortal(Pawn pawn, CompPocketMapPortal portal)
        {
            if (portal == null || !portal.LoadInProgress)
            {
                return false;
            }

            if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
            {
                return false;
            }

            if (!portal.IsEnterable(out _))
            {
                return false;
            }

            if (!pawn.CanReach(portal.Shuttle, PathEndMode.InteractionCell, pawn.NormalMaxDanger()))
            {
                return false;
            }

            return FindThingToLoad(pawn, portal).Thing != null;
        }

        public static Job JobOnPortal(Pawn pawn, CompPocketMapPortal portal)
        {
            Job job = JobMaker.MakeJob(
                Wula_JobDefOf.WULA_HaulToShuttlePocketMap,
                LocalTargetInfo.Invalid,
                portal.Shuttle);
            job.ignoreForbidden = true;
            return job;
        }

        public static ThingCount FindThingToLoad(Pawn pawn, CompPocketMapPortal portal)
        {
            if (portal?.LeftToLoad == null)
            {
                return default;
            }

            NeededThings.Clear();
            AlreadyLoading.Clear();

            IReadOnlyList<Pawn> allPawns = portal.Shuttle.Map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < allPawns.Count; i++)
            {
                Pawn other = allPawns[i];
                if (other == pawn
                    || other.CurJobDef != Wula_JobDefOf.WULA_HaulToShuttlePocketMap
                    || !(other.jobs.curDriver is JobDriver_HaulToShuttlePocketMap driver)
                    || driver.Shuttle != portal.Shuttle)
                {
                    continue;
                }

                TransferableOneWay reserved = TransferableUtility.TransferableMatchingDesperate(
                    driver.ThingToCarry, portal.LeftToLoad, TransferAsOneMode.PodsOrCaravanPacking);
                if (reserved == null)
                {
                    continue;
                }

                AlreadyLoading[reserved] = AlreadyLoading.TryGetValue(reserved, out int count)
                    ? count + driver.initialCount
                    : driver.initialCount;
            }

            for (int i = 0; i < portal.LeftToLoad.Count; i++)
            {
                TransferableOneWay transferable = portal.LeftToLoad[i];
                int reservedCount = AlreadyLoading.TryGetValue(transferable, out int loading) ? loading : 0;
                if (transferable.CountToTransfer - reservedCount <= 0)
                {
                    continue;
                }

                for (int j = 0; j < transferable.things.Count; j++)
                {
                    NeededThings.Add(transferable.things[j]);
                }
            }

            if (NeededThings.Count == 0)
            {
                NeededThings.Clear();
                AlreadyLoading.Clear();
                return default;
            }

            Thing thing = GenClosest.ClosestThingReachable(
                pawn.Position,
                pawn.Map,
                ThingRequest.ForGroup(ThingRequestGroup.HaulableEver),
                PathEndMode.Touch,
                TraverseParms.For(pawn),
                9999f,
                x => NeededThings.Contains(x)
                    && pawn.CanReserve(x)
                    && !x.IsForbidden(pawn)
                    && pawn.carryTracker.AvailableStackSpace(x.def) > 0,
                null,
                0,
                -1,
                forceAllowGlobalSearch: false,
                RegionType.Set_Passable,
                ignoreEntirelyForbiddenRegions: false,
                lookInHaulSources: true);

            if (thing == null)
            {
                foreach (Thing needed in NeededThings)
                {
                    if (needed is Pawn cargo
                        && cargo.Spawned
                        && ((!cargo.IsColonist && !cargo.IsColonyMech) || cargo.Downed || cargo.IsSelfShutdown())
                        && !cargo.inventory.UnloadEverything
                        && pawn.CanReserveAndReach(cargo, PathEndMode.Touch, Danger.Deadly))
                    {
                        NeededThings.Clear();
                        AlreadyLoading.Clear();
                        return new ThingCount(cargo, 1);
                    }
                }
            }

            NeededThings.Clear();
            if (thing == null)
            {
                AlreadyLoading.Clear();
                return default;
            }

            TransferableOneWay match = null;
            for (int i = 0; i < portal.LeftToLoad.Count; i++)
            {
                if (portal.LeftToLoad[i].things.Contains(thing))
                {
                    match = portal.LeftToLoad[i];
                    break;
                }
            }

            int already = match != null && AlreadyLoading.TryGetValue(match, out int value) ? value : 0;
            AlreadyLoading.Clear();
            if (match == null)
            {
                return default;
            }

            return new ThingCount(thing, Mathf.Min(match.CountToTransfer - already, thing.stackCount));
        }

        public static IEnumerable<Thing> ThingsBeingHauledTo(CompPocketMapPortal portal)
        {
            if (portal?.Shuttle?.Map == null)
            {
                yield break;
            }

            IReadOnlyList<Pawn> pawns = portal.Shuttle.Map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn hauler = pawns[i];
                if (hauler.CurJobDef != Wula_JobDefOf.WULA_HaulToShuttlePocketMap
                    || !(hauler.jobs.curDriver is JobDriver_HaulToShuttlePocketMap driver)
                    || driver.Shuttle != portal.Shuttle
                    || hauler.carryTracker.CarriedThing == null)
                {
                    continue;
                }

                yield return hauler.carryTracker.CarriedThing;
            }
        }

        public static void MakeLordsAsAppropriate(List<Pawn> pawns, CompPocketMapPortal portal)
        {
            if (portal?.Shuttle?.Map == null)
            {
                return;
            }

            IEnumerable<Pawn> enterers = pawns.Where(x =>
                (x.IsColonist || x.IsColonyMechPlayerControlled)
                && !x.Downed
                && x.Spawned
                && x.needs?.energy?.IsSelfShutdown != true);

            Lord lord = null;
            if (enterers.Any())
            {
                lord = portal.Shuttle.Map.lordManager.lords.FirstOrDefault(x =>
                    x.LordJob is LordJob_LoadPocketMap job && job.shuttle == portal.Shuttle)
                    ?? LordMaker.MakeNewLord(
                        Faction.OfPlayer,
                        new LordJob_LoadPocketMap(portal.Shuttle),
                        portal.Shuttle.Map);

                foreach (Pawn pawn in enterers)
                {
                    if (!lord.ownedPawns.Contains(pawn))
                    {
                        pawn.GetLord()?.Notify_PawnLost(pawn, PawnLostCondition.ForcedToJoinOtherLord);
                        lord.AddPawn(pawn);
                        pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
                    }
                }

                for (int i = lord.ownedPawns.Count - 1; i >= 0; i--)
                {
                    if (!enterers.Contains(lord.ownedPawns[i]))
                    {
                        lord.Notify_PawnLost(lord.ownedPawns[i], PawnLostCondition.LordRejected);
                    }
                }
            }

            List<Lord> lords = portal.Shuttle.Map.lordManager.lords;
            for (int i = lords.Count - 1; i >= 0; i--)
            {
                if (lords[i].LordJob is LordJob_LoadPocketMap job
                    && job.shuttle == portal.Shuttle
                    && lords[i] != lord)
                {
                    portal.Shuttle.Map.lordManager.RemoveLord(lords[i]);
                }
            }
        }

        public static bool WasLoadingCanceled(CompPocketMapPortal portal)
        {
            return portal == null || !portal.LoadInProgress;
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

            Toil wait = Toils_General.Wait(90)
                .FailOnCannotTouch(TargetIndex.A, PathEndMode.InteractionCell)
                .WithProgressBarToilDelay(TargetIndex.A, interpolateBetweenActorAndTarget: true);
            wait.tickIntervalAction = (System.Action<int>)System.Delegate.Combine(
                wait.tickIntervalAction,
                (System.Action<int>)delegate { pawn.rotationTracker.FaceTarget(TargetA); });
            wait.handlingFacing = true;
            yield return wait;

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
                    Messages.Message(
                        "UnableToEnterPortal".Translate(Shuttle.Label),
                        Shuttle,
                        MessageTypeDefOf.NegativeEvent);
                    return;
                }

                bool drafted = false;
                bool fireAtWill = false;
                if (pawn.IsPlayerControlled)
                {
                    drafted = pawn.Drafted;
                    fireAtWill = pawn.drafter.FireAtWill;
                }

                pawn.DeSpawnOrDeselect();
                GenSpawn.Spawn(pawn, destination, map, Rot4.Random);
                portal.NotifyPawnEntered(pawn);

                if (pawn.inventory != null)
                {
                    pawn.inventory.UnloadEverything = !map.IsPocketMap;
                }

                if (pawn.IsPlayerControlled && drafted)
                {
                    pawn.drafter.Drafted = true;
                    pawn.drafter.FireAtWill = fireAtWill;
                }

                if (pawn.carryTracker?.CarriedThing != null && !pawn.Drafted)
                {
                    pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Direct, out _);
                }

                pawn.mindState.priorityWork.ClearPrioritizedWorkAndJobQueue();
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
            CompPocketMapPortal portal = Shuttle?.PocketPortal;
            ThingCount thingCount = job.targetA.IsValid
                ? new ThingCount(job.targetA.Thing, job.count > 0 ? job.count : job.targetA.Thing.stackCount)
                : PocketMapPortalUtility.FindThingToLoad(pawn, portal);

            if (job.playerForced
                && pawn.carryTracker.CarriedThing != null
                && pawn.carryTracker.CarriedThing != thingCount.Thing)
            {
                pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out _);
            }

            if (thingCount.Thing == null)
            {
                return;
            }

            job.targetA = thingCount.Thing;
            job.count = thingCount.Count;
            initialCount = thingCount.Count;
            pawn.Reserve(thingCount.Thing, job);
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (!job.targetA.IsValid)
            {
                return true;
            }

            return pawn.Reserve(TargetA, job, 1, job.count, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOnDespawnedOrNull(TargetIndex.B);
            this.FailOn(() => PocketMapPortalUtility.WasLoadingCanceled(Shuttle.PocketPortal));
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch);
            yield return Toils_Haul.StartCarryThing(TargetIndex.A, putRemainderInQueue: false, subtractNumTakenFromJobCount: false, failIfStackCountLessThanJobCount: false);
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
            CompPocketMapPortal portal = PocketMapPortalUtility.PortalFor(thing);
            return PocketMapPortalUtility.HasJobOnPortal(pawn, portal);
        }

        public override Job JobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            CompPocketMapPortal portal = PocketMapPortalUtility.PortalFor(thing);
            if (portal == null)
            {
                return null;
            }

            return PocketMapPortalUtility.JobOnPortal(pawn, portal);
        }
    }

    public class JobGiver_HaulToShuttlePocketMap : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            CompPocketMapPortal portal = PocketMapPortalUtility.PortalFor(pawn.mindState.duty.focus.Thing);
            if (!PocketMapPortalUtility.HasJobOnPortal(pawn, portal))
            {
                return null;
            }

            return PocketMapPortalUtility.JobOnPortal(pawn, portal);
        }
    }

    public class JobGiver_EnterShuttlePocketMap : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            CompPocketMapPortal portal = PocketMapPortalUtility.PortalFor(pawn.mindState.duty.focus.Thing);
            if (portal == null
                || portal.Shuttle.Map != pawn.Map
                || !portal.IsEnterable(out _)
                || !portal.PawnSelectedToEnter(pawn)
                || !pawn.CanReach(portal.Shuttle, PathEndMode.InteractionCell, Danger.Deadly))
            {
                return null;
            }

            Job job = JobMaker.MakeJob(Wula_JobDefOf.WULA_EnterShuttlePocketMap, portal.Shuttle);
            job.locomotionUrgency = PawnUtility.ResolveLocomotion(pawn, LocomotionUrgency.Jog);
            return job;
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
