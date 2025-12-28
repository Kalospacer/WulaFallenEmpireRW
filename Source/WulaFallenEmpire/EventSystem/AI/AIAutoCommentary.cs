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
            if (letter == null)
            {
                WulaLog.Debug("[AI Commentary] Letter is null, skipping.");
                return;
            }

            WulaLog.Debug($"[AI Commentary] Received letter: {letter.Label.Resolve()}");

            // 检查设置
            var settings = WulaFallenEmpireMod.settings;
            if (settings == null)
            {
                WulaLog.Debug("[AI Commentary] Settings is null, skipping.");
                return;
            }
            
            if (!settings.enableAIAutoCommentary)
            {
                WulaLog.Debug("[AI Commentary] Auto commentary is disabled in settings, skipping.");
                return;
            }

            // 简单的冷却检查，避免刷屏
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (currentTick - lastProcessedTick < MinTicksBetweenComments)
            {
                WulaLog.Debug($"[AI Commentary] Cooldown active ({currentTick - lastProcessedTick} < {MinTicksBetweenComments}), skipping.");
                return;
            }
            lastProcessedTick = currentTick;

            // 获取 AI 核心
            var aiCore = Find.World?.GetComponent<AIIntelligenceCore>();
            if (aiCore == null)
            {
                WulaLog.Debug("[AI Commentary] AIIntelligenceCore not found on World.");
                return;
            }

            // 构建提示词 - 让 AI 自己决定是否需要回复
            string prompt = BuildPrompt(letter);
            
            WulaLog.Debug($"[AI Commentary] Sending to AI: {letter.Label.Resolve()}");
            
            // 直接发送到正常的 AI 对话流程（会经过完整的思考流程）
            aiCore.SendAutoCommentaryMessage(prompt);
            
            WulaLog.Debug($"[AI Commentary] Successfully sent letter to AI: {letter.Label.Resolve()}");
        }

        private static string BuildPrompt(Letter letter)
        {
            var sb = new StringBuilder();
            
            // 获取 Letter 信息
            string label = letter.Label.Resolve() ?? "Unknown";
            string defName = letter.def?.defName ?? "Unknown";
            
            // 获取事件描述（ChoiceLetter 才有 Text 属性）
            string description = "";
            if (letter is ChoiceLetter choiceLetter)
            {
                description = choiceLetter.Text.Resolve() ?? "";
            }
            
            sb.AppendLine("[游戏事件通知 - 自动评论请求]");
            sb.AppendLine($"事件标题: {label}");
            sb.AppendLine($"事件类型: {defName}");
            if (!string.IsNullOrEmpty(description))
            {
                sb.AppendLine($"事件描述: {description}");
            }
            sb.AppendLine();
            sb.AppendLine("请根据这个事件决定是否需要向玩家发表简短评论。");
            sb.AppendLine("- 如果是重要事件（如袭击、死亡），可以提供建议或警告");
            sb.AppendLine("- 如果是有趣的事件，可以发表幽默评论");
            sb.AppendLine("- 如果事件不重要或不值得评论，回复 [NO_COMMENT] 即可跳过");
            sb.AppendLine();
            sb.AppendLine("评论要简短（1-2句话），符合你作为帝国AI的人设。");
            sb.AppendLine("如果你决定不评论，只需回复: [NO_COMMENT]");
            
            return sb.ToString();
        }
    }
}
