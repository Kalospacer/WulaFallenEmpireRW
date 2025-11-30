using RimWorld;
using System.Collections.Generic;
using Verse;
using System.Linq;
using System.Text;
using RimWorld.Planet;

namespace WulaFallenEmpire
{
    public class StorytellerComp_SimpleTechnologyTrigger : StorytellerComp
    {
        private StorytellerCompProperties_SimpleTechnologyTrigger SimpleProps => 
            (StorytellerCompProperties_SimpleTechnologyTrigger)props;

        private static int IntervalsPassed => Find.TickManager.TicksGame / 1000;

        public override IEnumerable<FiringIncident> MakeIntervalIncidents(IIncidentTarget target)
        {
            // 检查基础条件（天数）
            if (IntervalsPassed <= SimpleProps.fireAfterDaysPassed * 60)
                yield break;

            // 检查是否满足周期
            if (!PassesIntervalCheck())
                yield break;

            // 检查派系关系条件
            var factionCheckResult = PassesRequiredFactionCheck();
            
            // 根据派系关系结果决定触发哪个事件
            IncidentDef incidentToTrigger = GetIncidentBasedOnFactionRelation(factionCheckResult);
            
            if (incidentToTrigger == null)
                yield break;

            // 检查派系过滤条件
            if (!PassesFactionFilter(target))
                yield break;

            // 检查科技条件
            if (!PassesTechnologyCheck())
                yield break;

            // 检查是否防止重复任务
            if (SimpleProps.preventDuplicateQuests && HasActiveQuest(incidentToTrigger))
                yield break;

            // 触发事件
            if (incidentToTrigger.TargetAllowed(target))
            {
                if (SimpleProps.debugLogging)
                {
                    Log.Message($"[SimpleTechnologyTrigger] Triggering incident {incidentToTrigger.defName} for technology {SimpleProps.technology.defName}");
                    Log.Message($"[SimpleTechnologyTrigger] Faction relation status: {factionCheckResult}");
                }

                yield return new FiringIncident(incidentToTrigger, this, GenerateParms(incidentToTrigger.category, target));
            }
        }

        /// <summary>
        /// 根据派系关系状态决定触发哪个事件
        /// </summary>
        private IncidentDef GetIncidentBasedOnFactionRelation(FactionRelationResult factionCheckResult)
        {
            switch (factionCheckResult.Status)
            {
                case FactionRelationStatus.Valid:
                    return SimpleProps.incident;
                
                case FactionRelationStatus.HostileButAllowed:
                    // 如果配置了敌对情况下的替代事件，则使用替代事件
                    if (SimpleProps.incidentIfHostile != null)
                    {
                        if (SimpleProps.debugLogging)
                        {
                            Log.Message($"[SimpleTechnologyTrigger] Using hostile alternative incident: {SimpleProps.incidentIfHostile.defName}");
                        }
                        return SimpleProps.incidentIfHostile;
                    }
                    // 如果没有配置替代事件，则使用原事件
                    return SimpleProps.incident;
                
                case FactionRelationStatus.Invalid:
                default:
                    return null;
            }
        }

        /// <summary>
        /// 检查必需派系关系条件
        /// </summary>
        private FactionRelationResult PassesRequiredFactionCheck()
        {
            // 如果没有配置必需派系，直接通过
            if (SimpleProps.requiredFaction == null)
                return FactionRelationResult.Valid();

            // 查找派系
            Faction requiredFactionInstance = Find.FactionManager.FirstFactionOfDef(SimpleProps.requiredFaction);

            // 检查派系是否存在
            if (SimpleProps.requireFactionExists)
            {
                if (requiredFactionInstance == null || requiredFactionInstance.defeated)
                {
                    if (SimpleProps.debugLogging)
                    {
                        Log.Message($"[SimpleTechnologyTrigger] Required faction {SimpleProps.requiredFaction.defName} does not exist or is defeated");
                    }
                    return FactionRelationResult.Invalid("Faction does not exist or is defeated");
                }
            }

            // 检查派系关系
            if (requiredFactionInstance != null)
            {
                Faction playerFaction = Faction.OfPlayer;
                bool isHostile = requiredFactionInstance.HostileTo(playerFaction);

                if (isHostile)
                {
                    if (SimpleProps.requireNonHostileRelation)
                    {
                        if (SimpleProps.debugLogging)
                        {
                            Log.Message($"[SimpleTechnologyTrigger] Required faction {SimpleProps.requiredFaction.defName} is hostile to player and requireNonHostileRelation is true");
                        }
                        return FactionRelationResult.Invalid("Faction is hostile and requireNonHostileRelation is true");
                    }
                    else
                    {
                        // 不要求非敌对关系，但派系是敌对的 - 这是一个特殊状态
                        if (SimpleProps.debugLogging)
                        {
                            Log.Message($"[SimpleTechnologyTrigger] Required faction {SimpleProps.requiredFaction.defName} is hostile but requireNonHostileRelation is false - allowing with possible alternative");
                        }
                        return FactionRelationResult.HostileButAllowed();
                    }
                }
            }

            if (SimpleProps.debugLogging)
            {
                Log.Message($"[SimpleTechnologyTrigger] Required faction {SimpleProps.requiredFaction.defName} check passed");
            }

            return FactionRelationResult.Valid();
        }

