using System.Collections.Generic;
using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class StorytellerComp_SingleOnceFixed_FactionFilter : StorytellerComp_SingleOnceFixed
    {
        private StorytellerCompProperties_SingleOnceFixed_FactionFilter PropsFilter => (StorytellerCompProperties_SingleOnceFixed_FactionFilter)props;

        public override IEnumerable<FiringIncident> MakeIntervalIncidents(IIncidentTarget target)
        {
            // 检查派系过滤条件
            if (!CheckFactionFilter())
            {
                yield break;
            }

            // 调用父类的逻辑
            foreach (var incident in base.MakeIntervalIncidents(target))
            {
                yield return incident;
            }
        }

        private bool CheckFactionFilter()
        {
            if (Faction.OfPlayer == null)
                return false;

            var playerFactionDef = Faction.OfPlayer.def;

            // 优先检查白名单：如果白名单有内容，只有白名单内的派系才能触发
            if (PropsFilter.allowedFactionTypes != null && PropsFilter.allowedFactionTypes.Count > 0)
            {
                return PropsFilter.allowedFactionTypes.Contains(playerFactionDef);
            }

            // 然后检查黑名单：如果黑名单有内容，黑名单内的派系不能触发
            if (PropsFilter.excludedFactionTypes != null && PropsFilter.excludedFactionTypes.Count > 0)
            {
                return !PropsFilter.excludedFactionTypes.Contains(playerFactionDef);
            }

            // 如果既没有白名单也没有黑名单，所有派系都能触发
            return true;
        }
    }

    public class StorytellerCompProperties_SingleOnceFixed_FactionFilter : StorytellerCompProperties_SingleOnceFixed
    {
        // 黑名单：这些派系类型不会触发事件
        public List<FactionDef> excludedFactionTypes;
        
        // 白名单：只有这些派系类型会触发事件（优先级高于黑名单）
        public List<FactionDef> allowedFactionTypes;

        public StorytellerCompProperties_SingleOnceFixed_FactionFilter()
        {
            compClass = typeof(StorytellerComp_SingleOnceFixed_FactionFilter);
        }
    }
}
