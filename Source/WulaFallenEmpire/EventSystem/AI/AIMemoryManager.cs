using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RimWorld.Planet;
using Verse;

namespace WulaFallenEmpire.EventSystem.AI
{
    public class AIMemoryManager : WorldComponent
    {
        private const string MemoryFolderName = "WulaAIMemory";
        private const string MemoryVersion = "2.0";
        private const int RecencyTickWindow = 60000;
        private const int SearchCacheTtlSeconds = 45;
        private const int SearchCacheMaxEntries = 256;

        private readonly object _lock = new object();
        private string _saveId;
        private string _loadedStorageKey;
        private List<AIMemoryEntry> _memories = new List<AIMemoryEntry>();
        private bool _loaded;
        private readonly Dictionary<string, SearchCacheEntry> _searchCache = new Dictionary<string, SearchCacheEntry>();
        private readonly Queue<string> _searchCacheOrder = new Queue<string>();

        public AIMemoryManager(World world) : base(world)
        {
        }

        public IReadOnlyList<AIMemoryEntry> GetAllMemories()
        {
            EnsureLoaded();
            lock (_lock)
            {
                return _memories.Select(CloneMemory).ToList();
            }
        }

        public AIMemoryEntry AddMemory(string fact, string category = "misc")
        {
            EnsureLoaded();
            if (string.IsNullOrWhiteSpace(fact)) return null;

            lock (_lock)
            {
                string normalizedCategory = NormalizeCategory(category);
                string hash = AIMemoryEntry.ComputeHash(fact);
                string normalizedFact = NormalizeFact(fact);
                var existing = _memories.FirstOrDefault(m => m != null &&
                    (string.Equals(m.Hash, hash, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(NormalizeFact(m.Fact), normalizedFact, StringComparison.Ordinal)));
                long now = GetCurrentTicks();
                if (existing != null)
                {
                    existing.UpdateFact(fact.Trim());
                    existing.Category = normalizedCategory;
                    existing.UpdatedTicks = now;
                    InvalidateSearchCache();
                    SaveToFileLocked();
                    return CloneMemory(existing);
                }

                var entry = new AIMemoryEntry(fact.Trim(), normalizedCategory)
                {
                    CreatedTicks = now,
                    UpdatedTicks = now,
                    AccessCount = 0
                };
                _memories.Add(entry);
                InvalidateSearchCache();
                SaveToFileLocked();
                return CloneMemory(entry);
            }
        }

        public bool UpdateMemory(string id, string newFact, string category = null)
        {
            EnsureLoaded();
            if (string.IsNullOrWhiteSpace(id)) return false;

            lock (_lock)
            {
                var entry = _memories.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));
                if (entry == null) return false;

                if (!string.IsNullOrWhiteSpace(newFact))
                {
                    entry.UpdateFact(newFact.Trim());
                }

                if (!string.IsNullOrWhiteSpace(category))
                {
                    entry.Category = NormalizeCategory(category);
                }

                entry.UpdatedTicks = GetCurrentTicks();
                InvalidateSearchCache();
                SaveToFileLocked();
                return true;
            }
        }

