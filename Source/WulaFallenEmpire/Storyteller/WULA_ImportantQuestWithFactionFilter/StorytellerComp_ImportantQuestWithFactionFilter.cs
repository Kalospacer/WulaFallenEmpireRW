using RimWorld;
using System.Collections.Generic;
using Verse;
using System.Text;
using System.Linq;
using RimWorld.Planet;

namespace WulaFallenEmpire
{
    public class StorytellerComp_ImportantQuestWithFactionFilter : StorytellerComp
    {
        private StorytellerCompProperties_ImportantQuestWithFactionFilter FilterProps => 
            (StorytellerCompProperties_ImportantQuestWithFactionFilter)props;

        // 重新实现基类的私有属性
        private static int IntervalsPassed => Find.TickManager.TicksGame / 1000;
        
        private bool BeenGivenQuest => Find.QuestManager.QuestsListForReading.Any((Quest q) => q.root == FilterProps.questDef);

        public override IEnumerable<FiringIncident> MakeIntervalIncidents(IIncidentTarget target)
        {
            // 先检查基础条件（天数、是否已给任务等）
            if (IntervalsPassed <= FilterProps.fireAfterDaysPassed * 60 || BeenGivenQuest)
                yield break;

            // 检查派系过滤条件
            if (!PassesFactionFilter(target))
                yield break;

            IncidentDef questIncident = FilterProps.questIncident;
            if (questIncident.TargetAllowed(target))
            {
                yield return new FiringIncident(questIncident, this, GenerateParms(questIncident.category, target));
            }
        }

        /// <summary>
        /// 检查目标是否符合派系过滤条件
        /// </summary>
        private bool PassesFactionFilter(IIncidentTarget target)
        {
            // 如果不启用派系过滤，直接通过
            if (!FilterProps.useFactionFilter)
                return true;

            // 获取目标的派系
            Faction faction = GetTargetFaction(target);
            if (faction == null)
                return false;

            // 检查黑名单
            if (FilterProps.factionTypeBlacklist != null && 
                FilterProps.factionTypeBlacklist.Contains(faction.def))
            {
                return false;
            }

            // 检查白名单
            if (FilterProps.factionTypeWhitelist != null && 
                FilterProps.factionTypeWhitelist.Count > 0)
            {
                bool inWhitelist = FilterProps.factionTypeWhitelist.Contains(faction.def);
                
                switch (FilterProps.defaultBehavior)
                {
                    case FactionFilterDefaultBehavior.Allow:
                        return true;
                        
                    case FactionFilterDefaultBehavior.Deny:
                        // 白名单模式：只有在白名单中才允许
                        if (inWhitelist)
                        {
                            return true;
                        }
                        else
                        {
                            return false;
                        }
                }
            }

            // 如果没有设置白名单，根据默认行为决定
            switch (FilterProps.defaultBehavior)
            {
                case FactionFilterDefaultBehavior.Allow:
                    return true;
                case FactionFilterDefaultBehavior.Deny:
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
        /// 调试方法：显示当前过滤状态
        /// </summary>
        public string GetFactionFilterStatus(IIncidentTarget target)
        {
            if (!FilterProps.useFactionFilter)
                return "Faction filter: DISABLED";

            Faction faction = GetTargetFaction(target);
            if (faction == null)
                return "Faction filter: NO FACTION";

            StringBuilder status = new StringBuilder();
            status.AppendLine($"Faction filter: {faction.def.defName}");
            
            // 黑名单检查
            if (FilterProps.factionTypeBlacklist != null && 
                FilterProps.factionTypeBlacklist.Contains(faction.def))
            {
                status.AppendLine("❌ BLACKLISTED");
                return status.ToString();
            }

            // 白名单检查
            if (FilterProps.factionTypeWhitelist != null && 
                FilterProps.factionTypeWhitelist.Count > 0)
            {
                bool inWhitelist = FilterProps.factionTypeWhitelist.Contains(faction.def);
                
                if (inWhitelist)
                {
                    status.AppendLine("✅ WHITELISTED");
                }
                else
                {
                    status.AppendLine(FilterProps.defaultBehavior == FactionFilterDefaultBehavior.Allow ? 
                        "⚠️ NOT IN WHITELIST (Allowed by default)" : 
                        "❌ NOT IN WHITELIST (Denied by default)");
                }
            }
            else
            {
                status.AppendLine(FilterProps.defaultBehavior == FactionFilterDefaultBehavior.Allow ? 
                    "✅ NO WHITELIST (Allowed by default)" : 
                    "❌ NO WHITELIST (Denied by default)");
            }

            return status.ToString();
        }
    }
}
