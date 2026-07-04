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
    }
}
