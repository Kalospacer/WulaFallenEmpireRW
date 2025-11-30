using RimWorld;
using System.Collections.Generic;
using Verse;

namespace WulaFallenEmpire
{
    public class StorytellerCompProperties_SimpleTechnologyTrigger : StorytellerCompProperties
    {
        // 必需配置
        public ResearchProjectDef technology;           // 要检测的科技
        public IncidentDef incident;                    // 要触发的事件

        // 时间配置
        public float fireAfterDaysPassed = 0f;          // 游戏开始后多少天开始检测
        public float checkIntervalDays = 5f;            // 检查间隔（天）

        // 任务相关配置
        public QuestScriptDef questDef;                 // 关联的任务定义（可选）
        public bool preventDuplicateQuests = true;      // 防止重复任务

        // 派系关系校验
        public FactionDef requiredFaction;              // 必须存在的派系
        public bool requireNonHostileRelation = true;   // 是否要求非敌对关系（默认true）
        public bool requireFactionExists = true;        // 是否要求派系必须存在（默认true）
        
        // 敌对情况下的替代事件 - 新增字段
        public IncidentDef incidentIfHostile;           // 当派系敌对且requireNonHostileRelation为false时触发的事件

        // 调试配置
        public bool debugLogging = false;               // 启用调试日志

        // 派系过滤（可选）
        public List<FactionDef> factionTypeWhitelist;
        public List<FactionDef> factionTypeBlacklist;
        public bool useFactionFilter = false;
        public FactionFilterDefaultBehavior defaultBehavior = FactionFilterDefaultBehavior.Allow;

        public StorytellerCompProperties_SimpleTechnologyTrigger()
        {
            compClass = typeof(StorytellerComp_SimpleTechnologyTrigger);
        }
    }
}
