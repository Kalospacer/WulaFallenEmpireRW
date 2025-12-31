using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Verse;
using System.Text.RegularExpressions;
using WulaFallenEmpire.EventSystem.AI;
using WulaFallenEmpire.EventSystem.AI.Utils;

namespace WulaFallenEmpire.EventSystem.AI.Tools
{
    public class Tool_GetRecentNotifications : AITool
    {
        public override string Name => "get_recent_notifications";
        public override string Description => "Returns the most recent letters and messages, sorted by in-game time from newest to oldest.";
        public override string UsageSchema =>
            "{\"count\":10,\"includeLetters\":true,\"includeMessages\":true}";
        public override Dictionary<string, object> GetParametersSchema()
        {
            var properties = new Dictionary<string, object>
            {
                ["count"] = SchemaInteger("Max notifications to return.", nullable: true),
                ["includeLetters"] = SchemaBoolean("Include letters.", nullable: true),
                ["includeMessages"] = SchemaBoolean("Include messages.", nullable: true)
            };
            return SchemaObject(properties, RequiredList("count", "includeLetters", "includeMessages"));
        }

        private struct NotificationEntry
        {
            public int Tick;
            public string Kind;
            public string Title;
            public string Body;
        }

        public override string Execute(string args)
        {
            try
            {
                int count = 10;
                bool includeLetters = true;
                bool includeMessages = true;

                var parsed = ParseJsonArgs(args);
                if (TryGetInt(parsed, "count", out int parsedCount)) count = parsedCount;
                if (TryGetBool(parsed, "includeLetters", out bool parsedLetters)) includeLetters = parsedLetters;
                if (TryGetBool(parsed, "includeMessages", out bool parsedMessages)) includeMessages = parsedMessages;

                count = Math.Max(1, Math.Min(100, count));

                int now = Find.TickManager?.TicksGame ?? 0;
                var entries = new List<NotificationEntry>();

                if (includeLetters)
                {
                    entries.AddRange(ReadLetters(now));
                }

                if (includeMessages)
                {
                    entries.AddRange(ReadMessages(now));
                }

                if (entries.Count == 0)
                {
                    return "No recent letters or messages found.";
                }

                var selected = entries
                    .OrderByDescending(e => e.Tick)
                    .ThenByDescending(e => e.Kind)
                    .Take(count)
                    .ToList();

                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"Found {selected.Count} recent notifications (newest -> oldest):");

                int idx = 1;
                foreach (var e in selected)
                {
                    sb.AppendLine($"{idx}. [{e.Kind}] tick={e.Tick}");
                    if (!string.IsNullOrWhiteSpace(e.Title))
                    {
                        sb.AppendLine($"   Title: {TrimForDisplay(e.Title, 140)}");
                    }
                    if (!string.IsNullOrWhiteSpace(e.Body))
                    {
                        sb.AppendLine($"   Text: {TrimForDisplay(e.Body, 600)}");
                    }
                    idx++;
                }

                string toolHistory = BuildToolHistory(count);
                if (!string.IsNullOrWhiteSpace(toolHistory))
                {
                    sb.AppendLine();
                    sb.AppendLine(toolHistory);
                }

                return sb.ToString().TrimEnd();
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }

        private static string BuildToolHistory(int maxCount)
        {
            var core = AIIntelligenceCore.Instance;
            if (core == null) return "AI Tool History: none found.";

            var history = core.GetHistorySnapshot();
            if (history == null || history.Count == 0) return "AI Tool History: none found.";

            var entries = new List<(string ToolJson, string ToolResult)>();
            for (int i = history.Count - 1; i >= 0; i--)
            {
                var entry = history[i];
                if (!string.Equals(entry.role, "tool", StringComparison.OrdinalIgnoreCase)) continue;

                string toolResult = entry.message ?? "";
                for (int j = i - 1; j >= 0; j--)
                {
                    var prev = history[j];
                    if (string.Equals(prev.role, "toolcall", StringComparison.OrdinalIgnoreCase) && IsToolCallJson(prev.message))
                    {
                        entries.Add((prev.message ?? "", toolResult));
                        i = j;
                        break;
                    }
                }

                if (entries.Count >= maxCount) break;
            }

            if (entries.Count == 0) return "AI Tool History: none found.";

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < entries.Count; i++)
            {
                if (i > 0) sb.AppendLine();
                sb.AppendLine(entries[i].ToolJson.Trim());
                sb.AppendLine(entries[i].ToolResult.Trim());
            }

