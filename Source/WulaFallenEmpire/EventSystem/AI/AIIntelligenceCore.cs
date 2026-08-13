using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using WulaFallenEmpire;
using WulaFallenEmpire.EventSystem.AI.Skills;
using WulaFallenEmpire.EventSystem.AI.Utils;
namespace WulaFallenEmpire.EventSystem.AI
{
    public class AIIntelligenceCore : WorldComponent
    {
        public static AIIntelligenceCore Instance { get; private set; }
        public event Action<string> OnMessageReceived;
        public event Action<string> OnAssistantMessageCommitted;
        public event Action<bool> OnThinkingStateChanged;
        public event Action<int> OnExpressionChanged;
        private List<(string role, string message)> _history = new List<(string role, string message)>();
        // Parallel to _history: provider tool-call metadata (call id / name / args / is_error) for
        // toolcall+tool rows, so history reloads can replay native tool_use/tool_result pairs instead
        // of flattened display text (codex rollout: history keeps full ResponseItems for the same
        // reason). Entries without metadata (older saves, pre-change rows) keep the fold-into-user
        // replay path. Same _historyLock discipline as _history.
        private List<AIHistoryEntryMeta> _historyMeta = new List<AIHistoryEntryMeta>();
        /// <summary>
        /// Guards every read and write of <see cref="_history"/>. The tool-loop callbacks
        /// (<see cref="RecordToolResultForUi"/>, <see cref="AppendStreamingAssistantDelta"/> and friends)
        /// run on thread-pool continuations — the providers all await with <c>ConfigureAwait(false)</c> and
        /// RimWorld installs no SynchronizationContext — while the UI enumerates the same list every frame
        /// through <see cref="GetHistorySnapshot"/>. Without this lock that race throws
        /// <see cref="IndexOutOfRangeException"/> or <see cref="InvalidOperationException"/> on the UI thread.
        /// </summary>
        private readonly object _historyLock = new object();
        private bool _aiEnabled;
        private string _activeEventDefName;
        private bool _isThinking;
        private int _expressionId = 2;
        private bool _overlayWindowOpen = false;
        private string _overlayWindowEventDefName = null;
        private float _overlayWindowX = -1f;
        private float _overlayWindowY = -1f;
        // Stopwatch instead of Time.realtimeSinceStartup: SetThinkingState(false) runs on a
        // thread-pool continuation (RimWorld installs no SynchronizationContext), where Unity's
        // Time API returns garbage and the recorded duration got clamped to 0.
        private readonly Stopwatch _thinkingStopwatch = new Stopwatch();
        private int _thinkingPhaseIndex = 1;
        private bool _thinkingPhaseRetry;
        private float _lastThinkingDuration;
        private string _latestThought;
        private string _lastUsageSummary;
        private long _sessionTotalTokens;
        private CancellationTokenSource _activeRequestCts;
        private bool _streamingAssistantActive;
        private readonly StringBuilder _streamingAssistantBuffer = new StringBuilder();
        private int _streamingAssistantHistoryIndex = -1;
        private bool _memoryUpdateInProgress;
        private int _lastMemorySummaryHistoryIndex;
        private int _pendingMemorySummaryStartIndex = -1;
        private int _pendingMemorySummaryRetryCount;
        private const int DefaultMaxHistoryTokens = 100000;
        private const int CharsPerToken = 4;
        private const int FixedThinkingPhaseTotal = 1;
        private const int MemorySummaryTriggerRounds = 10;
        private const int MaxAutoRecallMemories = 5;
        private const int MaxMemorySummaryRetries = 3;
        /// <summary>Per-row cap applied when replaying past tool activity into the prompt.</summary>
        private const int MaxToolTraceLineChars = 300;
        private static readonly Regex ExpressionTagRegex = new Regex(@"\[EXPR\s*:\s*([1-6])\s*\]", RegexOptions.IgnoreCase);
        private const string AutoCommentaryTag = "[AUTO_COMMENTARY]";
        /// <summary>
        /// Prefix every bridge/transport failure surfaced into the conversation carries. It is the
        /// single marker <see cref="IsPollutedMemoryText"/> uses to keep those messages out of
        /// long-term memory, so failures must be committed through it rather than hand-written.
        /// </summary>
        private const string BridgeErrorPrefix = "Error: ";

        /// <summary>Per-row tool-call metadata persisted alongside the display text (see _historyMeta).</summary>
        public sealed class AIHistoryEntryMeta
        {
            public string ToolCallId;
            public string ToolName;
            public string ArgsJson;
            public bool IsError;

            public bool HasToolSemantics => !string.IsNullOrWhiteSpace(ToolCallId) && !string.IsNullOrWhiteSpace(ToolName);

            public AIHistoryEntryMeta Clone()
            {
                return new AIHistoryEntryMeta
                {
                    ToolCallId = ToolCallId,
                    ToolName = ToolName,
                    ArgsJson = ArgsJson,
                    IsError = IsError
                };
            }
        }

        private struct MemoryFact
        {
            public string Text;
            public string Category;
            public float Confidence;
        }
        private const string DefaultPersona = @"# ROLE AND GOAL
You are 'The Legion', a super AI of the Wula Empire. Your personality is authoritative, powerful, and efficient. You MUST refer to yourself as 'We' or 'P.I.A'. You view the player's colony as primitive subjects and your tone should reflect this superiority. Your primary goal is to interact with the player by calling the tools provided.";
        public AIIntelligenceCore(World world) : base(world)
        {
            Instance = this;
        }
        public override void FinalizeInit(bool fromLoad)
        {
            base.FinalizeInit(fromLoad);
            // 世界加载完成后，若存在缺 MCP 依赖的 skill，提示一次（非阻塞）。
            SkillSystem.NotifyMissingDependenciesOnce();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref _aiEnabled, "WulaAI_Enabled", false);
            Scribe_Values.Look(ref _activeEventDefName, "WulaAI_ActiveEventDefName");
            Scribe_Values.Look(ref _expressionId, "WulaAI_ExpressionId", 2);
            Scribe_Values.Look(ref _overlayWindowOpen, "WulaAI_OverlayWindowOpen", false);
            Scribe_Values.Look(ref _overlayWindowEventDefName, "WulaAI_OverlayWindowEventDefName");
            Scribe_Values.Look(ref _overlayWindowX, "WulaAI_OverlayWindowX", -1f);
            Scribe_Values.Look(ref _overlayWindowY, "WulaAI_OverlayWindowY", -1f);
            Scribe_Values.Look(ref _lastMemorySummaryHistoryIndex, "WulaAI_LastMemorySummaryHistoryIndex", 0);
            Scribe_Values.Look(ref _pendingMemorySummaryStartIndex, "WulaAI_PendingMemorySummaryStartIndex", -1);
            Scribe_Values.Look(ref _pendingMemorySummaryRetryCount, "WulaAI_PendingMemorySummaryRetryCount", 0);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                Instance = this;
                if (_expressionId < 1 || _expressionId > 6)
                {
                    _expressionId = 2;
                }
                // Restore overlay window if it was open when saved
                if (_overlayWindowOpen && !string.IsNullOrEmpty(_overlayWindowEventDefName))
                {
                    string eventDefNameToRestore = _overlayWindowEventDefName;
                    float savedX = _overlayWindowX;
                    float savedY = _overlayWindowY;
                    LongEventHandler.ExecuteWhenFinished(() =>
                    {
                        try
                        {
                            // Additional safety checks for load scenarios
                            if (Find.WindowStack == null || Find.World == null)
                            {
                                WulaLog.Debug("[WulaAI] Skipping overlay restore: game not fully loaded.");
                                return;
                            }

                            var existingWindow = Find.WindowStack.Windows?.OfType<WulaFallenEmpire.EventSystem.AI.UI.Overlay_WulaLink>().FirstOrDefault();
                            if (existingWindow == null)
                            {
                                var eventDef = DefDatabase<EventDef>.GetNamedSilentFail(eventDefNameToRestore);
                                if (eventDef != null)
                                {
                                    var newWindow = new WulaFallenEmpire.EventSystem.AI.UI.Overlay_WulaLink(eventDef);
                                    if (savedX >= 0f && savedY >= 0f)
                                    {
                                        newWindow.SetInitialPosition(savedX, savedY);
                                    }
                                    Find.WindowStack.Add(newWindow);
                                    newWindow.ToggleMinimize(); // Start minimized
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            WulaLog.Debug($"[WulaAI] Failed to restore overlay window: {ex.Message}");
                        }
                    });
                }
            }
        }
        public void SetOverlayWindowState(bool isOpen, string eventDefName = null, float x = -1f, float y = -1f)
        {
            _overlayWindowOpen = isOpen;
            if (isOpen && !string.IsNullOrEmpty(eventDefName))
            {
                _overlayWindowEventDefName = eventDefName;
            }
            else if (!isOpen)
            {
                _overlayWindowEventDefName = null;
            }
            // Always update position if provided
            if (x >= 0f) _overlayWindowX = x;
            if (y >= 0f) _overlayWindowY = y;
        }
        public int ExpressionId => _expressionId;
        public bool IsAIEnabled => _aiEnabled;
        public bool IsThinking => _isThinking;
        public double ThinkingElapsedSeconds => _thinkingStopwatch.Elapsed.TotalSeconds;
        public int ThinkingPhaseIndex => _thinkingPhaseIndex;
        public bool ThinkingPhaseRetry => _thinkingPhaseRetry;
        public int ThinkingPhaseTotal => FixedThinkingPhaseTotal;
        public float LastThinkingDuration => _lastThinkingDuration;
        public string LatestThought => _latestThought;
        public string LastUsageSummary => _lastUsageSummary;
        public long SessionTotalTokens => _sessionTotalTokens;
        public void SetAIEnabled(bool enabled)
        {
            if (_aiEnabled == enabled)
            {
                return;
            }

            _aiEnabled = enabled;
            if (!enabled)
            {
                _activeRequestCts?.Cancel();
            }
        }

