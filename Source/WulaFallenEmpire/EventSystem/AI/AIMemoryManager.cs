using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using RimWorld.Planet;
using Verse;

namespace WulaFallenEmpire.EventSystem.AI
{
    public class AIMemoryManager : WorldComponent
    {
        private const string MemoryFolderName = "WulaAIMemory";
        private const string MemoryVersion = "1.0";
        private const int RecencyTickWindow = 60000;
        private string _saveId;
        private List<AIMemoryEntry> _memories = new List<AIMemoryEntry>();
        private bool _loaded;

        public AIMemoryManager(World world) : base(world)
        {
        }

        public IReadOnlyList<AIMemoryEntry> GetAllMemories()
        {
            EnsureLoaded();
            return _memories.ToList();
        }

        public AIMemoryEntry AddMemory(string fact, string category = "misc")
        {
            EnsureLoaded();
            if (string.IsNullOrWhiteSpace(fact)) return null;

            string normalizedCategory = NormalizeCategory(category);
            string hash = AIMemoryEntry.ComputeHash(fact);
            string normalizedFact = NormalizeFact(fact);
            var existing = _memories.FirstOrDefault(m => m != null &&
                (string.Equals(m.Hash, hash, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(NormalizeFact(m.Fact), normalizedFact, StringComparison.Ordinal)));
            long now = GetCurrentTicks();
            if (existing != null)
            {
                existing.UpdateFact(fact);
                existing.Category = normalizedCategory;
                existing.UpdatedTicks = now;
                SaveToFile();
                return existing;
            }

            var entry = new AIMemoryEntry(fact, normalizedCategory)
            {
                CreatedTicks = now,
                UpdatedTicks = now,
                AccessCount = 0
            };
            _memories.Add(entry);
            SaveToFile();
            return entry;
        }

        public bool UpdateMemory(string id, string newFact, string category = null)
        {
            EnsureLoaded();
            if (string.IsNullOrWhiteSpace(id)) return false;

            var entry = _memories.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));
            if (entry == null) return false;

            if (!string.IsNullOrWhiteSpace(newFact))
            {
                entry.UpdateFact(newFact);
            }

            if (!string.IsNullOrWhiteSpace(category))
            {
                entry.Category = NormalizeCategory(category);
            }

            entry.UpdatedTicks = GetCurrentTicks();
            SaveToFile();
            return true;
        }

