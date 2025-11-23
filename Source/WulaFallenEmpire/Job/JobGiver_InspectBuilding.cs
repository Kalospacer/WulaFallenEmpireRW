using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    public class JobGiver_InspectBuilding : ThinkNode_JobGiver
    {
        // 检查间隔（ticks）
        private const int CheckInterval = 120; // 2秒检查一次

        // 最大考察距离
        private const float MaxDistance = 20f;

        // 默认最小间隔（ticks）- 5分钟
        private const int DefaultMinIntervalTicks = 300 * 60; // 5分钟 * 60秒/分钟 * 60ticks/秒

        // 存储每个 Pawn 的最后考察时间
        private static Dictionary<Pawn, int> lastInspectionTicks = new Dictionary<Pawn, int>();

        protected override Job TryGiveJob(Pawn pawn)
        {
            // 检查 Pawn 是否有效
            if (pawn == null || pawn.Destroyed || !pawn.Spawned || pawn.Map == null)
                return null;

            // 检查 Pawn 是否能够工作
            if (pawn.Downed || pawn.InMentalState || !pawn.health.capacities.CanBeAwake)
                return null;

            // 检查 Pawn 是否能够移动
            if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Moving))
                return null;

            // 检查背景故事是否为军团背景
            if (!HasLegionBackstory(pawn))
                return null;

            // 检查是否已经有工作
            if (pawn.CurJob != null && pawn.CurJob.def == JobDefOf_WULA.WULA_InspectBuilding)
                return null;

            // 检查冷却时间
            if (!CanInspectNow(pawn))
                return null;

            // 寻找合适的考察目标
            Thing inspectionTarget = FindRandomInspectionTarget(pawn);
            if (inspectionTarget == null)
                return null;

            // 创建考察工作
            Job job = JobMaker.MakeJob(JobDefOf_WULA.WULA_InspectBuilding, inspectionTarget);
            job.expiryInterval = Rand.Range(300, 600); // 5-10秒的随机时间
            job.checkOverrideOnExpire = true;

            // 记录开始考察时间
            RecordInspectionStart(pawn);

            // 记录调试信息
            if (Prefs.DevMode)
            {
                Log.Message($"[JobGiver_InspectBuilding] Assigned inspection job to {pawn.Name} at {inspectionTarget.Label}");
            }

            return job;
        }

        /// <summary>
        /// 检查 Pawn 是否具有军团背景故事
        /// </summary>
        private bool HasLegionBackstory(Pawn pawn)
        {
            if (pawn.story == null)
                return false;

            // 检查成年背景故事是否为军团背景
            if (pawn.story.Adulthood != null && pawn.story.Adulthood.identifier == "WULA_Adult_Backstory_Legion")
                return true;

            return false;
        }

        /// <summary>
        /// 检查 Pawn 是否可以开始新的考察（冷却时间检查）
        /// </summary>
        private bool CanInspectNow(Pawn pawn)
        {
            // 获取设置的最小间隔时间
            int minIntervalTicks = GetMinInspectionIntervalTicks();

            // 如果 Pawn 没有记录，说明可以立即开始
            if (!lastInspectionTicks.ContainsKey(pawn))
                return true;

            int lastTick = lastInspectionTicks[pawn];
            int currentTick = Find.TickManager.TicksGame;
            int elapsedTicks = currentTick - lastTick;

            // 检查是否已经过了最小间隔时间
            bool canInspect = elapsedTicks >= minIntervalTicks;

            if (Prefs.DevMode && !canInspect)
            {
                int remainingTicks = minIntervalTicks - elapsedTicks;
                float remainingSeconds = remainingTicks / 60f;
                Log.Message($"[JobGiver_InspectBuilding] {pawn.Name} must wait {remainingSeconds:F1} seconds before next inspection");
            }

            return canInspect;
        }

        /// <summary>
        /// 记录 Pawn 开始考察的时间
        /// </summary>
        private void RecordInspectionStart(Pawn pawn)
        {
            lastInspectionTicks[pawn] = Find.TickManager.TicksGame;

            if (Prefs.DevMode)
            {
                Log.Message($"[JobGiver_InspectBuilding] Recorded inspection start for {pawn.Name} at tick {lastInspectionTicks[pawn]}");
            }
        }

        /// <summary>
        /// 获取最小考察间隔时间（ticks）
        /// </summary>
        private int GetMinInspectionIntervalTicks()
        {
            // 这里可以从 Mod 设置中获取值
            // 暂时返回默认值，您可以根据需要修改
            return DefaultMinIntervalTicks;
        }

        /// <summary>
        /// 随机寻找合适的考察目标
        /// </summary>
        private Thing FindRandomInspectionTarget(Pawn pawn)
        {
            // 获取地图上所有符合条件的建筑
            List<Thing> validBuildings = new List<Thing>();
            
            // 遍历地图上的所有建筑
            foreach (Thing thing in pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial))
            {
                if (IsValidInspectionTarget(thing, pawn))
                {
                    validBuildings.Add(thing);
                }
            }

            // 如果没有找到合适的建筑，返回null
            if (validBuildings.Count == 0)
            {
                if (Prefs.DevMode)
                {
                    Log.Message($"[JobGiver_InspectBuilding] No valid inspection targets found for {pawn.Name}");
                }
                return null;
            }

            // 随机选择一个建筑
            Thing selectedBuilding = validBuildings.RandomElement();
            
            if (Prefs.DevMode)
            {
                Log.Message($"[JobGiver_InspectBuilding] Randomly selected {selectedBuilding.Label} from {validBuildings.Count} valid targets");
            }

            return selectedBuilding;
        }

        /// <summary>
        /// 检查是否有效的考察目标
        /// </summary>
        private bool IsValidInspectionTarget(Thing thing, Pawn pawn)
        {
            // 基本检查
            if (thing == null || thing.Destroyed)
                return false;

            // 检查是否玩家拥有
            if (thing.Faction != Faction.OfPlayer)
                return false;

            // 检查是否可到达
            if (!pawn.CanReach(thing, PathEndMode.Touch, Danger.None))
                return false;

            // 排除一些不适合的建筑类型
            if (thing.def.IsFrame || thing.def.IsBlueprint)
                return false;

            // 确保建筑是完整的
            if (thing is Building building && (building.IsBurning() || building.IsBrokenDown()))
                return false;

            // 确保不是禁止进入的区域
            if (thing.IsForbidden(pawn))
                return false;

            // 确保没有其他 Pawn 正在考察这个建筑
            if (IsBeingInspectedByOther(thing, pawn))
                return false;

            // 排除墙壁建筑
            if (IsWall(thing))
                return false;

            // 距离检查（可选，但为了性能考虑可以保留）
            if (pawn.Position.DistanceTo(thing.Position) > MaxDistance)
                return false;

            return true;
        }

        /// <summary>
        /// 检查是否为墙壁
        /// </summary>
        private bool IsWall(Thing thing)
        {
            // 检查建筑的 def 中是否有 isWall 标签
            if (thing.def?.building != null && thing.def.building.isWall)
            {
                if (Prefs.DevMode)
                {
                    Log.Message($"[JobGiver_InspectBuilding] Excluding wall: {thing.Label}");
                }
                return true;
            }

            // 额外的检查：通过 defName 或标签判断
            if (thing.def?.defName == "Wall" || 
                (thing.def?.thingCategories?.Any(c => c.defName == "Walls") ?? false))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// 检查是否有其他 Pawn 正在考察这个建筑
        /// </summary>
        private bool IsBeingInspectedByOther(Thing thing, Pawn currentPawn)
        {
            foreach (Pawn otherPawn in thing.Map.mapPawns.AllPawnsSpawned)
            {
                if (otherPawn != currentPawn && 
                    otherPawn.CurJob != null && 
                    otherPawn.CurJob.def == JobDefOf_WULA.WULA_InspectBuilding &&
                    otherPawn.CurJob.targetA.Thing == thing)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 清理不再存在的 Pawn 的记录
        /// </summary>
        public static void CleanupInspectionRecords()
        {
            List<Pawn> toRemove = new List<Pawn>();
            
            foreach (var pair in lastInspectionTicks)
            {
                if (pair.Key.Destroyed || !pair.Key.Spawned)
                {
                    toRemove.Add(pair.Key);
                }
            }

            foreach (Pawn pawn in toRemove)
            {
                lastInspectionTicks.Remove(pawn);
            }

            if (Prefs.DevMode && toRemove.Count > 0)
            {
                Log.Message($"[JobGiver_InspectBuilding] Cleaned up {toRemove.Count} inspection records");
            }
        }

        public class InspectionCleanupComponent : WorldComponent
        {
            private int lastCleanupTick = 0;
            private const int CleanupIntervalTicks = 6000; // 每100秒清理一次
            public InspectionCleanupComponent(World world) : base(world) { }
            public override void WorldComponentTick()
            {
                base.WorldComponentTick();
                int currentTick = Find.TickManager.TicksGame;
                if (currentTick - lastCleanupTick > CleanupIntervalTicks)
                {
                    JobGiver_InspectBuilding.CleanupInspectionRecords();
                    lastCleanupTick = currentTick;
                }
            }
        }
    }
}
