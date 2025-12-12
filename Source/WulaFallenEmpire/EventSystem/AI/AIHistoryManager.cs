using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RimWorld.Planet;
using Verse;

namespace WulaFallenEmpire.EventSystem.AI
{
    public class AIHistoryManager : WorldComponent
    {
        private string _saveId;
        private Dictionary<string, List<ApiMessage>> _cache = new Dictionary<string, List<ApiMessage>>();

        public AIHistoryManager(World world) : base(world)
        {
        }

        private string GetSaveDirectory()
        {
            string path = Path.Combine(GenFilePaths.SaveDataFolderPath, "WulaAIHistory");
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
            return path;
        }

        private string GetFilePath(string eventDefName)
        {
            if (string.IsNullOrEmpty(_saveId))
            {
                _saveId = Guid.NewGuid().ToString();
            }
            return Path.Combine(GetSaveDirectory(), $"{_saveId}_{eventDefName}.json");
        }

        public List<ApiMessage> GetHistory(string eventDefName)
        {
            if (_cache.TryGetValue(eventDefName, out var cachedHistory))
            {
                return cachedHistory;
            }

            string path = GetFilePath(eventDefName);
            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    var history = SimpleJsonParser.Deserialize(json);
                    if (history != null)
                    {
                        _cache[eventDefName] = history;
                        return history;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[WulaFallenEmpire] Failed to load AI history from {path}: {ex}");
                }
            }

            return new List<ApiMessage>();
        }

        public void SaveHistory(string eventDefName, List<ApiMessage> history)
        {
            _cache[eventDefName] = history;
            string path = GetFilePath(eventDefName);
            try
            {
                string json = SimpleJsonParser.Serialize(history);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                Log.Error($"[WulaFallenEmpire] Failed to save AI history to {path}: {ex}");
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref _saveId, "WulaAIHistoryId");
            
            if (Scribe.mode == LoadSaveMode.PostLoadInit && string.IsNullOrEmpty(_saveId))
            {
                _saveId = Guid.NewGuid().ToString();
            }
        }
    }

    public static class SimpleJsonParser
    {
        public static string Serialize(List<ApiMessage> history)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("[");
            for (int i = 0; i < history.Count; i++)
            {
                var item = history[i];
                sb.Append("{");
                sb.Append($"\"role\":\"{Escape(item.role)}\",");
                sb.Append($"\"content\":\"{Escape(item.content)}\"");
                // Note: tool_calls are not serialized for history to keep it simple.
                sb.Append("}");
                if (i < history.Count - 1) sb.Append(",");
            }
            sb.Append("]");
            return sb.ToString();
        }

        public static List<ApiMessage> Deserialize(string json)
        {
            var result = new List<ApiMessage>();
            if (string.IsNullOrEmpty(json)) return result;

            json = json.Trim();
            if (json.StartsWith("[") && json.EndsWith("]"))
            {
                json = json.Substring(1, json.Length - 2);
            }

            if (string.IsNullOrEmpty(json)) return result;
            
            int depth = 0;
            int start = 0;
            for (int i = 0; i < json.Length; i++)
            {
                if (json[i] == '{')
                {
                    if (depth == 0) start = i;
                    depth++;
                }
                else if (json[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        string obj = json.Substring(start, i - start + 1);
                        var parsed = ParseObject(obj);
                        if (parsed.role != null) result.Add(parsed);
                    }
                }
            }

            return result;
        }

        public static Dictionary<string, string> Parse(string json)
        {
            var dict = new Dictionary<string, string>();
            json = json.Trim('{', '}');
            var parts = SplitByComma(json);
            foreach (var part in parts)
            {
                var kv = SplitByColon(part);
                if (kv.Length == 2)
                {
                    string key = Unescape(kv[0].Trim().Trim('"'));
                    string val = Unescape(kv[1].Trim().Trim('"'));
                    dict[key] = val;
                }
            }
            return dict;
        }
        
        public static List<Dictionary<string, string>> ParseArray(string json)
        {
            var list = new List<Dictionary<string, string>>();
            json = json.Trim('[', ']');
            int depth = 0;
            int start = 0;
            for (int i = 0; i < json.Length; i++)
            {
                if (json[i] == '{')
                {
                    if (depth == 0) start = i;
                    depth++;
                }
                else if (json[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        list.Add(Parse(json.Substring(start, i - start + 1)));
                    }
                }
            }
            return list;
        }

        private static ApiMessage ParseObject(string json)
        {
            var msg = new ApiMessage();
            var dict = Parse(json);
            if (dict.TryGetValue("role", out string r)) msg.role = r;
            if (dict.TryGetValue("content", out string c)) msg.content = c;
            return msg;
        }

        private static string[] SplitByComma(string input)
        {
            var list = new List<string>();
            bool inQuote = false;
            int start = 0;
            for (int i = 0; i < input.Length; i++)
            {
                if (input[i] == '"' && (i == 0 || input[i-1] != '\\')) inQuote = !inQuote;
                if (input[i] == ',' && !inQuote)
                {
                    list.Add(input.Substring(start, i - start));
                    start = i + 1;
                }
            }
            list.Add(input.Substring(start));
            return list.ToArray();
        }

        private static string[] SplitByColon(string input)
        {
            bool inQuote = false;
            for (int i = 0; i < input.Length; i++)
            {
                if (input[i] == '"' && (i == 0 || input[i-1] != '\\')) inQuote = !inQuote;
                if (input[i] == ':' && !inQuote)
                {
                    return new[] { input.Substring(0, i), input.Substring(i + 1) };
                }
            }
            return new[] { input };
        }

        private static string Escape(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }

        public static string Unescape(string s) // Changed to public
        {
            if (s == null) return "";
            return s.Replace("\\r", "\r").Replace("\\n", "\n").Replace("\\\"", "\"").Replace("\\\\", "\\");
        }
    }
}