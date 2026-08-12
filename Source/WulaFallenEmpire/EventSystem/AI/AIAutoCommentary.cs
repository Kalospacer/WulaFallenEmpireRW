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

            // 构建提示词 - 让 AI 自己决定是否需要回复
            string prompt = BuildPrompt(letter);
            WulaLog.Debug($"[AI Commentary] Sending to AI: {letter.Label.Resolve()}");
            TrySendPrompt(prompt, letter.Label.Resolve());
        }

        public static void ProcessEvent(string eventLabel, string eventDefName, string details)
        {
            if (string.IsNullOrWhiteSpace(eventLabel))
            {
                WulaLog.Debug("[AI Commentary] Event label is empty, skipping.");
                return;
            }

            string prompt = BuildEventPrompt(eventLabel, eventDefName, details);
            WulaLog.Debug($"[AI Commentary] Sending custom event to AI: {eventLabel}");
            TrySendPrompt(prompt, eventLabel);
        }

        private static void TrySendPrompt(string prompt, string sourceLabel)
        {
            if (!AIIntelligenceCore.IsEnabledForCurrentGame())
            {
                WulaLog.Debug("[AI Commentary] AI is disabled for this save, skipping.");
                return;
            }

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

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            // TicksGame restarts near zero on a new colony and jumps backwards when an earlier save is
            // loaded, while this cursor is a process-wide static. Without this guard the subtraction
            // below goes negative and silently suppresses commentary until game time catches up.
            if (currentTick < lastProcessedTick)
            {
                lastProcessedTick = currentTick - MinTicksBetweenComments;
            }
            if (currentTick - lastProcessedTick < MinTicksBetweenComments)
            {
                WulaLog.Debug($"[AI Commentary] Cooldown active ({currentTick - lastProcessedTick} < {MinTicksBetweenComments}), skipping.");
                return;
            }
            lastProcessedTick = currentTick;

            var aiCore = Find.World?.GetComponent<AIIntelligenceCore>();
            if (aiCore == null)
            {
                WulaLog.Debug("[AI Commentary] AIIntelligenceCore not found on World.");
                return;
            }

            aiCore.SendAutoCommentaryMessage(prompt);
            WulaLog.Debug($"[AI Commentary] Successfully sent event to AI: {sourceLabel}");
        }

        private static string BuildPrompt(Letter letter)
        {
            var sb = new StringBuilder();
            
            string label = letter.Label.Resolve() ?? "Unknown";
            string defName = letter.def?.defName ?? "Unknown";
            
            string description = "";
            if (letter is ChoiceLetter choiceLetter)
            {
                description = choiceLetter.Text.Resolve() ?? "";
            }
            
            sb.AppendLine("[游戏事件通知 - 观察者模式]");
            sb.AppendLine($"事件: {label} ({defName})");
            if (!string.IsNullOrEmpty(description)) sb.AppendLine($"详情: {description}");

            sb.AppendLine();
            sb.AppendLine("请根据你当前的人格设定，对该事件发表你的看法。");
            sb.AppendLine("- 保持个性：展现你的人格特征（如语气、态度或口癖）。");
            sb.AppendLine("- 拒绝废话：不要使用‘收到’、‘明白’等无意义的回复。你是在进行评论，而不是在接受指令。");
            sb.AppendLine("- 简短有力：30 字以内，一针见血。");
            sb.AppendLine("- 自主选择：如果这个事件平淡无奇，直接回复 [NO_COMMENT]。");
            sb.AppendLine();
            
            return sb.ToString();
        }

        private static string BuildEventPrompt(string eventLabel, string eventDefName, string details)
        {
            var sb = new StringBuilder();

            sb.AppendLine("[游戏事件通知 - 观察者模式]");
            sb.AppendLine($"事件: {eventLabel} ({(string.IsNullOrWhiteSpace(eventDefName) ? "CustomEvent" : eventDefName)})");
            if (!string.IsNullOrWhiteSpace(details))
            {
                sb.AppendLine("详情:");
                sb.AppendLine(details.Trim());
            }

            sb.AppendLine();
            sb.AppendLine("请根据你当前的人格设定，对该事件发表你的看法。");
            sb.AppendLine("- 保持个性：展现你的人格特征（如语气、态度或口癖）。");
            sb.AppendLine("- 拒绝废话：不要使用‘收到’、‘明白’等无意义的回复。你是在进行评论，而不是在接受指令。");
            sb.AppendLine("- 简短有力：30 字以内，一针见血。");
            sb.AppendLine("- 自主选择：如果这个事件平淡无奇，直接回复 [NO_COMMENT]。");
            sb.AppendLine();

            return sb.ToString();
        }
    }
}
