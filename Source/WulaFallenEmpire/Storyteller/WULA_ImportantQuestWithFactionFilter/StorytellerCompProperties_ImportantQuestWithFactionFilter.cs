using RimWorld;
using System.Collections.Generic;
using Verse;

namespace WulaFallenEmpire
{
    public class StorytellerCompProperties_ImportantQuestWithFactionFilter : StorytellerCompProperties_ImportantQuest
    {
        // 派系类型白名单 - 只有这些派系类型的殖民地会触发任务
        public List<FactionDef> factionTypeWhitelist;
        
        // 派系类型黑名单 - 这些派系类型的殖民地不会触发任务
        public List<FactionDef> factionTypeBlacklist;
        
        // 是否启用派系过滤
        public bool useFactionFilter = false;
        
        // 默认行为（当派系不在白名单中时的处理方式）
        public FactionFilterDefaultBehavior defaultBehavior = FactionFilterDefaultBehavior.Allow;
        
        public StorytellerCompProperties_ImportantQuestWithFactionFilter()
        {
            compClass = typeof(StorytellerComp_ImportantQuestWithFactionFilter);
        }
    }
    
    // 派系过滤的默认行为枚举
    public enum FactionFilterDefaultBehavior
    {
        Allow,    // 允许不在列表中的派系
        Deny      // 拒绝不在列表中的派系
    }
}