        /// <summary>
        /// 检查是否满足触发周期
        /// </summary>
        private bool PassesIntervalCheck()
        {
            int currentInterval = IntervalsPassed;
            int checkInterval = (int)(SimpleProps.checkIntervalDays * 60);
            
            if (checkInterval <= 0)
                return true;

            return currentInterval % checkInterval == 0;
        }

        /// <summary>
        /// 检查科技条件
        /// </summary>
        private bool PassesTechnologyCheck()
        {
            if (SimpleProps.technology == null)
            {
                Log.Error("[SimpleTechnologyTrigger] No technology specified in props");
                return false;
            }

            bool hasTechnology = SimpleProps.technology.IsFinished;

            if (SimpleProps.debugLogging)
            {
                Log.Message($"[SimpleTechnologyTrigger] Technology {SimpleProps.technology.defName} research status: {hasTechnology}");
            }

            return hasTechnology;
        }

        /// <summary>
        /// 检查派系过滤条件
        /// </summary>
        private bool PassesFactionFilter(IIncidentTarget target)
        {
            if (!SimpleProps.useFactionFilter)
                return true;

            Faction faction = GetTargetFaction(target);
            if (faction == null)
                return false;

            if (SimpleProps.factionTypeBlacklist != null && 
                SimpleProps.factionTypeBlacklist.Contains(faction.def))
            {
                if (SimpleProps.debugLogging)
                {
                    Log.Message($"[SimpleTechnologyTrigger] Faction {faction.def.defName} is in blacklist");
                }
                return false;
            }

            if (SimpleProps.factionTypeWhitelist != null && 
                SimpleProps.factionTypeWhitelist.Count > 0)
            {
                bool inWhitelist = SimpleProps.factionTypeWhitelist.Contains(faction.def);
                
                switch (SimpleProps.defaultBehavior)
                {
                    case FactionFilterDefaultBehavior.Allow:
                        return true;
                    case FactionFilterDefaultBehavior.Deny:
                        if (inWhitelist)
                        {
                            if (SimpleProps.debugLogging)
                            {
                                Log.Message($"[SimpleTechnologyTrigger] Faction {faction.def.defName} is in whitelist");
                            }
                            return true;
                        }
                        else
                        {
                            if (SimpleProps.debugLogging)
                            {
                                Log.Message($"[SimpleTechnologyTrigger] Faction {faction.def.defName} not in whitelist and default behavior is Deny");
                            }
                            return false;
                        }
                }
            }

            switch (SimpleProps.defaultBehavior)
            {
                case FactionFilterDefaultBehavior.Allow:
                    if (SimpleProps.debugLogging)
                    {
                        Log.Message($"[SimpleTechnologyTrigger] No whitelist, default behavior is Allow");
                    }
                    return true;
                case FactionFilterDefaultBehavior.Deny:
                    if (SimpleProps.debugLogging)
                    {
                        Log.Message($"[SimpleTechnologyTrigger] No whitelist, default behavior is Deny");
                    }
                    return false;
                default:
                    return true;
            }
        }

        /// <summary>
        /// 获取目标的派系
        /// </summary>
        private Faction GetTargetFaction(IIncidentTarget target)
        {
            if (target is Map map)
            {
                return map.ParentFaction ?? Faction.OfPlayer;
            }
            else if (target is World world)
            {
                return Faction.OfPlayer;
            }
            else if (target is Caravan caravan)
            {
                return caravan.Faction;
            }
            
            return Faction.OfPlayer;
        }

