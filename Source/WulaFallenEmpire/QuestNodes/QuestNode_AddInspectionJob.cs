using RimWorld;
using RimWorld.QuestGen;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    public class QuestNode_AddInspectionJob : QuestNode
    {
        public SlateRef<Pawn> pawn;                     // 直接接收 Pawn 对象
        
        public SlateRef<JobDef> inspectionJobDef;       // 考察工作定义
        public SlateRef<float> inspectionDuration;      // 考察停留时间（秒）
        public SlateRef<bool> requirePlayerOwned;       // 是否要求玩家拥有
        public SlateRef<bool> requireAccessible;        // 是否要求可到达

        protected override bool TestRunInt(Slate slate)
        {
            if (inspectionJobDef.GetValue(slate) == null)
            {
                WulaLog.Debug("[QuestNode_AddInspectionJob] inspectionJobDef is null");
                return false;
            }

            return true;
        }

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            
            // 直接获取 Pawn 对象
            Pawn pawnValue = pawn.GetValue(slate);
            if (pawnValue == null)
            {
                WulaLog.Debug("[QuestNode_AddInspectionJob] pawn is null");
                return;
            }

            // 检查 Pawn 是否有效且已生成
            if (!IsPawnValidAndSpawned(pawnValue))
            {
                // 如果 Pawn 无效或未生成，记录调试信息但不报错
                if (QuestGen.slate.Get<bool>("debugLogging", false))
                {
                    WulaLog.Debug($"[QuestNode_AddInspectionJob] Pawn {pawnValue.Name} is not ready for job assignment (Destroyed: {pawnValue.Destroyed}, Spawned: {pawnValue.Spawned}, Map: {pawnValue.Map?.Index ?? -1})");
                }
                return;
            }

            // 获取工作定义
            JobDef jobDef = inspectionJobDef.GetValue(slate) ?? JobDefOf.Wait;
            
            // 创建并分配工作
            Job job = CreateInspectionJob(pawnValue, jobDef, slate);
            if (job != null)
            {
                pawnValue.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                
                if (QuestGen.slate.Get<bool>("debugLogging", false))
                {
                    WulaLog.Debug($"[QuestNode_AddInspectionJob] Assigned inspection job to {pawnValue.Name} at position {pawnValue.Position}");
                }
            }
            else
            {
                if (QuestGen.slate.Get<bool>("debugLogging", false))
                {
                    WulaLog.Debug($"[QuestNode_AddInspectionJob] Failed to create inspection job for {pawnValue.Name}");
                }
            }
        }

        /// <summary>
        /// 检查 Pawn 是否有效且已生成
        /// </summary>
        private bool IsPawnValidAndSpawned(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed)
                return false;

            if (!pawn.Spawned)
                return false;

            if (pawn.Map == null)
                return false;

            return true;
        }

        /// <summary>
        /// 创建考察工作
        /// </summary>
        private Job CreateInspectionJob(Pawn pawn, JobDef jobDef, Slate slate)
        {
            // 寻找合适的考察目标
            Thing inspectionTarget = FindInspectionTarget(pawn, slate);
            if (inspectionTarget == null)
            {
                if (QuestGen.slate.Get<bool>("debugLogging", false))
                {
                    WulaLog.Debug($"[QuestNode_AddInspectionJob] No valid inspection target found for {pawn.Name} on map {pawn.Map}");
                }
                return null;
            }

            // 创建工作
            Job job = JobMaker.MakeJob(jobDef, inspectionTarget);
            
            // 设置停留时间（转换为 ticks）
            float duration = inspectionDuration.GetValue(slate);
            if (duration > 0)
            {
                job.expiryInterval = (int)(duration * 60f); // 秒转换为 ticks
            }
            else
            {
                job.expiryInterval = Rand.Range(180, 300); // 3-5 秒的随机时间
            }

            // 设置工作标签
            job.def.joyDuration = job.expiryInterval;
            
            if (QuestGen.slate.Get<bool>("debugLogging", false))
            {
                WulaLog.Debug($"[QuestNode_AddInspectionJob] Created inspection job for {pawn.Name} at {inspectionTarget.Label} (Position: {inspectionTarget.Position})");
            }

            return job;
        }

        /// <summary>
        /// 寻找考察目标
        /// </summary>
        private Thing FindInspectionTarget(Pawn pawn, Slate slate)
        {
            bool requirePlayerOwnedValue = requirePlayerOwned.GetValue(slate);
            bool requireAccessibleValue = requireAccessible.GetValue(slate);

            if (QuestGen.slate.Get<bool>("debugLogging", false))
            {
                WulaLog.Debug($"[QuestNode_AddInspectionJob] Searching for inspection target for {pawn.Name}");
                WulaLog.Debug($"[QuestNode_AddInspectionJob] Require player owned: {requirePlayerOwnedValue}, Require accessible: {requireAccessibleValue}");
            }

            // 寻找玩家拥有的建筑
            Thing target = GenClosest.ClosestThingReachable(
                pawn.Position,
                pawn.Map,
                ThingRequest.ForGroup(ThingRequestGroup.BuildingArtificial),
                PathEndMode.Touch,
                TraverseParms.For(pawn),
                maxDistance: 50f,
                validator: (Thing t) => IsValidInspectionTarget(t, pawn, requirePlayerOwnedValue, requireAccessibleValue)
            );

            if (QuestGen.slate.Get<bool>("debugLogging", false))
            {
                if (target != null)
                {
                    WulaLog.Debug($"[QuestNode_AddInspectionJob] Found target: {target.Label} at {target.Position}");
                }
                else
                {
                    WulaLog.Debug($"[QuestNode_AddInspectionJob] No target found within range");
                }
            }

            return target;
        }

        /// <summary>
        /// 检查是否有效的考察目标
        /// </summary>
        private bool IsValidInspectionTarget(Thing thing, Pawn pawn, bool requirePlayerOwned, bool requireAccessible)
        {
            // 基本检查
            if (thing == null || thing.Destroyed)
                return false;

            // 检查是否玩家拥有
            if (requirePlayerOwned && thing.Faction != Faction.OfPlayer)
            {
                if (QuestGen.slate.Get<bool>("debugLogging", false) && thing.Faction != null)
                {
                    WulaLog.Debug($"[QuestNode_AddInspectionJob] Target {thing.Label} faction: {thing.Faction.Name}, required: Player");
                }
                return false;
            }

            // 检查是否可到达
            if (requireAccessible && !pawn.CanReach(thing, PathEndMode.Touch, Danger.None))
            {
                if (QuestGen.slate.Get<bool>("debugLogging", false))
                {
                    WulaLog.Debug($"[QuestNode_AddInspectionJob] Target {thing.Label} at {thing.Position} is not reachable by {pawn.Name}");
                }
                return false;
            }

            // 排除一些不适合的建筑类型
            if (thing.def.IsFrame || thing.def.IsBlueprint)
                return false;

            // 确保建筑是完整的
            if (thing is Building building && (building.IsBurning() || building.IsBrokenDown()))
                return false;

            // 确保不是禁止进入的区域
            if (thing.IsForbidden(pawn))
                return false;

            if (QuestGen.slate.Get<bool>("debugLogging", false))
            {
                WulaLog.Debug($"[QuestNode_AddInspectionJob] Target {thing.Label} at {thing.Position} is valid");
            }

            return true;
        }
    }
}
