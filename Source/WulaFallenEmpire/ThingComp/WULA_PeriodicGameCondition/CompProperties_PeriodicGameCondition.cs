using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class CompProperties_PeriodicGameCondition : CompProperties
    {
        public GameConditionDef gameConditionDef;     // 要创建的游戏条件
        public float intervalDays = 1f;              // 触发间隔（天）
        public float durationDays = 1f;              // 游戏条件持续时间（天）
        
        // 条件检查设置
        public bool checkCanCoexist = true;          // 检查是否与其他条件共存
        public bool checkAlreadyActive = true;       // 检查是否已存在相同条件
        public bool checkPreventIncidents = true;    // 检查是否被其他条件阻止
        
        // 通知设置
        public bool sendLetter = true;               // 是否发送信件通知
        public string letterLabel;                   // 信件标题（如为空则使用游戏条件的默认标题）
        public LetterDef letterDef;                  // 信件类型
        
        // 特殊条件
        public bool requireFish = false;             // 是否需要水域有鱼（仅限Odyssey DLC）

        public CompProperties_PeriodicGameCondition()
        {
            compClass = typeof(CompPeriodicGameCondition);
        }
    }
}
