using System.Globalization;

namespace WulaFallenEmpire.EventSystem.AI
{
    public static class MemoryPrompts
    {
        public const string FactExtractionPrompt =
@"You are extracting long-term memory about the player from the conversation below.
Return JSON only, no extra text.
Schema:
{{""facts"":[{{""text"":""..."",""category"":""preference|personal|plan|colony|misc"",""stability"":""stable|volatile"",""confidence"":0.0}}]}}
Rules:
- Use ONLY User and Assistant final replies. Ignore tool outputs, system messages, and auto-commentary.
- Keep only stable, reusable facts about the player or colony.
- Mark transient details (counts, coordinates, inventories, momentary statuses, or one-off events) as ""volatile"".
- If unsure, set low confidence and mark as ""volatile"".
- Do not invent facts.
Conversation:
{0}";

        public const string MemoryUpdatePrompt =
@"You are updating a memory store.
Given existing memories and new facts, decide ADD, UPDATE, DELETE, or NONE.
Return JSON only, no extra text.
Schema:
{{""memory"":[{{""id"":""..."",""text"":""..."",""category"":""preference|personal|plan|colony|misc"",""event"":""ADD|UPDATE|DELETE|NONE""}}]}}
Rules:
- UPDATE if a new fact refines or corrects an existing memory.
- DELETE if a memory is contradicted by new facts.
- ADD for genuinely new information.
- NONE if no change is needed.
Existing memories (JSON):
{0}
New facts (JSON):
{1}";

        public static string BuildFactExtractionPrompt(string conversation)
        {
            return string.Format(CultureInfo.InvariantCulture, FactExtractionPrompt, conversation ?? "");
        }

        public static string BuildMemoryUpdatePrompt(string existingMemoriesJson, string newFactsJson)
        {
            return string.Format(CultureInfo.InvariantCulture, MemoryUpdatePrompt, existingMemoriesJson ?? "[]", newFactsJson ?? "[]");
        }
    }
}
