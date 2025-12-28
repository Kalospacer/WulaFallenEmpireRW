using System;
using System.Text;
using RimWorld;
using Verse;
using WulaFallenEmpire.EventSystem.AI.UI;

namespace WulaFallenEmpire.EventSystem.AI
{
    /// <summary>
    /// 简化版 AI 自动评论系统
    /// 直接将 Letter 信息发送给 AI 对话流程，让 LLM 自己决定是否回复
    /// </summary>
    public static class AIAutoCommentary
    {
        private static int lastProcessedTick = 0;
        private const int MinTicksBetweenComments = 300; // 5 秒冷却

        public static void ProcessLetter(Letter letter)
        {
            if (letter == null) return;

            // 检查设置
            var settings = WulaFallenEmpireMod.settings;
            if (settings == null || !settings.enableAIAutoCommentary) return;

            // 简单的冷却检查，避免刷屏
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (currentTick - lastProcessedTick < MinTicksBetweenComments) return;
            lastProcessedTick = currentTick;

            // 获取 AI 核心
            var aiCore = Find.World?.GetComponent<AIIntelligenceCore>();
            if (aiCore == null)
            {
                WulaLog.Debug("[AI Commentary] AIIntelligenceCore not found.");
                return;
            }

            // 构建提示词 - 让 AI 自己决定是否需要回复
            string prompt = BuildPrompt(letter);
            
            // 直接发送到正常的 AI 对话流程（会经过完整的思考流程）
            aiCore.SendAutoCommentaryMessage(prompt);
            
            WulaLog.Debug($"[AI Commentary] Sent letter to AI: {letter.Label.Resolve()}");
        }

        private static string BuildPrompt(Letter letter)
        {
            var sb = new StringBuilder();
            
            // 获取 Letter 信息
            string label = letter.Label.Resolve() ?? "Unknown";
            string defName = letter.def?.defName ?? "Unknown";
            
            sb.AppendLine("[游戏事件通知]");
            sb.AppendLine($"事件标题: {label}");
            sb.AppendLine($"事件类型: {defName}");
            sb.AppendLine();
            sb.AppendLine("请根据这个事件决定是否需要向玩家发表简短评论。");
            sb.AppendLine("- 如果是重要事件（如袭击、死亡），可以提供建议或警告");
            sb.AppendLine("- 如果是有趣的事件，可以发表幽默评论");
            sb.AppendLine("- 如果事件不重要或不值得评论，什么都不说即可");
            sb.AppendLine();
            sb.AppendLine("评论要简短（1-2句话），符合你作为帝国AI的人设。");
            
            return sb.ToString();
        }
    }
}
