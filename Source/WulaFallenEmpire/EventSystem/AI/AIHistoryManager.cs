using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;
using Newtonsoft.Json;
using RimWorld.Planet;
using Verse;

namespace WulaFallenEmpire.EventSystem.AI
{
    public class AIHistoryManager : WorldComponent
    {
        private string _saveId;
        private string _storageKey;
        private Dictionary<string, List<SavedHistoryEntry>> _cache = new Dictionary<string, List<SavedHistoryEntry>>();

        /// <summary>
        /// One persisted history row: display text plus, for tool rows, the provider tool-call
        /// metadata that lets a reloaded conversation replay as native tool_use/tool_result pairs.
        /// </summary>
        public sealed class SavedHistoryEntry
        {
            public string Role;
            public string Message;
            public string ToolCallId;
            public string ToolName;
            public string ArgsJson;
            public bool IsError;

            public bool HasToolSemantics => !string.IsNullOrWhiteSpace(ToolCallId) && !string.IsNullOrWhiteSpace(ToolName);
        }

        public AIHistoryManager(World world) : base(world)
        {
        }

        private string GetSaveDirectory()
        {
            string path = Path.Combine(GenFilePaths.SaveDataFolderPath, "WulaAIHistoryV2");
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
            return path;
        }

        private string GetFilePath(string eventDefName)
        {
            if (string.IsNullOrWhiteSpace(_storageKey))
            {
                _storageKey = GetStorageKey();
            }
            return Path.Combine(GetSaveDirectory(), $"{_storageKey}_{SanitizeFileName(eventDefName)}.json");
        }

        private void EnsureStorageKeyCurrent()
        {
            string storageKey = GetStorageKey();
            if (string.Equals(_storageKey, storageKey, StringComparison.Ordinal)) return;

            _storageKey = storageKey;
            _cache.Clear();
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

        public List<SavedHistoryEntry> GetHistory(string eventDefName)
        {
            EnsureStorageKeyCurrent();
            if (_cache.TryGetValue(eventDefName, out var cachedHistory))
            {
                var filtered = (cachedHistory ?? new List<SavedHistoryEntry>())
                    .Where(IsPersistableHistoryEntry)
                    .ToList();
                if (filtered.Count != (cachedHistory?.Count ?? 0))
                {
                    _cache[eventDefName] = filtered;
                }
                return filtered;
            }

            string path = GetFilePath(eventDefName);
            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    var dto = JsonConvert.DeserializeObject<List<AIHistoryEntryDto>>(json);
                    var history = dto?
                        .Where(e => e != null && !string.IsNullOrWhiteSpace(e.role))
                        .Select(e => new SavedHistoryEntry
                        {
                            Role = e.role,
                            Message = e.message ?? "",
                            ToolCallId = e.toolCallId,
                            ToolName = e.toolName,
                            ArgsJson = e.argsJson,
                            IsError = e.isError
                        })
                        .Where(IsPersistableHistoryEntry)
                        .ToList();
                    if (history == null) history = new List<SavedHistoryEntry>();
                    _cache[eventDefName] = history;
                    return history;
                }
                catch (Exception ex)
                {
                    WulaLog.Debug($"[WulaFallenEmpire] Failed to load AI history from {path}: {ex}");
                }
            }

            return new List<SavedHistoryEntry>();
        }

        public void SaveHistory(string eventDefName, List<SavedHistoryEntry> history)
        {
            EnsureStorageKeyCurrent();
            var filteredHistory = (history ?? new List<SavedHistoryEntry>())
                .Where(IsPersistableHistoryEntry)
                .ToList();
            _cache[eventDefName] = filteredHistory;
            string path = GetFilePath(eventDefName);
            try
            {
                var dto = filteredHistory
                    .Select(e => new AIHistoryEntryDto
                    {
                        role = e.Role,
                        message = e.Message,
                        toolCallId = e.ToolCallId,
                        toolName = e.ToolName,
                        argsJson = e.ArgsJson,
                        isError = e.IsError
                    })
                    .ToList();
                string json = JsonConvert.SerializeObject(dto, Formatting.Indented);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[WulaFallenEmpire] Failed to save AI history to {path}: {ex}");
            }
        }

        public void ClearHistory(string eventDefName)
        {
            EnsureStorageKeyCurrent();
            _cache.Remove(eventDefName);
            string path = GetFilePath(eventDefName);
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[WulaFallenEmpire] Failed to clear AI history at {path}: {ex}");
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref _saveId, "WulaAIHistoryId");
            
            if (Scribe.mode == LoadSaveMode.PostLoadInit && string.IsNullOrEmpty(_saveId))
            {
                _saveId = Guid.NewGuid().ToString();
                _storageKey = null;
                _cache.Clear();
            }
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "unknown";

            char[] invalid = Path.GetInvalidFileNameChars();
            var chars = value.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray();
            string sanitized = new string(chars).Trim('.');
            return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
        }

        private sealed class AIHistoryEntryDto
        {
            public string role;
            public string message;
            // Tool-call metadata; null on rows written before the replay change — those replay as text.
            public string toolCallId;
            public string toolName;
            public string argsJson;
            public bool isError;
        }

        /// <summary>
        /// Single source of truth for what may be written to persistent history. Shared with
        /// <see cref="AIIntelligenceCore"/> so the in-memory and on-disk views cannot drift.
        /// </summary>
        public static bool IsPersistableHistoryEntry(SavedHistoryEntry entry)
        {
            return !string.Equals((entry.Role ?? "").Trim(), "trace", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Tuple overload kept for <see cref="AIIntelligenceCore"/>'s in-memory list.</summary>
        public static bool IsPersistableHistoryEntry((string role, string message) entry)
        {
            string role = (entry.role ?? "").Trim();
            return !string.Equals(role, "trace", StringComparison.OrdinalIgnoreCase);
        }
    }
}