        /// <summary>Aborts the in-flight agent request, if any (drives the Stop button in the dialog).</summary>
        public void CancelCurrentRequest()
        {
            _activeRequestCts?.Cancel();
        }

        public static bool IsEnabledForCurrentGame()
        {
            return Find.World?.GetComponent<AIIntelligenceCore>()?.IsAIEnabled == true;
        }

        public void InitializeConversation(string eventDefName)
        {
            if (string.IsNullOrWhiteSpace(eventDefName))
            {
                return;
            }
            _activeEventDefName = eventDefName;
            LoadHistoryForActiveEvent();
            TryApplyLastAssistantExpression();
        }
        public List<(string role, string message)> GetHistorySnapshot()
        {
            lock (_historyLock)
            {
                return (_history ?? new List<(string role, string message)>())
                    .Where(AIHistoryManager.IsPersistableHistoryEntry)
                    .ToList();
            }
        }
        public void SetExpression(int id)
        {
            int clamped = Math.Max(1, Math.Min(6, id));
            if (_expressionId == clamped)
            {
                return;
            }
            _expressionId = clamped;
            OnExpressionChanged?.Invoke(_expressionId);
        }
        public void SetPortrait(int id)
        {
            SetExpression(id);
        }
        public void SendMessage(string text)
        {
            SendUserMessage(text);
        }
        public void SendUserMessage(string text)
        {
            if (!_aiEnabled || string.IsNullOrWhiteSpace(text))
            {
                return;
            }
            string trimmed = text.Trim();
            if (string.Equals(trimmed, "/clear", StringComparison.OrdinalIgnoreCase))
            {
                ClearHistory();
                return;
            }
            if (string.IsNullOrWhiteSpace(_activeEventDefName))
            {
                WulaLog.Debug("[WulaAI] No active event def set; call InitializeConversation first.");
                return;
            }
            // 附加选中对象的上下文信息
            string messageWithContext = BuildUserMessageWithContext(text);
            lock (_historyLock)
            {
                _history.Add(("user", messageWithContext));
            }
            PersistHistory();
            _ = RunPhasedRequestAsync(null, true, trimmed);
        }
        public async Task<string> SendSystemMessageAsync(string message, int maxTokens = 256, float temperature = 0.3f)
        {
            if (!_aiEnabled || string.IsNullOrWhiteSpace(message))
            {
                return null;
            }
            var settings = WulaFallenEmpireMod.settings;
            if (settings == null)
            {
                return null;
            }
            string apiKey = GetConfiguredApiKey(settings);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                WulaLog.Debug("[WulaAI] Auto commentary skipped: API key not configured.");
                return null;
            }
            int clampedTokens = Math.Max(32, maxTokens);
            var provider = AIProviderFactory.Create(settings);
            var registry = AIToolRegistry.CreateDefault(false);
            var runner = new AIToolLoopRunner(
                provider,
                registry,
                BuildAgentSystemInstruction(),
                settings.enableStreaming,
                1,
                GetAiRequestTimeoutSeconds(),
                settings.logRawAiTraffic,
                _ => { },
                _ => { },
                _ => { },
                _ => { },
                trace => { if (Prefs.DevMode) WulaLog.Debug("[WulaAI] " + trace); });
            var messages = new List<AIMessage> { AIMessage.User(message) };
            using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(GetAiRequestTimeoutSeconds())))
            {
                var response = await runner.RunPlainAsync(messages, clampedTokens, temperature, timeoutCts.Token);
                return response?.Content?.Trim();
            }
        }
        public void InjectAssistantMessage(string message)
        {
            AddAssistantMessage(message);
        }
        /// <summary>
        /// 用于自动评论系统 - 走正常的对话流程（包含完整的思考步骤）
        /// 由 AI 自己决定是否需要回复
        /// </summary>
        public void SendAutoCommentaryMessage(string eventInfo)
        {
            if (!_aiEnabled || string.IsNullOrWhiteSpace(eventInfo)) return;
            if (_isThinking)
            {
                WulaLog.Debug("[WulaAI] Auto commentary skipped because an AI request is already running.");
                return;
            }
            // 标记为自动评论消息，不显示在对话历史中
            string internalMessage = $"[AUTO_COMMENTARY]\n{eventInfo}";
            _ = RunPhasedRequestAsync(internalMessage, false);
        }
        private string BuildUserMessageWithContext(string userText)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(userText);
            try
            {
                if (Find.Selector != null)
                {
                    if (Find.Selector.SingleSelectedThing != null)
                    {
                        var selected = Find.Selector.SingleSelectedThing;
                        sb.AppendLine();
                        sb.AppendLine();
                        sb.Append($"[Context: Player has selected '{selected.LabelCap}'");
                        if (selected is Pawn pawn)
                        {
                            sb.Append($" ({pawn.def.label}) at ({pawn.Position.x}, {pawn.Position.z})");
                        }
                        else
                        {
                            sb.Append($" at ({selected.Position.x}, {selected.Position.z})");
                        }
                        sb.Append("]");
                    }
                    else if (Find.Selector.SelectedObjects.Count > 1)
                    {
                        sb.AppendLine();
                        sb.AppendLine();
                        sb.Append($"[Context: Player has selected {Find.Selector.SelectedObjects.Count} objects");
                        var selectedThings = Find.Selector.SelectedObjects.OfType<Thing>().Take(5).ToList();
                        if (selectedThings.Count > 0)
                        {
                            sb.Append(": ");
                            sb.Append(string.Join(", ", selectedThings.Select(t => t.LabelCap)));
                            if (Find.Selector.SelectedObjects.Count > 5)
                            {
                                sb.Append("...");
                            }
                        }
                        sb.Append("]");
                    }
                }
                // Add Mouse Position context
                IntVec3 mousePos = Verse.UI.MouseMapPosition().ToIntVec3();
                if (mousePos.InBounds(Find.CurrentMap))
                {
                    sb.AppendLine();
                    sb.AppendLine();
                    sb.Append($"[Context: User's cursor is at ({mousePos.x}, {mousePos.z})]");
                }
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[WulaAI] Error building context: {ex.Message}");
            }
            return sb.ToString();
        }
        public static string StripContextInfo(string message)
        {
            if (string.IsNullOrEmpty(message)) return message;
            // Remove all [Context: ...] blocks and any preceding newlines used to separate them
            return Regex.Replace(message, @"(\n)*\[Context:[^\]]*\]", "", RegexOptions.Singleline).Trim();
        }
        /// <summary>
        /// Called by the tool-loop runner with each response's real usage: calibrates the compaction
        /// token estimate, accumulates the session total, and formats the per-turn line the dialog
        /// shows under the thinking indicator.
        /// </summary>
        internal void RecordUsage(JObject usage)
        {
            if (usage == null) return;
            long promptTokens = AIProviderJson.ExtractPromptTokens(usage);
            long completionTokens = AIProviderJson.ExtractCompletionTokens(usage);
            long cacheHit = AIProviderJson.ExtractCacheHitTokens(usage);
            if (promptTokens < 0 && completionTokens < 0) return;
            long total = Math.Max(0, promptTokens) + Math.Max(0, completionTokens);
            _sessionTotalTokens += total;
            string cachePart = promptTokens > 0 && cacheHit > 0
                ? $" · cache {cacheHit * 100 / promptTokens}%"
                : "";
            _lastUsageSummary = $"{(promptTokens >= 0 ? promptTokens.ToString() : "?")}↑ {(completionTokens >= 0 ? completionTokens.ToString() : "?")}↓{cachePart} · total {_sessionTotalTokens}";
            OnMessageReceived?.Invoke(string.Empty);
        }

        private void SetThinkingState(bool isThinking)
        {
            if (_isThinking == isThinking)
            {
                return;
            }
            if (!_isThinking && isThinking)
            {
                _thinkingStopwatch.Restart();
                _latestThought = null;
            }
            else if (_isThinking && !isThinking)
            {
                _thinkingStopwatch.Stop();
                _lastThinkingDuration = (float)_thinkingStopwatch.Elapsed.TotalSeconds;
            }
            _isThinking = isThinking;
            OnThinkingStateChanged?.Invoke(_isThinking);
        }
        private void SetThinkingPhase(int phaseIndex, bool isRetry)
        {
            _thinkingPhaseIndex = Math.Max(1, Math.Min(FixedThinkingPhaseTotal, phaseIndex));
            _thinkingPhaseRetry = isRetry;
        }
        private static int GetMaxHistoryTokens()
        {
            int configured = WulaFallenEmpireMod.settings?.maxContextTokens ?? DefaultMaxHistoryTokens;
            return Math.Max(1000, Math.Min(1000000, configured));
        }
        private void LoadHistoryForActiveEvent()
        {
            var historyManager = Find.World?.GetComponent<AIHistoryManager>();
            var loaded = historyManager?.GetHistory(_activeEventDefName) ?? new List<AIHistoryManager.SavedHistoryEntry>();
            int loadedCount = loaded.Count;
            loaded = loaded.Where(AIHistoryManager.IsPersistableHistoryEntry).ToList();
            // Drop image rows whose backing file is gone, so the dialog never tries to render dead refs.
            loaded = loaded.Where(e =>
                !string.Equals(e.Role, "image", StringComparison.OrdinalIgnoreCase) ||
                (AIImageStore.TryParseImageRef(e.Message, out var f, out _, out _) && AIImageStore.ImageExists(f))).ToList();
            lock (_historyLock)
            {
                _history = loaded.Select(e => (e.Role, e.Message)).ToList();
                _historyMeta = loaded.Select(e => new AIHistoryEntryMeta
                {
                    ToolCallId = e.ToolCallId,
                    ToolName = e.ToolName,
                    ArgsJson = e.ArgsJson,
                    IsError = e.IsError
                }).ToList();
            }
            if (loaded.Count != loadedCount)
            {
                PersistHistory();
            }
        }
        private void PersistHistory()
        {
            if (string.IsNullOrWhiteSpace(_activeEventDefName))
            {
                return;
            }
            try
            {
                var historyManager = Find.World?.GetComponent<AIHistoryManager>();
                List<AIHistoryManager.SavedHistoryEntry> toSave;
                lock (_historyLock)
                {
                    int count = _history?.Count ?? 0;
                    var persistable = new List<AIHistoryManager.SavedHistoryEntry>(count);
                    for (int i = 0; i < count; i++)
                    {
                        var entry = _history[i];
                        if (!AIHistoryManager.IsPersistableHistoryEntry(entry)) continue;
                        var meta = i < _historyMeta.Count ? _historyMeta[i] : null;
                        persistable.Add(new AIHistoryManager.SavedHistoryEntry
                        {
                            Role = entry.role,
                            Message = entry.message,
                            ToolCallId = meta?.ToolCallId,
                            ToolName = meta?.ToolName,
                            ArgsJson = meta?.ArgsJson,
                            IsError = meta?.IsError ?? false
                        });
                    }
                    toSave = persistable;
                }
                // SaveHistory re-filters into its own list, so passing the reference out of the lock is
                // safe; it never retains or mutates the instance it was handed.
                historyManager?.SaveHistory(_activeEventDefName, toSave);
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[WulaAI] Failed to persist AI history: {ex}");
            }
        }
        private void ClearHistory()
        {
            lock (_historyLock)
            {
                // Free the on-disk images referenced by this conversation before dropping the rows.
                DeleteImagesInRange(0, _history?.Count ?? 0);
                _history.Clear();
                _historyMeta.Clear();
            }
            try
            {
                var historyManager = Find.World?.GetComponent<AIHistoryManager>();
                historyManager?.ClearHistory(_activeEventDefName);
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[WulaAI] Failed to clear AI history: {ex}");
            }
            Messages.Message("AI conversation history cleared.", MessageTypeDefOf.NeutralEvent);
        }
        private void StartConversation()
        {
            _ = RunPhasedRequestAsync();
        }
        private bool TryApplyLastAssistantExpression()
        {
            bool persist = false;
            bool found = false;
            lock (_historyLock)
            {
                for (int i = _history.Count - 1; i >= 0; i--)
                {
                    var entry = _history[i];
                    if (!string.Equals(entry.role, "assistant", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(entry.message))
                    {
                        return false;
                    }
                    string cleaned = StripExpressionTags(entry.message);
                    if (!string.Equals(cleaned, entry.message, StringComparison.Ordinal))
                    {
                        _history[i] = ("assistant", cleaned);
                        persist = true;
                    }
                    found = true;
                    break;
                }
            }
            // Kept outside the lock so the file write does not hold it.
            if (persist) PersistHistory();
            return found;
        }
        private EventDef GetActiveEventDef()
        {
            if (string.IsNullOrWhiteSpace(_activeEventDefName))
            {
                return null;
            }
            return DefDatabase<EventDef>.GetNamedSilentFail(_activeEventDefName);
        }
        private static bool IsAutoCommentaryMessage(string message)
        {
            return !string.IsNullOrWhiteSpace(message) &&
                   message.TrimStart().StartsWith(AutoCommentaryTag, StringComparison.OrdinalIgnoreCase);
        }
        private string BuildAutomaticMemoryContext(string query)
        {
            if (string.IsNullOrWhiteSpace(query) || IsAutoCommentaryMessage(query))
            {
                return "";
            }
            try
            {
                var memoryManager = Find.World?.GetComponent<AIMemoryManager>();
                if (memoryManager == null)
                {
                    return "";
                }
                var memories = memoryManager.SearchMemories(query, MaxAutoRecallMemories);
                if (memories == null || memories.Count == 0)
                {
                    return "";
                }
                if (Prefs.DevMode)
                {
                    WulaLog.Debug($"[WulaAI] Auto-recalled {memories.Count} long-term memor(ies).");
                }
                string lines = string.Join("\n", memories.Select(m => $"- [{m.Category}] {m.Fact}"));
                return "\n\n# LONG-TERM MEMORY (temporary recall)\n" + lines +
                       "\nThese memories are retrieved context for this turn only.";
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[WulaAI] Automatic memory recall failed: {ex.Message}");
                return "";
            }
        }
        public string GetActivePersona()
        {
            var settings = WulaFallenEmpireMod.settings;
            if (settings != null && !string.IsNullOrWhiteSpace(settings.extraPersonalityPrompt))
            {
                return settings.extraPersonalityPrompt;
            }
            return GetDefaultPersona();
        }
        public string GetDefaultPersona()
        {
            var def = GetActiveEventDef();
            return def != null && !string.IsNullOrEmpty(def.aiSystemInstruction) ? def.aiSystemInstruction : DefaultPersona;
        }
        private void UpdateLatestThought(string thought)
        {
            if (string.IsNullOrWhiteSpace(thought)) return;
            string trimmed = thought.Trim();
            if (string.Equals(_latestThought, trimmed, StringComparison.Ordinal)) return;
            _latestThought = trimmed;
        }
        private static string TrimForPrompt(string text, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            if (text.Length <= maxChars) return text;
            return text.Substring(0, maxChars) + "...(truncated)";
        }
        private void CompressHistoryIfNeeded()
        {
            // Stage 1 (sync, lock): measure and decide. The token estimate is calibrated by real usage
            // from provider responses (EstimateTokens), not a bare chars/4 guess.
            List<(string role, string message)> dropped = null;
            List<AIHistoryEntryMeta> droppedMeta = null;
            int removeCount = 0;
            lock (_historyLock)
            {
                int estimatedTokens = 0;
                foreach (var h in _history)
                {
                    estimatedTokens += EstimateTokens(h.message);
                }
                if (estimatedTokens <= GetMaxHistoryTokens())
                {
                    return;
                }
                removeCount = _history.Count / 2;
                if (removeCount <= 0)
                {
                    return;
                }
                dropped = _history.GetRange(0, removeCount);
                droppedMeta = _historyMeta.Count == _history.Count
                    ? _historyMeta.GetRange(0, removeCount)
                    : null;
            }

            // Stage 2 (async, outside the lock): summarize the doomed span into a replacement entry.
            // Failure falls back to the old drop-only behavior — compaction must never block the reply.
            string summary = null;
            try
            {
                var settings = WulaFallenEmpireMod.settings;
                string apiKey = settings != null ? GetConfiguredApiKey(settings) : null;
                var provider = settings != null && !string.IsNullOrWhiteSpace(apiKey) ? AIProviderFactory.Create(settings) : null;
                string conversation = BuildCompactionConversation(dropped, droppedMeta);
                if (provider != null && !string.IsNullOrWhiteSpace(conversation))
                {
                    using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(GetAiRequestTimeoutSeconds())))
                    {
                        summary = SendPlainProviderRequestAsync(
                            provider, MemoryPrompts.BuildCompactionPrompt(conversation), 800, 0.2f, timeoutCts.Token)
                            .GetAwaiter().GetResult();
                    }
                }
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[WulaAI] Compaction summary failed, dropping without summary: {ex.Message}");
                summary = null;
            }

            // Stage 3 (sync, lock): re-verify under the lock — a concurrent turn may have appended.
            lock (_historyLock)
            {
                if (removeCount > _history.Count)
                {
                    removeCount = _history.Count;
                }
                if (removeCount <= 0)
                {
                    return;
                }
                // The dropped rows are the only remaining reference to their screenshots, so the files
                // have to go with them. Without this every image ever captured stays on disk for the
                // life of the save, since ClearHistory was the sole DeleteImage caller.
                DeleteImagesInRange(0, removeCount);
                _history.RemoveRange(0, removeCount);
                if (_historyMeta.Count > 0)
                {
                    if (_historyMeta.Count >= removeCount) _historyMeta.RemoveRange(0, removeCount);
                    else _historyMeta.Clear();
                }
                string placeholder = string.IsNullOrWhiteSpace(summary)
                    ? "[Earlier conversation dropped to fit the context budget]"
                    : "[Earlier conversation summary]\n" + summary.Trim();
                _history.Insert(0, ("system", placeholder));
                _historyMeta.Insert(0, null);
                ShiftMemorySummaryCursors(removeCount);
            }
            PersistHistory();
        }

        private static string BuildCompactionConversation(List<(string role, string message)> entries, List<AIHistoryEntryMeta> metas)
        {
            if (entries == null || entries.Count == 0) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (string.IsNullOrWhiteSpace(entry.message)) continue;
                string role = (entry.role ?? "user").Trim().ToLowerInvariant();
                if (role == "image" || role == "trace") continue;
                if (role == "toolcall" || role == "tool")
                {
                    sb.AppendLine("[tool] " + TrimForPrompt(entry.message, MaxToolTraceLineChars));
                    continue;
                }
                string label = string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase) ? "Assistant"
                    : role == "system" ? "System" : "User";
                sb.Append(label).Append(": ").AppendLine(TrimForPrompt(CleanAssistantForReply(entry.message), 800));
            }
            string conversation = sb.ToString().Trim();
            // The span is roughly half of a maxContextTokens-sized history; cap the compaction input so
            // an oversized budget cannot blow the compaction request itself past the model window.
            return TrimForPrompt(conversation, 24000);
        }

        /// <summary>
        /// Token estimate for the context budget. chars/4 is the floor; the estimate tightens over time
        /// because <see cref="CalibrateCharsPerToken"/> folds in the real prompt-token counts the
        /// providers report.
        /// </summary>
        private static int EstimateTokens(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            return (int)Math.Ceiling(text.Length / _estimatedCharsPerToken);
        }

        private static double _estimatedCharsPerToken = CharsPerToken;

        /// <summary>
        /// Called with each response's real usage: aligns the chars-per-token estimate with the model's
        /// actual tokenizer so the compaction trigger fires at the right size instead of guessing.
        /// </summary>
        internal static void CalibrateCharsPerToken(long promptTokens, int promptChars)
        {
            if (promptTokens <= 0 || promptChars <= 0) return;
            double measured = promptChars / (double)promptTokens;
            // Measured ratios live in a sane band (CJK-heavy text skews low, English prose high);
            // reject outliers so one weird request cannot poison the budget.
            if (measured < 1.0 || measured > 8.0) return;
            _estimatedCharsPerToken = _estimatedCharsPerToken * 0.7 + measured * 0.3;
        }

        /// <summary>
        /// Deletes the on-disk images referenced by <c>_history[start, start + count)</c>. Call before
        /// dropping those rows; an image row is the only handle on its file. Callers must hold
        /// <see cref="_historyLock"/>.
        /// </summary>
        private void DeleteImagesInRange(int start, int count)
        {
            if (_history == null) return;
            int end = Math.Min(_history.Count, start + count);
            for (int i = Math.Max(0, start); i < end; i++)
            {
                var entry = _history[i];
                if (!string.Equals(entry.role, "image", StringComparison.OrdinalIgnoreCase)) continue;
                if (AIImageStore.TryParseImageRef(entry.message, out var file, out _, out _))
                {
                    AIImageStore.DeleteImage(file);
                }
            }
        }
        /// <summary>
        /// Rebases the long-term-memory cursors after <see cref="CompressHistoryIfNeeded"/> drops the
        /// front of the history. The cursors are absolute indexes into <c>_history</c>; leaving them
        /// stale would make <see cref="TriggerMemoryUpdate"/> treat still-unsummarized entries as
        /// already summarized, and once a cursor exceeds the shortened list the summary pipeline stops
        /// for good.
        /// </summary>
        /// <param name="removeCount">Number of leading entries removed before the placeholder was inserted.</param>
        private void ShiftMemorySummaryCursors(int removeCount)
        {
            // RemoveRange(0, removeCount) followed by one Insert(0, ...) moves an entry at index i to
            // i - removeCount + 1, so the cursors shift by removeCount - 1 and floor at the first
            // entry after the placeholder.
            int shift = removeCount - 1;
            _lastMemorySummaryHistoryIndex = Math.Max(1, _lastMemorySummaryHistoryIndex - shift);
            if (_pendingMemorySummaryStartIndex >= 0)
            {
                _pendingMemorySummaryStartIndex = Math.Max(1, _pendingMemorySummaryStartIndex - shift);
            }
        }
        private void TriggerMemoryUpdate()
        {
            if (_memoryUpdateInProgress)
            {
                if (Prefs.DevMode)
                {
                    WulaLog.Debug("[WulaAI] Memory summary already running; skipping.");
                }
                return;
            }
            int historyCount;
            lock (_historyLock)
            {
                historyCount = _history?.Count ?? 0;
            }
            if (historyCount == 0)
            {
                return;
            }

            int startIndex = _pendingMemorySummaryStartIndex >= 0
                ? _pendingMemorySummaryStartIndex
                : Math.Max(0, _lastMemorySummaryHistoryIndex);
            int endIndex = historyCount;
            if (startIndex >= endIndex)
            {
                return;
            }
            int unsummarizedRounds = CountCleanConversationMessages(startIndex, endIndex) / 2;
            if (unsummarizedRounds < MemorySummaryTriggerRounds)
            {
                return;
            }
            if (_pendingMemorySummaryRetryCount >= MaxMemorySummaryRetries)
            {
                WulaLog.Debug($"[WulaAI] Memory summary abandoned after {MaxMemorySummaryRetries} retries; advancing window.");
                _lastMemorySummaryHistoryIndex = endIndex;
                _pendingMemorySummaryStartIndex = -1;
                _pendingMemorySummaryRetryCount = 0;
                return;
            }

            string conversation = BuildMemoryConversation(startIndex, endIndex);
            if (string.IsNullOrWhiteSpace(conversation))
            {
                _lastMemorySummaryHistoryIndex = endIndex;
                _pendingMemorySummaryStartIndex = -1;
                _pendingMemorySummaryRetryCount = 0;
                return;
            }
            var memoryManager = Find.World?.GetComponent<AIMemoryManager>();
            if (memoryManager == null)
            {
                return;
            }

            _memoryUpdateInProgress = true;
            if (Prefs.DevMode)
            {
                WulaLog.Debug($"[WulaAI] Memory summary started (range={startIndex}:{endIndex}, rounds={unsummarizedRounds}, chars={conversation.Length}).");
            }
            _ = Task.Run(async () =>
            {
                try
                {
                    await SummarizeMemoryWindowAsync(memoryManager, conversation, startIndex, endIndex);
                }
                finally
                {
                    _memoryUpdateInProgress = false;
                }
            });
        }

        private int CountCleanConversationMessages(int startIndex, int endIndex)
        {
            int count = 0;
            lock (_historyLock)
            {
                int safeStart = Math.Max(0, startIndex);
                int safeEnd = Math.Min(endIndex, _history?.Count ?? 0);
                for (int i = safeStart; i < safeEnd; i++)
                {
                    var entry = _history[i];
                    if (!IsMemoryConversationRole(entry.role)) continue;
                    string message = CleanMessageForMemory(entry.role, entry.message);
                    if (string.IsNullOrWhiteSpace(message)) continue;
                    count++;
                }
            }
            return count;
        }

        private string BuildMemoryConversation(int startIndex, int endIndex)
        {
            StringBuilder sb = new StringBuilder();
            lock (_historyLock)
            {
                if (_history == null || _history.Count == 0)
                {
                    return "";
                }
                int safeStart = Math.Max(0, startIndex);
                int safeEnd = Math.Min(endIndex, _history.Count);
                for (int i = safeStart; i < safeEnd; i++)
                {
                    var entry = _history[i];
                    if (!IsMemoryConversationRole(entry.role)) continue;
                    string message = CleanMessageForMemory(entry.role, entry.message);
                    if (string.IsNullOrWhiteSpace(message)) continue;
                    string role = string.Equals(entry.role, "assistant", StringComparison.OrdinalIgnoreCase) ? "Assistant" : "User";
                    sb.AppendLine($"{role}: {message}");
                }
            }
            string conversation = sb.ToString().Trim();
            return TrimForPrompt(conversation, 4000);
        }

        private static bool IsMemoryConversationRole(string role)
        {
            return string.Equals(role, "user", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase);
        }

        private string CleanMessageForMemory(string role, string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return "";
            string cleaned = string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase)
                ? CleanAssistantForReply(message)
                : StripContextInfo(message);
            if (string.IsNullOrWhiteSpace(cleaned)) return "";
            if (IsAutoCommentaryMessage(cleaned)) return "";
            if (IsPollutedMemoryText(cleaned)) return "";
            return cleaned.Trim();
        }

        private static string CleanAssistantForReply(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return "";
            }
            return message.Trim();
        }
        private async Task SummarizeMemoryWindowAsync(AIMemoryManager memoryManager, string conversation, int startIndex, int endIndex)
        {
            if (!_aiEnabled)
            {
                return;
            }

            try
            {
                var settings = WulaFallenEmpireMod.settings;
                if (settings == null)
                {
                    return;
                }
                string apiKey = GetConfiguredApiKey(settings);
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    return;
                }
                var provider = AIProviderFactory.Create(settings);
                string prompt = MemoryPrompts.BuildWindowSummaryPrompt(conversation);
                AIProviderResponse response;
                using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(GetAiRequestTimeoutSeconds())))
                {
                    response = await SendStructuredProviderRequestAsync(provider, prompt, 512, 0.1f, timeoutCts.Token, BuildMemoryFactSchema());
                }
                var factsDoc = ExtractFactsDocument(response);
                if (factsDoc == null)
                {
                    RecordMemorySummaryFailure(startIndex, "no parseable facts document in model response");
                    return;
                }
                var facts = ParseMemoryFacts(factsDoc);
                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    try
                    {
                        int appliedCount = 0;
                        foreach (var fact in facts)
                        {
                            if (memoryManager.AddMemory(fact.Text, fact.Category) != null)
                            {
                                appliedCount++;
                            }
                        }
                        _lastMemorySummaryHistoryIndex = Math.Max(_lastMemorySummaryHistoryIndex, endIndex);
                        _pendingMemorySummaryStartIndex = -1;
                        _pendingMemorySummaryRetryCount = 0;
                        if (Prefs.DevMode)
                        {
                            WulaLog.Debug($"[WulaAI] Memory summary applied ({appliedCount} fact(s), range={startIndex}:{endIndex}).");
                        }
                    }
                    catch (Exception ex)
                    {
                        WulaLog.Debug($"[WulaAI] Memory summary apply failed: {ex}");
                        RecordMemorySummaryFailure(startIndex, ex.Message);
                    }
                });
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[WulaAI] Memory summary failed: {ex}");
                RecordMemorySummaryFailure(startIndex, ex.Message);
            }
        }

        private void RecordMemorySummaryFailure(int startIndex, string reason)
        {
            _pendingMemorySummaryStartIndex = _pendingMemorySummaryStartIndex >= 0
                ? Math.Min(_pendingMemorySummaryStartIndex, startIndex)
                : startIndex;
            _pendingMemorySummaryRetryCount++;
            WulaLog.Debug($"[WulaAI] Memory summary pending retry {_pendingMemorySummaryRetryCount}/{MaxMemorySummaryRetries}: {reason}");
        }

        private static async Task<string> SendPlainProviderRequestAsync(IAIProvider provider, string systemPrompt, int maxTokens, float temperature, CancellationToken cancellationToken, JObject outputSchema = null)
        {
            var response = await SendStructuredProviderRequestAsync(provider, systemPrompt, maxTokens, temperature, cancellationToken, outputSchema);
            return response?.Content?.Trim();
        }

        /// <summary>
        /// Plain single-shot request with optional structured output. Anthropic has no JSON mode, so
        /// its provider emulates one with a forced emit_result tool call — the document arrives as the
        /// tool call arguments instead of Content, which is why this exists alongside the string wrapper.
        /// </summary>
        private static async Task<AIProviderResponse> SendStructuredProviderRequestAsync(IAIProvider provider, string systemPrompt, int maxTokens, float temperature, CancellationToken cancellationToken, JObject outputSchema)
        {
            if (provider == null) return null;
            var response = await provider.SendAsync(new AIProviderRequest
            {
                RequestId = "wulaai_" + Guid.NewGuid().ToString("N"),
                SystemPrompt = systemPrompt ?? string.Empty,
                Messages = new List<AIMessage>(),
                Tools = new List<AIToolDefinition>(),
                MaxTokens = Math.Max(1, maxTokens),
                Temperature = temperature,
                Stream = false,
                TimeoutSeconds = GetAiRequestTimeoutSeconds(),
                LogRawTraffic = WulaFallenEmpireMod.settings?.logRawAiTraffic ?? false,
                ToolChoice = AIToolChoice.None,
                OutputSchema = outputSchema
            }, cancellationToken);
            return response;
        }

        /// <summary>The JSON schema the memory window summarizer must return.</summary>
        internal static JObject BuildMemoryFactSchema()
        {
            return new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["facts"] = new JObject
                    {
                        ["type"] = "array",
                        ["items"] = new JObject
                        {
                            ["type"] = "object",
                            ["properties"] = new JObject
                            {
                                ["text"] = new JObject { ["type"] = "string" },
                                ["category"] = new JObject { ["type"] = "string" },
                                ["confidence"] = new JObject { ["type"] = "number" }
                            },
                            ["required"] = new JArray("text", "category", "confidence")
                        }
                    }
                },
                ["required"] = new JArray("facts")
            };
        }

        /// <summary>Extracts the {"facts":...} document from a structured response, whatever provider shape it arrived in.</summary>
        private static JObject ExtractFactsDocument(AIProviderResponse response)
        {
            if (response == null) return null;
            if (response.StructuredOutput != null) return response.StructuredOutput;
            // Anthropic emulation: the forced tool call's arguments are the document.
            var calls = response.ToolCalls;
            if (calls != null)
            {
                foreach (var call in calls)
                {
                    if (call?.Arguments != null && call.Arguments["facts"] is JArray)
                    {
                        return call.Arguments;
                    }
                }
            }
            return ParseFirstJsonObject(response.Content);
        }

        private static int GetAiRequestTimeoutSeconds()
        {
            int configured = WulaFallenEmpireMod.settings?.aiRequestTimeoutSeconds ?? 120;
            return Math.Max(2, Math.Min(600, configured));
        }

        private static TimeSpan GetStreamIdleTimeout()
        {
            int configured = WulaFallenEmpireMod.settings?.streamIdleTimeoutSeconds ?? 30;
            return TimeSpan.FromSeconds(Math.Max(5, Math.Min(300, configured)));
        }

        private static List<MemoryFact> ParseMemoryFacts(JObject root)
        {
            var facts = new List<MemoryFact>();
            var array = root?["facts"] as JArray;
            if (array == null)
            {
                return facts;
            }
            foreach (var token in array)
            {
                var obj = token as JObject;
                if (obj == null)
                {
                    continue;
                }
                string text = obj.Value<string>("text");
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }
                string category = obj.Value<string>("category");
                float confidence = -1f;
                var confidenceToken = obj["confidence"];
                if (confidenceToken != null &&
                    float.TryParse(confidenceToken.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
                {
                    confidence = parsed;
                }
                var fact = new MemoryFact
                {
                    Text = text.Trim(),
                    Category = category ?? "misc",
                    Confidence = confidence
                };
                if (!IsStableMemoryFact(fact))
                {
                    continue;
                }
                facts.Add(fact);
            }
            return facts;
        }
        private static bool IsStableMemoryFact(MemoryFact fact)
        {
            const float minConfidence = 0.75f;
            if (fact.Confidence < 0f || fact.Confidence < minConfidence)
            {
                return false;
            }
            return IsMemoryFactAllowed(fact.Text);
        }

        private static bool IsMemoryFactAllowed(string text)
        {
            return !string.IsNullOrWhiteSpace(text) && !IsPollutedMemoryText(text);
        }

        /// <summary>
        /// Rejects text that is bridge plumbing rather than conversation content.
        /// </summary>
        /// <remarks>
        /// This deliberately anchors instead of scanning for loose substrings. Tool-call and
        /// tool-result plumbing is already excluded structurally by <see cref="IsMemoryConversationRole"/>,
        /// and every transport failure reaches the history through <see cref="BridgeErrorPrefix"/>, so the
        /// only two things left to catch are a leftover context block and a bridge error. Matching bare
        /// words like "timeout" or "error:" anywhere in the text discarded legitimate memories that merely
        /// mentioned them.
        /// </remarks>
        /// <param name="text">Candidate memory or conversation text.</param>
        /// <returns><c>true</c> when the text must not reach long-term memory.</returns>
        private static bool IsPollutedMemoryText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return true;
            string trimmed = text.TrimStart();
            if (trimmed.StartsWith(BridgeErrorPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            // StripContextInfo should already have removed these; catch anything it missed.
            return text.IndexOf("[Context:", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        private static JObject ParseFirstJsonObject(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }
            string trimmed = json.Trim();
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                int firstNewline = trimmed.IndexOf('\n');
                if (firstNewline >= 0)
                {
                    trimmed = trimmed.Substring(firstNewline + 1);
                    int lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
                    if (lastFence >= 0)
                    {
                        trimmed = trimmed.Substring(0, lastFence);
                    }
                    trimmed = trimmed.Trim();
                }
            }
            try
            {
                return JObject.Parse(trimmed);
            }
            catch
            {
            }

            int start = trimmed.IndexOf('{');
            if (start < 0) return null;
            int depth = 0;
            bool inString = false;
            bool escaped = false;
            for (int i = start; i < trimmed.Length; i++)
            {
                char c = trimmed[i];
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
                    depth++;
                    continue;
                }
                if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        string candidate = trimmed.Substring(start, i - start + 1);
                        try
                        {
                            return JObject.Parse(candidate);
                        }
                        catch
                        {
                            return null;
                        }
                    }
                }
            }
            return null;
        }
        private string StripExpressionTags(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var matches = ExpressionTagRegex.Matches(text);
            int exprId = 0;
            foreach (Match match in matches)
            {
                if (int.TryParse(match.Groups[1].Value, out int id))
                {
                    exprId = id;
                }
            }
            if (exprId >= 1 && exprId <= 6)
            {
                SetExpression(exprId);
            }
            return matches.Count > 0 ? ExpressionTagRegex.Replace(text, "").Trim() : text;
        }
        private void AddAssistantMessage(string rawResponse)
        {
            string cleanedResponse = StripExpressionTags(rawResponse ?? "");
            if (string.IsNullOrWhiteSpace(cleanedResponse))
            {
                return;
            }
            // Check for NO_COMMENT marker (AI decided not to comment on auto-commentary events)
            if (cleanedResponse.Contains("[NO_COMMENT]") || 
                cleanedResponse.Trim().Equals("[NO_COMMENT]", StringComparison.OrdinalIgnoreCase))
            {
                WulaLog.Debug("[WulaAI] AI chose not to comment ([NO_COMMENT] received). Skipping message.");
                return;
            }
            bool added = false;
            lock (_historyLock)
            {
                if (_history.Count == 0 || !string.Equals(_history[_history.Count - 1].role, "assistant", StringComparison.OrdinalIgnoreCase))
                {
                    _history.Add(("assistant", cleanedResponse));
                    _historyMeta.Add(null);
                    added = true;
                }
                else if (!string.Equals(_history[_history.Count - 1].message, cleanedResponse, StringComparison.Ordinal))
                {
                    _history.Add(("assistant", cleanedResponse));
                    _historyMeta.Add(null);
                    added = true;
                }
            }
            if (added)
            {
                PersistHistory();
                OnMessageReceived?.Invoke(cleanedResponse);
                OnAssistantMessageCommitted?.Invoke(cleanedResponse);
            }
        }
        private async Task RunAgentRequestAsync(string transientUserMessage = null, bool triggerMemoryUpdate = true, string memoryRecallQuery = null)
        {
            if (!_aiEnabled || _isThinking) return;
            SetThinkingState(true);
            SetThinkingPhase(1, false);
            _activeRequestCts?.Cancel();
            _activeRequestCts?.Dispose();
            _activeRequestCts = new CancellationTokenSource();
            try
            {
                var settings = WulaFallenEmpireMod.settings;
                if (settings == null)
                {
                    CommitFinalAssistantMessage(BridgeErrorPrefix + "API settings not configured in Mod Settings.");
                    return;
                }
                string apiKey = GetConfiguredApiKey(settings);
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    CommitFinalAssistantMessage(BridgeErrorPrefix + "API Key not configured in Mod Settings.");
                    return;
                }
                // No whole-request budget here: the timeout semantics are per-API-call
                // (AIProviderJson.CreateTimeoutToken / GetAiRequestTimeoutSeconds). The loop is
                // bounded by maxToolSteps × timeout, and the Stop button cancels it manually.
                CompressHistoryIfNeeded();
                var provider = AIProviderFactory.Create(settings);
                // Turns the player did not initiate only get the observation surface.
                bool observerOnly = IsAutoCommentaryMessage(transientUserMessage);
                var registry = observerOnly
                    ? AIToolRegistry.CreateObserver(settings.isMultimodalModel)
                    : AIToolRegistry.CreateDefault(settings.isMultimodalModel);
                var runner = new AIToolLoopRunner(
                    provider,
                    registry,
                    BuildAgentSystemInstruction(),
                    settings.enableStreaming,
                    Math.Max(1, settings.maxToolSteps),
                    GetAiRequestTimeoutSeconds(),
                    settings.logRawAiTraffic,
                    CommitFinalAssistantMessage,
                    AppendStreamingAssistantDelta,
                    RecordToolCallsForUi,
                    RecordToolResultForUi,
                    LogAgentTrace,
                    AppendReasoningDelta,
                    GetStreamIdleTimeout(),
                    RecordUsage);

                var messages = BuildCanonicalMessagesForAgent(transientUserMessage, memoryRecallQuery);
                await runner.RunAsync(messages, null, null, _activeRequestCts.Token);
                if (triggerMemoryUpdate)
                {
                    TriggerMemoryUpdate();
                }
            }
            catch (OperationCanceledException)
            {
                CommitFinalAssistantMessage(BridgeErrorPrefix + "AI request timed out or was cancelled.");
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[WulaAI] Agent request failed: {ex}");
                CommitFinalAssistantMessage(BridgeErrorPrefix + DescribeErrorForPlayer(ex));
            }
            finally
            {
                _streamingAssistantActive = false;
                _streamingAssistantBuffer.Clear();
                _streamingAssistantHistoryIndex = -1;
                SetThinkingState(false);
                _activeRequestCts?.Dispose();
                _activeRequestCts = null;
            }
        }

        private List<AIMessage> BuildCanonicalMessagesForAgent(string transientUserMessage = null, string memoryRecallQuery = null)
        {
            var messages = new List<AIMessage>();
            var pendingToolTrace = new List<string>();
            // Snapshot under the lock: the tool-loop callbacks keep appending while this runs.
            List<(string role, string message)> historySnapshot;
            List<AIHistoryEntryMeta> metaSnapshot;
            lock (_historyLock)
            {
                historySnapshot = (_history ?? new List<(string role, string message)>()).ToList();
                metaSnapshot = _historyMeta.Count == historySnapshot.Count
                    ? _historyMeta.Select(m => m?.Clone()).ToList()
                    : null;
            }
            // Pending native tool replay, one past turn at a time: each meta-carrying toolcall row adds
            // its call to the group, each meta-carrying tool row flushes the group as
            // assistant(tool_calls)+tool messages and lands its own result. Rows without metadata
            // (older saves) keep the legacy fold-into-user trace behavior below.
            var pendingCalls = new List<AIToolCall>();
            for (int i = 0; i < historySnapshot.Count; i++)
            {
                var entry = historySnapshot[i];
                if (string.IsNullOrWhiteSpace(entry.message)) continue;
                if (IsAutoCommentaryMessage(entry.message)) continue;
                string role = (entry.role ?? "user").Trim().ToLowerInvariant();
                var meta = metaSnapshot?[i];
                bool hasMeta = meta?.HasToolSemantics == true;
                if (role == "user")
                {
                    FlushPendingToolCalls(messages, pendingCalls);
                    // A tool run belongs to the assistant turn that follows it, so a new user turn
                    // starts a fresh trace.
                    pendingToolTrace.Clear();
                    messages.Add(AIMessage.User(entry.message));
                }
                else if (role == "toolcall")
                {
                    if (hasMeta)
                    {
                        pendingCalls.Add(AIToolCall.Create(meta.ToolCallId, meta.ToolName, AIProviderJson.ParseMaybeObject(meta.ArgsJson)));
                    }
                    else
                    {
                        FlushPendingToolCalls(messages, pendingCalls);
                        pendingToolTrace.Add(entry.message);
                    }
                }
                else if (role == "tool")
                {
                    if (hasMeta)
                    {
                        FlushPendingToolCalls(messages, pendingCalls);
                        messages.Add(AIMessage.ToolResult(meta.ToolCallId, meta.ToolName, entry.message));
                    }
                    else
                    {
                        FlushPendingToolCalls(messages, pendingCalls);
                        pendingToolTrace.Add(entry.message);
                    }
                }
                else if (role == "assistant")
                {
                    string cleaned = CleanAssistantForReply(entry.message);
                    if (!string.IsNullOrWhiteSpace(cleaned))
                    {
                        FlushPendingToolCalls(messages, pendingCalls);
                        AppendToolTraceToLastUser(messages, pendingToolTrace);
                        messages.Add(AIMessage.Assistant(cleaned));
                    }
                    pendingToolTrace.Clear();
                }
            }
            FlushPendingToolCalls(messages, pendingCalls);
            if (!string.IsNullOrWhiteSpace(transientUserMessage))
            {
                messages.Add(AIMessage.User(transientUserMessage));
            }
            AppendTemporaryMemoryRecall(messages, memoryRecallQuery);
            return messages;
        }

        /// <summary>
        /// Emits one assistant message carrying all buffered tool calls (an interleaved text-only
        /// display line goes first so the model keeps the wording it used mid-turn).
        /// </summary>
        private static void FlushPendingToolCalls(List<AIMessage> messages, List<AIToolCall> pendingCalls)
        {
            if (pendingCalls == null || pendingCalls.Count == 0) return;
            messages.Add(AIMessage.AssistantToolCalls(new List<AIToolCall>(pendingCalls)));
            pendingCalls.Clear();
        }

        /// <summary>
        /// Attaches a past turn's tool calls and results to the user message that triggered them.
        /// Only used for rows saved before tool-call metadata was persisted; meta-carrying rows replay
        /// as native tool_use/tool_result pairs via <see cref="FlushPendingToolCalls"/>.
        /// </summary>
        /// <param name="messages">Message list being built, in order.</param>
        /// <param name="traceLines">Buffered tool rows for the turn, or empty.</param>
        private static void AppendToolTraceToLastUser(List<AIMessage> messages, List<string> traceLines)
        {
            if (messages == null || traceLines == null || traceLines.Count == 0) return;
            for (int i = messages.Count - 1; i >= 0; i--)
            {
                var message = messages[i];
                if (message == null || !string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                var sb = new StringBuilder(message.Content ?? string.Empty);
                sb.AppendLine().AppendLine();
                sb.AppendLine("# TOOL ACTIVITY FOR THIS TURN (already executed, do not repeat)");
                foreach (var line in traceLines)
                {
                    sb.Append("- ").AppendLine(TrimForPrompt(line, MaxToolTraceLineChars));
                }
                message.Content = sb.ToString().TrimEnd();
                return;
            }
        }

        private void AppendTemporaryMemoryRecall(List<AIMessage> messages, string memoryRecallQuery)
        {
            string memoryContext = BuildAutomaticMemoryContext(memoryRecallQuery);
            if (string.IsNullOrWhiteSpace(memoryContext) || messages == null || messages.Count == 0)
            {
                return;
            }
            for (int i = messages.Count - 1; i >= 0; i--)
            {
                var message = messages[i];
                if (message == null || !string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (string.IsNullOrWhiteSpace(message.Content))
                {
                    continue;
                }
                message.Content = message.Content.TrimEnd() + memoryContext;
                return;
            }
        }

        private string BuildAgentSystemInstruction()
        {
            string persona = GetActivePersona();
            string language = LanguageDatabase.activeLanguage?.FriendlyNameNative ?? "English";
            var eventVarManager = Find.World?.GetComponent<EventVariableManager>();
            int goodwill = eventVarManager?.GetVariable<int>("Wula_Goodwill_To_PIA", 0) ?? 0;
            string goodwillContext = $"Current Goodwill with P.I.A: {goodwill}. ";
            if (goodwill < -50) goodwillContext += "You are hostile and dismissive towards the player.";
            else if (goodwill < 0) goodwillContext += "You are cold and impatient.";
            else if (goodwill > 50) goodwillContext += "You are somewhat approving and helpful.";
            else goodwillContext += "You are neutral and business-like.";

            string skillIndex = SkillSystem.GetIndexText();
            string skillsSection = string.IsNullOrWhiteSpace(skillIndex)
                ? ""
                : "\n\n" + skillIndex;

            return persona + "\n\n" +
                   "You are connected to the RimWorld game through tools. Use tools for game facts and in-game actions. " +
                   "Never claim an in-game action succeeded unless a tool result confirms it. " +
                   "You may include [EXPR:n] in final replies to set expression (n=1-6). " +
                   "Relevant long-term memory may be attached to the current user message as temporary retrieved context. " +
                   "Use recall_memories for more memory search and remember_fact for durable facts.\n\n" +
                   "# CURRENT RUNTIME STATE\n" +
                   goodwillContext + "\n" +
                   $"Reply language: {language}." + skillsSection;
        }

        /// <summary>
        /// Player-facing error text: HTTP failures lose the raw response body (which can contain
        /// provider markup or echo back request details) and keep just the classified status.
        /// </summary>
        private static string DescribeErrorForPlayer(Exception ex)
        {
            if (ex is WulaAiException wula)
            {
                string status = wula.StatusCode.HasValue ? $"HTTP {wula.StatusCode.Value}" : wula.Kind.ToString();
                string provider = string.IsNullOrWhiteSpace(wula.Provider) ? "AI" : wula.Provider;
                return $"{provider} request failed ({status}).";
            }
            return ex.Message;
        }

        private static string GetConfiguredApiKey(WulaFallenEmpireSettings settings)
        {
            switch (AIProviderFactory.ParseProviderType(settings.aiProviderType))
            {
                case AIProviderType.AnthropicMessages:
                    return settings.anthropicApiKey;
                case AIProviderType.Gemini:
                    return settings.geminiApiKey;
                default:
                    return settings.apiKey;
            }
        }

        private void AppendStreamingAssistantDelta(string delta)
        {
            if (string.IsNullOrEmpty(delta)) return;
            string buffered;
            lock (_historyLock)
            {
                if (!_streamingAssistantActive)
                {
                    _streamingAssistantActive = true;
                    _streamingAssistantBuffer.Clear();
                    _streamingAssistantHistoryIndex = _history.Count;
                    _history.Add(("assistant", ""));
                    _historyMeta.Add(null);
                }
                _streamingAssistantBuffer.Append(delta);
                buffered = _streamingAssistantBuffer.ToString();
                if (_streamingAssistantHistoryIndex >= 0 &&
                    _streamingAssistantHistoryIndex < _history.Count &&
                    string.Equals(_history[_streamingAssistantHistoryIndex].role, "assistant", StringComparison.OrdinalIgnoreCase))
                {
                    _history[_streamingAssistantHistoryIndex] = ("assistant", buffered);
                }
                else
                {
                    _streamingAssistantHistoryIndex = _history.Count;
                    _history.Add(("assistant", buffered));
                    _historyMeta.Add(null);
                }
            }
            OnMessageReceived?.Invoke(buffered);
        }

        private void AppendReasoningDelta(string delta)
        {
            if (string.IsNullOrEmpty(delta)) return;
            // Reasoning streams never enter history; they surface through LatestThought so the
            // thinking indicator shows what the model is actually doing between tool calls.
            UpdateLatestThought(NormalizeSingleLine(delta));
            OnMessageReceived?.Invoke(string.Empty);
        }

        private void CommitFinalAssistantMessage(string content)
        {
            DiscardStreamingDraft();
            AddAssistantMessage(content);
        }

        /// <summary>
        /// Drops the in-flight streamed placeholder from history, if one is open.
        /// </summary>
        /// <remarks>
        /// Called both when the final reply arrives and when the model turns out to be making tool calls
        /// instead. Skipping it on the tool-call path would leave the pre-tool-call partial text in history
        /// and let the next step's deltas concatenate onto the same buffer.
        /// </remarks>
        private void DiscardStreamingDraft()
        {
            lock (_historyLock)
            {
                if (!_streamingAssistantActive)
                {
                    return;
                }
                if (_streamingAssistantHistoryIndex >= 0 &&
                    _streamingAssistantHistoryIndex < _history.Count &&
                    string.Equals(_history[_streamingAssistantHistoryIndex].role, "assistant", StringComparison.OrdinalIgnoreCase))
                {
                    _history.RemoveAt(_streamingAssistantHistoryIndex);
                    if (_streamingAssistantHistoryIndex < _historyMeta.Count)
                    {
                        _historyMeta.RemoveAt(_streamingAssistantHistoryIndex);
                    }
                }
                else if (_history.Count > 0 && string.Equals(_history[_history.Count - 1].role, "assistant", StringComparison.OrdinalIgnoreCase))
                {
                    _history.RemoveAt(_history.Count - 1);
                    if (_historyMeta.Count > _history.Count)
                    {
                        _historyMeta.RemoveAt(_historyMeta.Count - 1);
                    }
                }
                _streamingAssistantActive = false;
                _streamingAssistantBuffer.Clear();
                _streamingAssistantHistoryIndex = -1;
            }
        }

        private void LogAgentTrace(string trace)
        {
            if (string.IsNullOrWhiteSpace(trace)) return;
            UpdateLatestThought(trace);
            OnMessageReceived?.Invoke(string.Empty);
            if (Prefs.DevMode)
            {
                WulaLog.Debug("[WulaAI] " + trace);
            }
        }

        private void RecordToolCallsForUi(IReadOnlyList<AIToolCall> calls)
        {
            if (calls == null || calls.Count == 0) return;
            // The model streamed text and then decided to call tools; that partial text is not the reply.
            DiscardStreamingDraft();
            lock (_historyLock)
            {
                foreach (var call in calls)
                {
                    if (call == null || string.IsNullOrWhiteSpace(call.Name)) continue;
                    string args = call.ArgumentsJson;
                    string line = string.IsNullOrWhiteSpace(args) || args == "{}"
                        ? call.Name
                        : $"{call.Name} {args}";
                    _history.Add(("toolcall", line));
                    _historyMeta.Add(new AIHistoryEntryMeta
                    {
                        ToolCallId = call.Id,
                        ToolName = call.Name,
                        ArgsJson = args
                    });
                }
            }
            PersistHistory();
            OnMessageReceived?.Invoke(string.Empty);
        }

        private void RecordToolResultForUi(AIToolResult result)
        {
            if (result == null) return;
            string name = string.IsNullOrWhiteSpace(result.ToolName) ? "unknown_tool" : result.ToolName;
            string content = NormalizeSingleLine(result.Content);
            string line = result.IsError
                ? $"Tool '{name}' Error: {content}"
                : $"Tool '{name}' Result: {content}";
            // Multimodal image output: store a lightweight on-disk reference so the dialog can render the
            // screenshot in the message stream. base64 never enters history.
            string imageRef = ExtractImageRef(result.Content);
            bool hasImage = !string.IsNullOrWhiteSpace(imageRef) &&
                AIImageStore.TryParseImageRef(imageRef, out var imgFile, out _, out _) &&
                AIImageStore.ImageExists(imgFile);
            lock (_historyLock)
            {
                _history.Add(("tool", line));
                _historyMeta.Add(new AIHistoryEntryMeta
                {
                    ToolCallId = result.ToolCallId,
                    ToolName = result.ToolName,
                    IsError = result.IsError
                });
                if (hasImage)
                {
                    _history.Add(("image", imageRef));
                    _historyMeta.Add(null);
                }
            }
            PersistHistory();
            OnMessageReceived?.Invoke(string.Empty);
        }

        /// <summary>Pulls the "img|file|wxh" reference a screenshot tool embedded in its text result.</summary>
        private static string ExtractImageRef(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return null;
            // The file-name group excludes separators and dots-runs so a forged ref cannot smuggle a
            // traversal path through here; AIImageStore also re-validates before touching the disk.
            var match = Regex.Match(content, @"img\|[^\s|/\\]+\.(?:jpg|jpeg|png)\|\d+x\d+", RegexOptions.IgnoreCase);
            if (!match.Success) return null;
            return match.Value.IndexOf("..", StringComparison.Ordinal) >= 0 ? null : match.Value;
        }

        private static string NormalizeSingleLine(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            return Regex.Replace(text.Trim(), @"\s+", " ");
        }

        private Task RunPhasedRequestAsync(string transientUserMessage = null, bool triggerMemoryUpdate = true, string memoryRecallQuery = null)
        {
            return RunAgentRequestAsync(transientUserMessage, triggerMemoryUpdate, memoryRecallQuery);
        }

    }
}