        public bool DeleteMemory(string id)
        {
            EnsureLoaded();
            if (string.IsNullOrWhiteSpace(id)) return false;

            lock (_lock)
            {
                int removed = _memories.RemoveAll(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));
                if (removed <= 0) return false;

                InvalidateSearchCache();
                SaveToFileLocked();
                return true;
            }
        }

        public List<AIMemoryEntry> SearchMemories(string query, int limit = 5)
        {
            EnsureLoaded();
            if (string.IsNullOrWhiteSpace(query)) return new List<AIMemoryEntry>();

            int safeLimit = Math.Max(1, limit);
            string normalizedQuery = query.Trim();
            string cacheKey = BuildSearchCacheKey(normalizedQuery, safeLimit);
            long now = GetCurrentTicks();

            lock (_lock)
            {
                if (TryGetCachedSearch(cacheKey, out var cached))
                {
                    QueueAccessUpdates(cached.Select(m => m.Id).ToList(), now);
                    return cached.Select(CloneMemory).ToList();
                }
            }

            List<AIMemoryEntry> results;
            lock (_lock)
            {
                List<string> tokens = Tokenize(normalizedQuery);
                var scored = new List<Tuple<AIMemoryEntry, float>>();
                foreach (var entry in _memories)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.Fact)) continue;
                    float score = ComputeScore(entry, normalizedQuery, tokens, now);
                    if (score <= 0f) continue;
                    scored.Add(Tuple.Create(entry, score));
                }

                results = scored
                    .OrderByDescending(s => s.Item2)
                    .ThenByDescending(s => s.Item1.UpdatedTicks)
                    .Take(safeLimit)
                    .Select(s => CloneMemory(s.Item1))
                    .ToList();

                SetCachedSearch(cacheKey, results);
            }

            if (results.Count > 0)
            {
                QueueAccessUpdates(results.Select(m => m.Id).ToList(), now);
            }

            return results;
        }

        public List<AIMemoryEntry> GetRecentMemories(int limit = 5)
        {
            EnsureLoaded();
            lock (_lock)
            {
                return _memories
                    .Where(m => m != null && !string.IsNullOrWhiteSpace(m.Fact))
                    .OrderByDescending(m => m.UpdatedTicks)
                    .ThenByDescending(m => m.CreatedTicks)
                    .Take(Math.Max(1, limit))
                    .Select(CloneMemory)
                    .ToList();
            }
        }

        public void ClearAllMemories()
        {
            EnsureLoaded();
            lock (_lock)
            {
                _memories.Clear();
                InvalidateSearchCache();
                SaveToFileLocked();
            }
        }

        private void EnsureLoaded()
        {
            string storageKey = GetStorageKey();
            if (_loaded && string.Equals(_loadedStorageKey, storageKey, StringComparison.Ordinal)) return;

            lock (_lock)
            {
                storageKey = GetStorageKey();
                if (_loaded && string.Equals(_loadedStorageKey, storageKey, StringComparison.Ordinal)) return;

                LoadFromFileLocked(storageKey);
                _loadedStorageKey = storageKey;
                _loaded = true;
                InvalidateSearchCache();
            }
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

        private string GetFilePath(string storageKey)
        {
            return Path.Combine(GetSaveDirectory(), $"{storageKey}.json");
        }

        private string GetStorageKey()
        {
            EnsureSaveId();

            string saveName = Find.World?.info?.FileNameNoExtension;
            if (!string.IsNullOrWhiteSpace(saveName))
            {
                return SanitizeFileName(saveName) + "_" + _saveId;
            }

            return _saveId;
        }

        private void EnsureSaveId()
        {
            if (string.IsNullOrEmpty(_saveId))
            {
                _saveId = Guid.NewGuid().ToString("N");
            }
        }

        private void LoadFromFileLocked(string storageKey)
        {
            _memories = new List<AIMemoryEntry>();
            string path = GetFilePath(storageKey);
            if (!File.Exists(path)) return;

            try
            {
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return;

                var dto = JsonConvert.DeserializeObject<MemoryFileDto>(json);
                if (dto?.Memories == null) return;

                foreach (var entry in dto.Memories)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.Fact)) continue;
                    entry.Fact = entry.Fact.Trim();
                    entry.Category = NormalizeCategory(entry.Category);
                    entry.Hash = AIMemoryEntry.ComputeHash(entry.Fact);
                    if (string.IsNullOrWhiteSpace(entry.Id))
                    {
                        entry.Id = Guid.NewGuid().ToString("N").Substring(0, 12);
                    }
                    _memories.Add(entry);
                }
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[WulaAI] Failed to load memory file: {ex}");
            }
        }

        private void SaveToFileLocked()
        {
            if (string.IsNullOrWhiteSpace(_loadedStorageKey))
            {
                _loadedStorageKey = GetStorageKey();
            }
            string path = GetFilePath(_loadedStorageKey);
            try
            {
                var dto = new MemoryFileDto
                {
                    Version = MemoryVersion,
                    Memories = _memories
                        .Where(m => m != null && !string.IsNullOrWhiteSpace(m.Fact))
                        .Select(CloneMemory)
                        .ToList()
                };
                string json = JsonConvert.SerializeObject(dto, Formatting.Indented);
                File.WriteAllText(path, json);
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
                _loadedStorageKey = null;
                _loaded = false;
            }
        }

        private void QueueAccessUpdates(List<string> ids, long now)
        {
            if (ids == null || ids.Count == 0) return;
            Task.Run(() =>
            {
                lock (_lock)
                {
                    bool changed = false;
                    foreach (string id in ids)
                    {
                        var entry = _memories.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));
                        if (entry == null) continue;
                        entry.MarkAccessed();
                        entry.UpdatedTicks = now;
                        changed = true;
                    }
                    if (changed)
                    {
                        SaveToFileLocked();
                    }
                }
            });
        }

        private bool TryGetCachedSearch(string cacheKey, out List<AIMemoryEntry> memories)
        {
            memories = null;
            if (!_searchCache.TryGetValue(cacheKey, out var cached))
            {
                return false;
            }
            if ((DateTime.UtcNow - cached.CreatedUtc).TotalSeconds > SearchCacheTtlSeconds)
            {
                _searchCache.Remove(cacheKey);
                return false;
            }
            memories = cached.Memories.Select(CloneMemory).ToList();
            return true;
        }

        private void SetCachedSearch(string cacheKey, List<AIMemoryEntry> memories)
        {
            _searchCache[cacheKey] = new SearchCacheEntry
            {
                CreatedUtc = DateTime.UtcNow,
                Memories = memories.Select(CloneMemory).ToList()
            };
            _searchCacheOrder.Enqueue(cacheKey);
            while (_searchCache.Count > SearchCacheMaxEntries && _searchCacheOrder.Count > 0)
            {
                string oldKey = _searchCacheOrder.Dequeue();
                if (_searchCache.ContainsKey(oldKey) && !string.Equals(oldKey, cacheKey, StringComparison.Ordinal))
                {
                    _searchCache.Remove(oldKey);
                }
            }
        }

        private void InvalidateSearchCache()
        {
            _searchCache.Clear();
            _searchCacheOrder.Clear();
        }

        private static string BuildSearchCacheKey(string query, int limit)
        {
            return limit.ToString(CultureInfo.InvariantCulture) + "|" + NormalizeFact(query);
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "unknown";

            char[] invalid = Path.GetInvalidFileNameChars();
            var chars = value.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray();
            string sanitized = new string(chars).Trim('.');
            return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
        }

        private static long GetCurrentTicks()
        {
            return Find.TickManager?.TicksGame ?? 0;
        }

        private static AIMemoryEntry CloneMemory(AIMemoryEntry entry)
        {
            if (entry == null) return null;
            return new AIMemoryEntry
            {
                Id = entry.Id,
                Fact = entry.Fact,
                Category = entry.Category,
                CreatedTicks = entry.CreatedTicks,
                UpdatedTicks = entry.UpdatedTicks,
                AccessCount = entry.AccessCount,
                Hash = entry.Hash
            };
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
            if (string.IsNullOrWhiteSpace(fact)) return "";
            return string.Join(" ", fact.Trim().ToLowerInvariant().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
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

            var sb = new System.Text.StringBuilder();
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

        private sealed class MemoryFileDto
        {
            [JsonProperty("version")]
            public string Version { get; set; }

            [JsonProperty("memories")]
            public List<AIMemoryEntry> Memories { get; set; }
        }

        private sealed class SearchCacheEntry
        {
            public DateTime CreatedUtc;
            public List<AIMemoryEntry> Memories;
        }
    }
}
