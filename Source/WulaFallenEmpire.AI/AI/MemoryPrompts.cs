using System.Globalization;

namespace WulaFallenEmpire.EventSystem.AI
{
    public static class MemoryPrompts
    {
        public const string WindowSummaryPrompt =
@"You are extracting durable long-term memory from a clean conversation window.
Return JSON only, no markdown and no extra text.
Schema:
{{""facts"":[{{""text"":""..."",""category"":""preference|personal|plan|colony|misc"",""confidence"":0.0}}]}}
Rules:
- Use only the provided User and Assistant final replies.
- Keep only stable, reusable facts about the player, colony, durable plans, preferences, or important persistent events.
- Do not store tool results, API errors, raw requests, coordinates, cursor position, selected objects, temporary counts, inventories, momentary map state, or one-off UI context.
- Do not store assistant persona/style/self-description as user memory.
- Do not invent facts. If unsure, omit the fact.
- Use confidence >= 0.75 only when the fact is explicit and durable.
- If there are no durable facts, return {{""facts"":[]}}.
Conversation:
{0}";

        public static string BuildWindowSummaryPrompt(string conversation)
        {
            return string.Format(CultureInfo.InvariantCulture, WindowSummaryPrompt, conversation ?? "");
        }

        /// <summary>
        /// Compaction prompt: condenses history that is about to be dropped into a replacement summary,
        /// so long conversations keep their earlier context instead of losing it (codex compact.rs).
        /// </summary>
        public const string CompactionPrompt =
@"You are compacting a RimWorld AI companion conversation to fit the context budget.
Summarize the conversation below into a compact briefing for the assistant's future self.
Requirements:
- Write in the conversation's language.
- Cover: durable player preferences, promises or plans made, current goals, important game-state facts established by tool results, and the last topic being discussed.
- Be terse (under 400 words). Plain prose or short bullet lines, no JSON, no headers.
- Do not invent facts; omit anything not stated in the conversation.
Conversation to compact:
{0}";

        public static string BuildCompactionPrompt(string conversation)
        {
            return string.Format(CultureInfo.InvariantCulture, CompactionPrompt, conversation ?? "");
        }
    }
}
