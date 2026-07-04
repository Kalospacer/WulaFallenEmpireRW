using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace WulaFallenEmpire.EventSystem.AI
{
    public static class XmlToolCallParser
    {
        private static readonly Regex ToolCallRegex = new Regex(
            "<tool_call>\\s*(.*?)\\s*</tool_call>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        public static List<AIToolCall> Parse(string text)
        {
            var results = new List<AIToolCall>();
            if (string.IsNullOrWhiteSpace(text)) return results;
            var matches = ToolCallRegex.Matches(text);
            if (matches.Count == 0)
            {
                return text.IndexOf("<tool_call", StringComparison.OrdinalIgnoreCase) >= 0
                    ? new List<AIToolCall> { Invalid("Malformed <tool_call> block.", text) }
                    : results;
            }

            foreach (Match match in matches)
            {
                string payload = match.Groups[1].Value;
                try
                {
                    var obj = JObject.Parse(payload);
                    string name = obj.Value<string>("name");
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    var args = obj["arguments"] as JObject ?? new JObject();
                    results.Add(AIToolCall.Create(obj.Value<string>("id"), name, args));
                }
                catch (Exception ex)
                {
                    results.Add(Invalid(ex.Message, payload));
                }
            }
            return results;
        }

        private static AIToolCall Invalid(string error, string payload)
        {
            return AIToolCall.Create(null, "invalid_xml_tool_call", new JObject
            {
                ["error"] = error ?? "Invalid XML tool call.",
                ["payload"] = payload ?? string.Empty
            });
        }

        public static bool ContainsToolCallBlock(string text)
        {
            return !string.IsNullOrWhiteSpace(text) && ToolCallRegex.IsMatch(text);
        }
    }
}
