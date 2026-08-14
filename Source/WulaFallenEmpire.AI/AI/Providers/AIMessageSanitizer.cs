using System;
using System.Collections.Generic;
using System.Linq;

namespace WulaFallenEmpire.EventSystem.AI
{
    /// <summary>
    /// Pre-send sanitization of the canonical message list, applied inside each provider's payload
    /// build. Modeled on AstrBot's per-provider sanitize passes (openai_source._sanitize_assistant_messages,
    /// anthropic_source._merge_consecutive_anthropic_messages, gemini append_or_extend): history can be
    /// cut at any row boundary by <c>CompressHistoryIfNeeded</c>, which produces orphaned tool results
    /// (their assistant tool_calls row was dropped) and leading/edge shapes each API rejects with a 400.
    /// Providers call these helpers on <c>request.Messages</c> before converting so a malformed turn
    /// degrades into dropped rows instead of a failed request.
    /// </summary>
    internal static class AIMessageSanitizer
    {
        private static bool IsAssistantWithToolCalls(AIMessage m)
        {
            return m != null &&
                   string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase) &&
                   m.ToolCalls != null && m.ToolCalls.Count > 0;
        }

        private static bool IsTool(AIMessage m)
        {
            return m != null && string.Equals(m.Role, "tool", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsEmpty(string s) => string.IsNullOrWhiteSpace(s);

        /// <summary>
        /// Strips tool_calls from any assistant message whose declared calls are not all answered by
        /// immediately-following tool messages. History truncation (compaction) and cancelled tool loops
        /// can leave an assistant tool_calls turn with no matching tool results, which OpenAI, Anthropic
        /// and Gemini all reject with a 400 ("must be followed by tool messages responding to each
        /// tool_call_id"). Tool messages consumed by the check are left in place.
        /// </summary>
        public static void StripDanglingToolCalls(List<AIMessage> messages)
        {
            if (messages == null) return;
            for (int i = 0; i < messages.Count; i++)
            {
                var m = messages[i];
                if (!IsAssistantWithToolCalls(m)) continue;

                var unanswered = new HashSet<string>(StringComparer.Ordinal);
                foreach (var call in m.ToolCalls)
                {
                    if (!string.IsNullOrWhiteSpace(call?.Id)) unanswered.Add(call.Id);
                }

                int j = i + 1;
                while (j < messages.Count && IsTool(messages[j]))
                {
                    unanswered.Remove(messages[j].ToolCallId ?? string.Empty);
                    j++;
                }

                if (unanswered.Count == 0)
                {
                    i = j - 1; // skip the tool block that answers these calls
                    continue;
                }

                var remaining = new List<AIToolCall>();
                foreach (var call in m.ToolCalls)
                {
                    if (call == null || string.IsNullOrWhiteSpace(call.Id) || !unanswered.Contains(call.Id))
                    {
                        remaining.Add(call);
                    }
                }

                if (remaining.Count == 0 && IsEmpty(m.Content) && IsEmpty(m.ReasoningContent))
                {
                    messages.RemoveAt(i);
                    i--;
                    continue;
                }

                m.ToolCalls = remaining;
                i = j - 1;
            }
        }

        /// <summary>
        /// OpenAI chat-completions rules (AstrBot openai_source.py:452-529):
        /// 1. assistant with no content, no tool calls, no reasoning → drop the row (strict endpoints
        ///    400 on a fully empty assistant; Moonshot/DeepSeek Reasoner among them).
        /// 2. assistant with reasoning but no content/tool calls → content = "" placeholder (reasoning
        ///    history must be kept for reasoning models, but the API demands content or tool_calls).
        /// 3. assistant with tool calls but empty content → Content = null (canonical OpenAI shape;
        ///    ConvertMessage already emits null, this only fixes legacy rows carrying "").
        /// 4. A tool row is kept only when its ToolCallId was declared by the nearest preceding
        ///    assistant tool_calls group (an earlier tool row in the same group is fine); anything else
        ///    is an orphan produced by history truncation and is dropped.
        /// </summary>
        public static List<AIMessage> SanitizeForOpenAI(List<AIMessage> messages)
        {
            if (messages == null) return messages;
            var result = new List<AIMessage>(messages.Count);
            var pendingIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var m in messages)
            {
                if (m == null) continue;
                string role = (m.Role ?? string.Empty).ToLowerInvariant();
                if (role == "assistant")
                {
                    bool hasToolCalls = m.ToolCalls != null && m.ToolCalls.Count > 0;
                    bool hasReasoning = !IsEmpty(m.ReasoningContent);
                    if (IsEmpty(m.Content) && !hasToolCalls)
                    {
                        if (!hasReasoning)
                        {
                            pendingIds.Clear();
                            continue; // rule 1: fully empty assistant row, drop
                        }
                        m.Content = string.Empty; // rule 2: placeholder so reasoning history survives
                    }
                    else if (IsEmpty(m.Content) && hasToolCalls)
                    {
                        m.Content = null; // rule 3: canonical null next to tool_calls
                    }
                    pendingIds.Clear();
                    if (hasToolCalls)
                    {
                        foreach (var call in m.ToolCalls)
                        {
                            if (!string.IsNullOrWhiteSpace(call?.Id)) pendingIds.Add(call.Id);
                        }
                    }
                    result.Add(m);
                    continue;
                }
                if (role == "tool")
                {
                    // rule 4: orphan tool results (their declaring assistant was truncated away) 400.
                    if (pendingIds.Count == 0 || !pendingIds.Contains(m.ToolCallId ?? string.Empty))
                    {
                        continue;
                    }
                    result.Add(m);
                    continue;
                }
                pendingIds.Clear();
                result.Add(m);
            }
            StripDanglingToolCalls(result);
            return result;
        }

        /// <summary>
        /// Anthropic Messages rules (anthropic_source.py:311-433): the API rejects consecutive
        /// same-role turns and tool_result blocks without a matching tool_use. Tool rows carry their
        /// ToolCallId so the orphan check runs BEFORE any merging: each tool row whose id was declared
        /// by the nearest preceding assistant tool_calls group folds into the FOLLOWING user row
        /// (text lands after the tool text, giving "user turn = tool_result blocks + user text",
        /// exactly AstrBot's merged shape); orphan rows are dropped. Adjacent same-role rows are then
        /// merged (text concatenated, tool calls merged).
        /// </summary>
        public static List<AIMessage> SanitizeForAnthropic(List<AIMessage> messages)
        {
            if (messages == null) return messages;
            var prelim = new List<AIMessage>(messages.Count);
            var pendingIds = new HashSet<string>(StringComparer.Ordinal);
            var foldBuffer = new List<AIMessage>(); // verified tool rows awaiting their user turn
            foreach (var m in messages)
            {
                if (m == null) continue;
                string role = (m.Role ?? string.Empty).ToLowerInvariant();
                if (role == "assistant")
                {
                    FoldIntoPreviousUser(prelim, foldBuffer);
                    pendingIds.Clear();
                    if (m.ToolCalls != null)
                    {
                        foreach (var call in m.ToolCalls)
                        {
                            if (!string.IsNullOrWhiteSpace(call?.Id)) pendingIds.Add(call.Id);
                        }
                    }
                    prelim.Add(m);
                    continue;
                }
                if (role == "tool")
                {
                    if (pendingIds.Count == 0 || !pendingIds.Contains(m.ToolCallId ?? string.Empty))
                    {
                        continue; // orphaned tool_result → the API 400s on unmatched ids
                    }
                    foldBuffer.Add(m);
                    continue;
                }
                if (role == "user")
                {
                    if (foldBuffer.Count > 0)
                    {
                        // Tool results become this user turn's leading blocks; the player's text follows.
                        foreach (var toolRow in foldBuffer)
                        {
                            AppendToolText(m, toolRow.Content);
                            if (toolRow.Parts != null && toolRow.Parts.Count > 0)
                            {
                                if (m.Parts == null) m.Parts = new List<AIContentPart>();
                                m.Parts.InsertRange(0, toolRow.Parts);
                            }
                        }
                        foldBuffer.Clear();
                    }
                    pendingIds.Clear();
                    prelim.Add(m);
                    continue;
                }
                // system rows pass through untouched (BuildPayload extracts them into `system`).
                FoldIntoPreviousUser(prelim, foldBuffer);
                pendingIds.Clear();
                prelim.Add(m);
            }
            // Trailing tool rows with no following user turn (tool loop cut short by Stop): keep them
            // as their own rows — the turn ends with tool results, which is legal after an assistant
            // tool_use group.
            prelim.AddRange(foldBuffer);

            // Pass 2: merge adjacent same-role rows (tool rows count as user turns after folding).
            var merged = new List<AIMessage>(prelim.Count);
            foreach (var m in prelim)
            {
                string role = (m.Role ?? string.Empty).ToLowerInvariant();
                if (role == "system")
                {
                    merged.Add(m);
                    continue;
                }
                var prev = merged.Count > 0 ? merged[merged.Count - 1] : null;
                string prevRole = (prev?.Role ?? string.Empty).ToLowerInvariant();
                string effectiveRole = role == "tool" ? "user" : role;
                string effectivePrevRole = prevRole == "tool" ? "user" : prevRole;
                if (prev != null && effectiveRole == effectivePrevRole && (effectiveRole == "user" || effectiveRole == "assistant"))
                {
                    MergeInto(prev, m);
                    continue;
                }
                merged.Add(m);
            }
            return merged;
        }

        /// <summary>
        /// When a tool buffer never meets its user turn (the next real turn is assistant/system),
        /// keep the tool rows as standalone rows in place rather than losing the results.
        /// </summary>
        private static void FoldIntoPreviousUser(List<AIMessage> prelim, List<AIMessage> foldBuffer)
        {
            if (foldBuffer.Count == 0) return;
            prelim.AddRange(foldBuffer);
            foldBuffer.Clear();
        }

        private static void AppendToolText(AIMessage userRow, string toolText)
        {
            if (string.IsNullOrWhiteSpace(toolText)) return;
            userRow.Content = string.IsNullOrWhiteSpace(userRow.Content)
                ? toolText
                : toolText + "\n" + userRow.Content;
        }

        /// <summary>
        /// Gemini generateContent rules (gemini_source.py:292-433): contents must start with a user
        /// turn (a history truncated to a model-leading tail 400s) and tool results replay as
        /// functionResponse parts keyed by tool name — an orphan result has no matching functionCall
        /// and confuses the model, so it is dropped like the other providers' orphans.
        /// </summary>
        public static List<AIMessage> SanitizeForGemini(List<AIMessage> messages)
        {
            if (messages == null) return messages;
            var result = new List<AIMessage>(messages.Count);
            var pendingNames = new HashSet<string>(StringComparer.Ordinal);
            bool sawNonSystem = false;
            foreach (var m in messages)
            {
                if (m == null) continue;
                string role = (m.Role ?? string.Empty).ToLowerInvariant();
                if (role == "system")
                {
                    result.Add(m); // BuildPayload skips these; kept so the skip logic stays there
                    continue;
                }
                if (!sawNonSystem)
                {
                    // First content turn must be user; drop leading assistant rows from a truncated tail.
                    if (role == "assistant")
                    {
                        continue;
                    }
                    sawNonSystem = true;
                }
                if (role == "assistant")
                {
                    pendingNames.Clear();
                    if (m.ToolCalls != null)
                    {
                        foreach (var call in m.ToolCalls)
                        {
                            if (!string.IsNullOrWhiteSpace(call?.Name)) pendingNames.Add(call.Name);
                        }
                    }
                    result.Add(m);
                    continue;
                }
                if (role == "tool")
                {
                    // functionResponse pairs by NAME on Gemini (no call ids), so match on ToolName.
                    string name = !string.IsNullOrWhiteSpace(m.ToolName) ? m.ToolName : m.ToolCallId;
                    if (pendingNames.Count == 0 || !pendingNames.Contains(name ?? string.Empty))
                    {
                        continue;
                    }
                    result.Add(m);
                    continue;
                }
                pendingNames.Clear();
                result.Add(m);
            }
            return result;
        }

        /// <summary>
        /// Folds row <paramref name="next"/> into <paramref name="prev"/> for same-role merging:
        /// text concatenates with a blank line, part lists append, tool call lists append.
        /// </summary>
        private static void MergeInto(AIMessage prev, AIMessage next)
        {
            if (!IsEmpty(prev.Content) && !IsEmpty(next.Content))
            {
                prev.Content = prev.Content + "\n\n" + next.Content;
            }
            else if (!IsEmpty(next.Content))
            {
                prev.Content = next.Content;
            }
            if (next.Parts != null && next.Parts.Count > 0)
            {
                if (prev.Parts == null || prev.Parts.Count == 0)
                {
                    prev.Parts = new List<AIContentPart>(next.Parts);
                }
                else
                {
                    prev.Parts.AddRange(next.Parts);
                }
            }
            if (next.ToolCalls != null && next.ToolCalls.Count > 0)
            {
                if (prev.ToolCalls == null) prev.ToolCalls = new List<AIToolCall>();
                prev.ToolCalls.AddRange(next.ToolCalls);
            }
            if (IsEmpty(prev.ReasoningContent)) prev.ReasoningContent = next.ReasoningContent;
        }
    }
}