        public bool DeleteMemory(string id)
        {
            EnsureLoaded();
            if (string.IsNullOrWhiteSpace(id)) return false;

            int removed = _memories.RemoveAll(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));
            if (removed > 0)
            {
                SaveToFile();
                return true;
            }
            return false;
        }

        public List<AIMemoryEntry> SearchMemories(string query, int limit = 5)
        {
            EnsureLoaded();
            if (string.IsNullOrWhiteSpace(query)) return new List<AIMemoryEntry>();

            string normalizedQuery = query.Trim();
            List<string> tokens = Tokenize(normalizedQuery);

            long now = GetCurrentTicks();
            var scored = new List<(AIMemoryEntry entry, float score)>();

            foreach (var entry in _memories)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Fact)) continue;
                float score = ComputeScore(entry, normalizedQuery, tokens, now);
                if (score <= 0f) continue;
                scored.Add((entry, score));
            }

            var results = scored
                .OrderByDescending(s => s.score)
                .ThenByDescending(s => s.entry.UpdatedTicks)
                .Take(Math.Max(1, limit))
                .Select(s => s.entry)
                .ToList();

            if (results.Count > 0)
            {
                foreach (var entry in results)
                {
                    entry.MarkAccessed();
                    entry.UpdatedTicks = now;
                }
                SaveToFile();
            }

            return results;
        }

        public List<AIMemoryEntry> GetRecentMemories(int limit = 5)
        {
            EnsureLoaded();
            return _memories
                .Where(m => m != null && !string.IsNullOrWhiteSpace(m.Fact))
                .OrderByDescending(m => m.UpdatedTicks)
                .ThenByDescending(m => m.CreatedTicks)
                .Take(Math.Max(1, limit))
                .ToList();
        }

        private void EnsureLoaded()
        {
            if (_loaded) return;
            LoadFromFile();
            _loaded = true;
        }

        private string GetSaveDirectory()
        {
            string path = Path.Combine(GenFilePaths.SaveDataFolderPath, MemoryFolderName);
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
            return path;
        }

        private string GetFilePath()
        {
            if (string.IsNullOrEmpty(_saveId))
            {
                _saveId = Guid.NewGuid().ToString("N");
            }
            return Path.Combine(GetSaveDirectory(), $"{_saveId}.json");
        }

        private void LoadFromFile()
        {
            _memories = new List<AIMemoryEntry>();

            string path = GetFilePath();
            if (!File.Exists(path)) return;

            try
            {
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return;

                string array = ExtractJsonArray(json, "memories");
                if (string.IsNullOrWhiteSpace(array)) return;

                foreach (string obj in ExtractJsonObjects(array))
                {
                    var dict = SimpleJsonParser.Parse(obj);
                    if (dict == null || dict.Count == 0) continue;

                    var entry = new AIMemoryEntry();
                    if (dict.TryGetValue("id", out string id) && !string.IsNullOrWhiteSpace(id)) entry.Id = id;
                    if (dict.TryGetValue("fact", out string fact)) entry.Fact = fact;
                    if (dict.TryGetValue("category", out string category)) entry.Category = NormalizeCategory(category);
                    if (dict.TryGetValue("createdTicks", out string created) && long.TryParse(created, NumberStyles.Integer, CultureInfo.InvariantCulture, out long createdTicks)) entry.CreatedTicks = createdTicks;
                    if (dict.TryGetValue("updatedTicks", out string updated) && long.TryParse(updated, NumberStyles.Integer, CultureInfo.InvariantCulture, out long updatedTicks)) entry.UpdatedTicks = updatedTicks;
                    if (dict.TryGetValue("accessCount", out string access) && int.TryParse(access, NumberStyles.Integer, CultureInfo.InvariantCulture, out int accessCount)) entry.AccessCount = accessCount;
                    if (dict.TryGetValue("hash", out string hash)) entry.Hash = hash;
                    if (string.IsNullOrWhiteSpace(entry.Hash))
                    {
                        entry.Hash = AIMemoryEntry.ComputeHash(entry.Fact);
                    }
                    if (string.IsNullOrWhiteSpace(entry.Category)) entry.Category = "misc";
                    _memories.Add(entry);
                }
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[WulaAI] Failed to load memory file: {ex}");
            }
        }

        private void SaveToFile()
        {
            string path = GetFilePath();
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.Append("{");
                sb.Append("\"version\":\"").Append(MemoryVersion).Append("\",");
                sb.Append("\"memories\":[");
                bool first = true;
                foreach (var memory in _memories)
                {
                    if (memory == null) continue;
                    if (!first) sb.Append(",");
                    first = false;
                    sb.Append("{");
                    sb.Append("\"id\":\"").Append(EscapeJson(memory.Id)).Append("\",");
                    sb.Append("\"fact\":\"").Append(EscapeJson(memory.Fact)).Append("\",");
                    sb.Append("\"category\":\"").Append(EscapeJson(memory.Category)).Append("\",");
                    sb.Append("\"createdTicks\":").Append(memory.CreatedTicks.ToString(CultureInfo.InvariantCulture)).Append(",");
                    sb.Append("\"updatedTicks\":").Append(memory.UpdatedTicks.ToString(CultureInfo.InvariantCulture)).Append(",");
                    sb.Append("\"accessCount\":").Append(memory.AccessCount.ToString(CultureInfo.InvariantCulture)).Append(",");
                    sb.Append("\"hash\":\"").Append(EscapeJson(memory.Hash)).Append("\"");
                    sb.Append("}");
                }
                sb.Append("]}");
                File.WriteAllText(path, sb.ToString());
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[WulaAI] Failed to save memory file: {ex}");
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref _saveId, "WulaAIMemoryId");

            if (Scribe.mode == LoadSaveMode.PostLoadInit && string.IsNullOrEmpty(_saveId))
            {
                _saveId = Guid.NewGuid().ToString("N");
                _loaded = false;
            }
        }

        private static long GetCurrentTicks()
        {
            return Find.TickManager?.TicksGame ?? 0;
        }

        private static string NormalizeCategory(string category)
        {
            if (string.IsNullOrWhiteSpace(category)) return "misc";
            string lower = category.Trim().ToLowerInvariant();
            switch (lower)
            {
                case "preference":
                case "personal":
                case "plan":
                case "colony":
                case "misc":
                    return lower;
                default:
                    return "misc";
            }
        }

        private static string NormalizeFact(string fact)
        {
            return string.IsNullOrWhiteSpace(fact) ? "" : fact.Trim().ToLowerInvariant();
        }

        private static float ComputeScore(AIMemoryEntry entry, string query, List<string> tokens, long now)
        {
            string fact = entry.Fact ?? "";
            if (string.IsNullOrWhiteSpace(fact)) return 0f;

            string factLower = fact.ToLowerInvariant();
            string queryLower = query.ToLowerInvariant();

            float score = 0f;
            if (string.Equals(factLower, queryLower, StringComparison.OrdinalIgnoreCase))
            {
                score = 1.2f;
            }
            else if (factLower.Contains(queryLower) || queryLower.Contains(factLower))
            {
                score = 0.9f;
            }

            if (tokens.Count > 0)
            {
                int matches = 0;
                foreach (string token in tokens)
                {
                    if (string.IsNullOrWhiteSpace(token)) continue;
                    if (factLower.Contains(token)) matches++;
                }
                float coverage = matches / (float)Math.Max(1, tokens.Count);
                score = Math.Max(score, 0.3f * coverage);
            }

            long updated = entry.UpdatedTicks > 0 ? entry.UpdatedTicks : entry.CreatedTicks;
            long age = Math.Max(0, now - updated);
            float recency = 1f / (1f + (age / (float)RecencyTickWindow));
            float accessBoost = 1f + Math.Min(0.2f, entry.AccessCount * 0.02f);
            return score * recency * accessBoost;
        }

        private static List<string> Tokenize(string text)
        {
            var tokens = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return tokens;

            var sb = new StringBuilder();
            foreach (char c in text)
            {
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(char.ToLowerInvariant(c));
                }
                else
                {
                    if (sb.Length > 0)
                    {
                        tokens.Add(sb.ToString());
                        sb.Length = 0;
                    }
                }
            }
            if (sb.Length > 0) tokens.Add(sb.ToString());
            return tokens;
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }

        private static string ExtractJsonArray(string json, string key)
        {
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(key)) return null;

            string keyPattern = $"\"{key}\"";
            int keyIndex = json.IndexOf(keyPattern, StringComparison.OrdinalIgnoreCase);
            if (keyIndex == -1) return null;

            int arrayStart = json.IndexOf('[', keyIndex);
            if (arrayStart == -1) return null;

            int arrayEnd = FindMatchingBracket(json, arrayStart);
            if (arrayEnd == -1) return null;

            return json.Substring(arrayStart + 1, arrayEnd - arrayStart - 1);
        }

        private static List<string> ExtractJsonObjects(string arrayContent)
        {
            var objects = new List<string>();
            if (string.IsNullOrWhiteSpace(arrayContent)) return objects;

            int depth = 0;
            int start = -1;
            bool inString = false;
            bool escaped = false;

            for (int i = 0; i < arrayContent.Length; i++)
            {
                char c = arrayContent[i];
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                        continue;
                    }
                    if (c == '\\')
                    {
                        escaped = true;
                        continue;
                    }
                    if (c == '"')
                    {
                        inString = false;
                    }
                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    continue;
                }

                if (c == '{')
                {
                    if (depth == 0) start = i;
                    depth++;
                    continue;
                }
                if (c == '}')
                {
                    depth--;
                    if (depth == 0 && start >= 0)
                    {
                        objects.Add(arrayContent.Substring(start, i - start + 1));
                        start = -1;
                    }
                }
            }

            return objects;
        }

        private static int FindMatchingBracket(string json, int startIndex)
        {
            int depth = 0;
            bool inString = false;
            bool escaped = false;

            for (int i = startIndex; i < json.Length; i++)
            {
                char c = json[i];
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                        continue;
                    }
                    if (c == '\\')
                    {
                        escaped = true;
                        continue;
                    }
                    if (c == '"')
                    {
                        inString = false;
                    }
                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    continue;
                }

                if (c == '[')
                {
                    depth++;
                    continue;
                }

                if (c == ']')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }

            return -1;
        }
    }
}
