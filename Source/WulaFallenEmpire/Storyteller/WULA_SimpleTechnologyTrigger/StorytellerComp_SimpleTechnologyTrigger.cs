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

        // 重新实现基类的私有属性
        private static int IntervalsPassed => Find.TickManager.TicksGame / 1000;

        public override IEnumerable<FiringIncident> MakeIntervalIncidents(IIncidentTarget target)
        {
            // 检查基础条件（天数）
            if (IntervalsPassed <= SimpleProps.fireAfterDaysPassed * 60)
                yield break;

            // 检查是否满足周期
            if (!PassesIntervalCheck())
                yield break;

            // 检查派系关系条件 - 新增检查
            if (!PassesRequiredFactionCheck())
                yield break;

            // 检查派系过滤条件
            if (!PassesFactionFilter(target))
                yield break;

            // 检查科技条件
            if (!PassesTechnologyCheck())
                yield break;

            // 检查是否防止重复任务
            if (SimpleProps.preventDuplicateQuests && HasActiveQuest())
                yield break;

            // 触发事件
            IncidentDef techIncident = SimpleProps.incident;
            if (techIncident.TargetAllowed(target))
            {
                if (SimpleProps.debugLogging)
                {
                    Log.Message($"[SimpleTechnologyTrigger] Triggering incident {techIncident.defName} for technology {SimpleProps.technology.defName}");
                }

                yield return new FiringIncident(techIncident, this, GenerateParms(techIncident.category, target));
            }
        }

        /// <summary>
        /// 检查必需派系关系条件 - 新增方法
        /// </summary>
        private bool PassesRequiredFactionCheck()
        {
            // 如果没有配置必需派系，直接通过
            if (SimpleProps.requiredFaction == null)
                return true;

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
                    return false;
                }
            }

            // 检查派系关系
            if (SimpleProps.requireNonHostileRelation && requiredFactionInstance != null)
            {
                Faction playerFaction = Faction.OfPlayer;
                if (requiredFactionInstance.HostileTo(playerFaction))
                {
                    if (SimpleProps.debugLogging)
                    {
                        Log.Message($"[SimpleTechnologyTrigger] Required faction {SimpleProps.requiredFaction.defName} is hostile to player");
                    }
                    return false;
                }
            }

            if (SimpleProps.debugLogging)
            {
                Log.Message($"[SimpleTechnologyTrigger] Required faction {SimpleProps.requiredFaction.defName} check passed");
            }

            return true;
        }

        /// <summary>
        /// 检查是否满足触发周期
        /// </summary>
        private bool PassesIntervalCheck()
        {
            // 简单的周期检查：每 X 天检查一次
            int currentInterval = IntervalsPassed;
            int checkInterval = (int)(SimpleProps.checkIntervalDays * 60);
            
            // 如果检查间隔为0，则每个间隔都检查
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

            // 简单检查科技是否已研究完成
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
            // 如果不启用派系过滤，直接通过
            if (!SimpleProps.useFactionFilter)
                return true;

            // 获取目标的派系
            Faction faction = GetTargetFaction(target);
            if (faction == null)
                return false;

            // 检查黑名单
            if (SimpleProps.factionTypeBlacklist != null && 
                SimpleProps.factionTypeBlacklist.Contains(faction.def))
            {
                if (SimpleProps.debugLogging)
                {
                    Log.Message($"[SimpleTechnologyTrigger] Faction {faction.def.defName} is in blacklist");
                }
                return false;
            }

            // 检查白名单
            if (SimpleProps.factionTypeWhitelist != null && 
                SimpleProps.factionTypeWhitelist.Count > 0)
            {
                bool inWhitelist = SimpleProps.factionTypeWhitelist.Contains(faction.def);
                
                switch (SimpleProps.defaultBehavior)
                {
                    case FactionFilterDefaultBehavior.Allow:
                        // 白名单模式：在白名单中或默认允许
                        if (SimpleProps.debugLogging && !inWhitelist)
                        {
                            Log.Message($"[SimpleTechnologyTrigger] Faction {faction.def.defName} not in whitelist, but default behavior is Allow");
                        }
                        return true;
                    case FactionFilterDefaultBehavior.Deny:
                        // 白名单模式：只有在白名单中才允许
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

            // 如果没有设置白名单，根据默认行为决定
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
        private bool HasActiveQuest()
        {
            if (SimpleProps.questDef == null)
                return false;

            bool hasActiveQuest = Find.QuestManager.QuestsListForReading.Any((Quest q) => 
                q.root == SimpleProps.questDef && !q.Historical);

            if (SimpleProps.debugLogging && hasActiveQuest)
            {
                Log.Message($"[SimpleTechnologyTrigger] Active quest {SimpleProps.questDef.defName} found, skipping trigger");
            }

            return hasActiveQuest;
        }

        /// <summary>
        /// 调试方法：显示当前科技触发状态
        /// </summary>
        public string GetSimpleTechnologyTriggerStatus()
        {
            StringBuilder status = new StringBuilder();
            status.AppendLine($"Simple Technology Trigger: {SimpleProps.technology?.defName ?? "NULL"}");
            status.AppendLine($"Research Status: {(PassesTechnologyCheck() ? "✅ COMPLETED" : "❌ NOT COMPLETED")}");
            status.AppendLine($"Required Faction: {SimpleProps.requiredFaction?.defName ?? "NONE"}");
            status.AppendLine($"Faction Relation: {(PassesRequiredFactionCheck() ? "✅ VALID" : "❌ INVALID")}");
            status.AppendLine($"Check Interval: {SimpleProps.checkIntervalDays} days");
            status.AppendLine($"Current Interval: {IntervalsPassed}");
            status.AppendLine($"Next Check: {GetNextCheckInterval()} intervals");
            status.AppendLine($"Can Trigger Now: {PassesIntervalCheck() && PassesTechnologyCheck() && PassesRequiredFactionCheck()}");

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
}