            return sb.ToString().TrimEnd();
        }

        private static bool IsToolCallJson(string response)
        {
            if (string.IsNullOrWhiteSpace(response)) return false;
            return JsonToolCallParser.TryParseToolCalls(response, out _);
        }

        private static IEnumerable<NotificationEntry> ReadLetters(int fallbackNow)
        {
            var list = new List<NotificationEntry>();

            object letterStack = Find.LetterStack;
            if (letterStack == null) return list;

            IEnumerable letters = null;
            try
            {
                letters = GetMemberValue(letterStack, "LettersListForReading", "letters", "lettersList", "lettersListForReading") as IEnumerable;
            }
            catch
            {
                letters = null;
            }

            if (letters == null) return list;

            foreach (var letter in letters)
            {
                if (letter == null) continue;

                int tick = GetInt(letter, "arrivalTick", "receivedTick", "tick", "ticksGame") ?? fallbackNow;
                string label = GetString(letter, "label", "Label", "LabelCap");
                string text = GetString(letter, "text", "Text", "TextString", "LetterText");

                string defName = GetString(GetMemberValue(letter, "def", "Def"), "defName", "DefName");
                string kind = string.IsNullOrWhiteSpace(defName) ? "Letter" : $"Letter:{defName}";

                list.Add(new NotificationEntry
                {
                    Tick = tick,
                    Kind = kind,
                    Title = label,
                    Body = text
                });
            }

            return list;
        }

        private static IEnumerable<NotificationEntry> ReadMessages(int fallbackNow)
        {
            var list = new List<NotificationEntry>();

            IEnumerable messages = null;
            try
            {
                messages = GetMemberValue(typeof(Messages), "MessagesListForReading", "messagesListForReading", "messages") as IEnumerable;
            }
            catch
            {
                messages = null;
            }

            if (messages == null) return list;

            foreach (var message in messages)
            {
                if (message == null) continue;

                int tick = GetInt(message, "time", "timeReceived", "receivedTick", "ticks", "tick", "startTick") ?? fallbackNow;
                string text = GetString(message, "text", "Text", "message", "Message");
                string typeDef = GetString(GetMemberValue(message, "def", "Def", "type", "Type", "messageType", "MessageType"), "defName", "DefName");
                string kind = string.IsNullOrWhiteSpace(typeDef) ? "Message" : $"Message:{typeDef}";

                list.Add(new NotificationEntry
                {
                    Tick = tick,
                    Kind = kind,
                    Title = null,
                    Body = text
                });
            }

            return list;
        }

        private static string TrimForDisplay(string s, int maxChars)
        {
            if (string.IsNullOrEmpty(s)) return s;
            string oneLine = s.Replace("\r", " ").Replace("\n", " ").Trim();
            if (oneLine.Length <= maxChars) return oneLine;
            return oneLine.Substring(0, maxChars) + "...";
        }

        private static object GetMemberValue(object objOrType, params string[] names)
        {
            if (objOrType == null || names == null || names.Length == 0) return null;

            Type t = objOrType as Type ?? objOrType.GetType();
            bool isStatic = objOrType is Type;

            const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.FlattenHierarchy | BindingFlags.Instance | BindingFlags.Static;

            foreach (var name in names)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;

                var prop = t.GetProperty(name, Flags);
                if (prop != null)
                {
                    try
                    {
                        return prop.GetValue(isStatic ? null : objOrType, null);
                    }
                    catch
                    {
                    }
                }

                var field = t.GetField(name, Flags);
                if (field != null)
                {
                    try
                    {
                        return field.GetValue(isStatic ? null : objOrType);
                    }
                    catch
                    {
                    }
                }
            }

            return null;
        }

        private static int? GetInt(object obj, params string[] names)
        {
            object val = GetMemberValue(obj, names);
            if (val == null) return null;
            if (val is int i) return i;
            if (val is long l)
            {
                if (l > int.MaxValue) return int.MaxValue;
                if (l < int.MinValue) return int.MinValue;
                return (int)l;
            }
            if (val is float f) return (int)f;
            if (val is double d) return (int)d;
            if (int.TryParse(val.ToString(), out int parsed)) return parsed;
            return null;
        }

        private static string GetString(object obj, params string[] names)
        {
            object val = GetMemberValue(obj, names);
            if (val == null) return null;
            return val.ToString();
        }
    }
}