        /// <summary>
        /// 检查是否存在活跃的相同任务
        /// </summary>
        private bool HasActiveQuest(IncidentDef incident)
        {
            // 如果配置了防止重复，检查是否有相同根源的任务
            if (SimpleProps.preventDuplicateQuests && SimpleProps.questDef != null)
            {
                bool hasActiveQuest = Find.QuestManager.QuestsListForReading.Any((Quest q) => 
                    q.root == SimpleProps.questDef && !q.Historical);

                if (SimpleProps.debugLogging && hasActiveQuest)
                {
                    Log.Message($"[SimpleTechnologyTrigger] Active quest {SimpleProps.questDef.defName} found, skipping trigger");
                }

                return hasActiveQuest;
            }

            return false;
        }

        /// <summary>
        /// 调试方法：显示当前科技触发状态
        /// </summary>
        public string GetSimpleTechnologyTriggerStatus()
        {
            StringBuilder status = new StringBuilder();
            status.AppendLine($"Simple Technology Trigger: {SimpleProps.technology?.defName ?? "NULL"}");
            
            var factionCheck = PassesRequiredFactionCheck();
            status.AppendLine($"Faction Relation Status: {factionCheck.Status}");
            if (!factionCheck.IsValid)
            {
                status.AppendLine($"Faction Relation Reason: {factionCheck.Reason}");
            }
            
            status.AppendLine($"Research Status: {(PassesTechnologyCheck() ? "✅ COMPLETED" : "❌ NOT COMPLETED")}");
            status.AppendLine($"Required Faction: {SimpleProps.requiredFaction?.defName ?? "NONE"}");
            status.AppendLine($"Hostile Alternative: {SimpleProps.incidentIfHostile?.defName ?? "NONE"}");
            status.AppendLine($"Check Interval: {SimpleProps.checkIntervalDays} days");
            status.AppendLine($"Current Interval: {IntervalsPassed}");
            status.AppendLine($"Next Check: {GetNextCheckInterval()} intervals");
            
            bool canTrigger = PassesIntervalCheck() && PassesTechnologyCheck() && factionCheck.IsValid;
            status.AppendLine($"Can Trigger Now: {canTrigger}");

            // 详细派系信息
            if (SimpleProps.requiredFaction != null)
            {
                Faction faction = Find.FactionManager.FirstFactionOfDef(SimpleProps.requiredFaction);
                if (faction != null)
                {
                    status.AppendLine($"Required Faction Status: {(faction.defeated ? "DEFEATED" : "ACTIVE")}");
                    status.AppendLine($"Relation with Player: {(faction.HostileTo(Faction.OfPlayer) ? "HOSTILE" : "NON-HOSTILE")}");
                }
                else
                {
                    status.AppendLine($"Required Faction Status: NOT FOUND IN GAME");
                }
            }

            return status.ToString();
        }

        /// <summary>
        /// 计算下一次检查的间隔
        /// </summary>
        private int GetNextCheckInterval()
        {
            int checkInterval = (int)(SimpleProps.checkIntervalDays * 60);
            if (checkInterval <= 0) return 0;
            
            int currentInterval = IntervalsPassed;
            return ((currentInterval / checkInterval) + 1) * checkInterval;
        }
    }

    /// <summary>
    /// 派系关系检查结果
    /// </summary>
    public struct FactionRelationResult
    {
        public FactionRelationStatus Status { get; }
        public string Reason { get; }
        public bool IsValid => Status == FactionRelationStatus.Valid || Status == FactionRelationStatus.HostileButAllowed;

        public FactionRelationResult(FactionRelationStatus status, string reason = "")
        {
            Status = status;
            Reason = reason;
        }

        public static FactionRelationResult Valid() => new FactionRelationResult(FactionRelationStatus.Valid);
        public static FactionRelationResult HostileButAllowed() => new FactionRelationResult(FactionRelationStatus.HostileButAllowed);
        public static FactionRelationResult Invalid(string reason) => new FactionRelationResult(FactionRelationStatus.Invalid, reason);

        public override string ToString()
        {
            return $"{Status}{(string.IsNullOrEmpty(Reason) ? "" : $" ({Reason})")}";
        }
    }

    /// <summary>
    /// 派系关系状态枚举
    /// </summary>
    public enum FactionRelationStatus
    {
        Valid,              // 关系有效，可以触发原事件
        HostileButAllowed,  // 关系敌对但允许，可以触发替代事件
        Invalid             // 关系无效，不触发任何事件
    }
}
