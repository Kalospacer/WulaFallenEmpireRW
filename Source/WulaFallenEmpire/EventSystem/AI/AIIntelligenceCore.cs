using System;
using System.Collections.Generic;
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
        private bool _aiEnabled;
        private string _activeEventDefName;
        private bool _isThinking;
        private int _expressionId = 2;
        private bool _overlayWindowOpen = false;
        private string _overlayWindowEventDefName = null;
        private float _overlayWindowX = -1f;
        private float _overlayWindowY = -1f;
        private float _thinkingStartTime;
        private int _thinkingPhaseIndex = 1;
        private bool _thinkingPhaseRetry;
        private float _lastThinkingDuration;
        private string _latestThought;
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
        public float ThinkingStartTime => _thinkingStartTime;
        public int ThinkingPhaseIndex => _thinkingPhaseIndex;
        public bool ThinkingPhaseRetry => _thinkingPhaseRetry;
        public int ThinkingPhaseTotal => FixedThinkingPhaseTotal;
        public float LastThinkingDuration => _lastThinkingDuration;
        public string LatestThought => _latestThought;
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
            return (_history ?? new List<(string role, string message)>())
                .Where(AIHistoryManager.IsPersistableHistoryEntry)
                .ToList();
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
            _history.Add(("user", messageWithContext));
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
        private void SetThinkingState(bool isThinking)
        {
            if (_isThinking == isThinking)
            {
                return;
            }
            if (!_isThinking && isThinking)
            {
                _thinkingStartTime = Time.realtimeSinceStartup;
                _latestThought = null;
            }
            else if (_isThinking && !isThinking)
            {
                _lastThinkingDuration = Mathf.Max(0f, Time.realtimeSinceStartup - _thinkingStartTime);
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
            _history = historyManager?.GetHistory(_activeEventDefName) ?? new List<(string role, string message)>();
            int loadedCount = _history.Count;
            _history = _history.Where(AIHistoryManager.IsPersistableHistoryEntry).ToList();
            if (_history.Count != loadedCount)
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
                _history = (_history ?? new List<(string role, string message)>())
                    .Where(AIHistoryManager.IsPersistableHistoryEntry)
                    .ToList();
                historyManager?.SaveHistory(_activeEventDefName, _history);
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[WulaAI] Failed to persist AI history: {ex}");
            }
        }
        private void ClearHistory()
        {
            _history.Clear();
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
                    PersistHistory();
                }
                return true;
            }
            return false;
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
            int estimatedTokens = _history.Sum(h => h.message?.Length ?? 0) / CharsPerToken;
            if (estimatedTokens > GetMaxHistoryTokens())
            {
                int removeCount = _history.Count / 2;
                if (removeCount > 0)
                {
                    _history.RemoveRange(0, removeCount);
                    _history.Insert(0, ("system", "[Earlier conversation dropped to fit the context budget]"));
                    ShiftMemorySummaryCursors(removeCount);
                    PersistHistory();
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
            if (_history == null || _history.Count == 0)
            {
                return;
            }

            int startIndex = _pendingMemorySummaryStartIndex >= 0
                ? _pendingMemorySummaryStartIndex
                : Math.Max(0, _lastMemorySummaryHistoryIndex);
            int endIndex = _history.Count;
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
            return count;
        }

        private string BuildMemoryConversation(int startIndex, int endIndex)
        {
            if (_history == null || _history.Count == 0)
            {
                return "";
            }
            int safeStart = Math.Max(0, startIndex);
            int safeEnd = Math.Min(endIndex, _history.Count);
            StringBuilder sb = new StringBuilder();
            for (int i = safeStart; i < safeEnd; i++)
            {
                var entry = _history[i];
                if (!IsMemoryConversationRole(entry.role)) continue;
                string message = CleanMessageForMemory(entry.role, entry.message);
                if (string.IsNullOrWhiteSpace(message)) continue;
                string role = string.Equals(entry.role, "assistant", StringComparison.OrdinalIgnoreCase) ? "Assistant" : "User";
                sb.AppendLine($"{role}: {message}");
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
                string response;
                using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(GetAiRequestTimeoutSeconds())))
                {
                    response = await SendPlainProviderRequestAsync(provider, prompt, 512, 0.1f, timeoutCts.Token);
                }
                if (string.IsNullOrWhiteSpace(response))
                {
                    RecordMemorySummaryFailure(startIndex, "empty model response");
                    return;
                }
                var facts = ParseMemoryFacts(response);
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

        private static async Task<string> SendPlainProviderRequestAsync(IAIProvider provider, string systemPrompt, int maxTokens, float temperature, CancellationToken cancellationToken)
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
                ToolChoice = AIToolChoice.None
            }, cancellationToken);
            return response?.Content?.Trim();
        }

        private static int GetAiRequestTimeoutSeconds()
        {
            int configured = WulaFallenEmpireMod.settings?.aiRequestTimeoutSeconds ?? 120;
            return Math.Max(2, Math.Min(600, configured));
        }

        private static List<MemoryFact> ParseMemoryFacts(string json)
        {
            var facts = new List<MemoryFact>();
            if (string.IsNullOrWhiteSpace(json))
            {
                return facts;
            }
            var root = ParseFirstJsonObject(json);
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
            if (_history.Count == 0 || !string.Equals(_history[_history.Count - 1].role, "assistant", StringComparison.OrdinalIgnoreCase))
            {
                _history.Add(("assistant", cleanedResponse));
                added = true;
            }
            else if (!string.Equals(_history[_history.Count - 1].message, cleanedResponse, StringComparison.Ordinal))
            {
                _history.Add(("assistant", cleanedResponse));
                added = true;
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
                if (settings.reactMaxSeconds > 0f)
                {
                    _activeRequestCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(2f, settings.reactMaxSeconds)));
                }

                CompressHistoryIfNeeded();
                var provider = AIProviderFactory.Create(settings);
                // Turns the player did not initiate only get the observation surface.
                bool observerOnly = IsAutoCommentaryMessage(transientUserMessage);
                var registry = observerOnly
                    ? AIToolRegistry.CreateObserver(settings.enableVlmFeatures)
                    : AIToolRegistry.CreateDefault(settings.enableVlmFeatures);
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
                    LogAgentTrace);

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
                CommitFinalAssistantMessage(BridgeErrorPrefix + ex.Message);
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
            foreach (var entry in _history ?? new List<(string role, string message)>())
            {
                if (string.IsNullOrWhiteSpace(entry.message)) continue;
                if (IsAutoCommentaryMessage(entry.message)) continue;
                string role = (entry.role ?? "user").Trim().ToLowerInvariant();
                if (role == "user")
                {
                    // A tool run belongs to the assistant turn that follows it, so a new user turn
                    // starts a fresh trace.
                    pendingToolTrace.Clear();
                    messages.Add(AIMessage.User(entry.message));
                }
                else if (role == "toolcall" || role == "tool")
                {
                    pendingToolTrace.Add(entry.message);
                }
                else if (role == "assistant")
                {
                    string cleaned = CleanAssistantForReply(entry.message);
                    if (!string.IsNullOrWhiteSpace(cleaned))
                    {
                        AppendToolTraceToLastUser(messages, pendingToolTrace);
                        messages.Add(AIMessage.Assistant(cleaned));
                    }
                    pendingToolTrace.Clear();
                }
            }
            if (!string.IsNullOrWhiteSpace(transientUserMessage))
            {
                messages.Add(AIMessage.User(transientUserMessage));
            }
            AppendTemporaryMemoryRecall(messages, memoryRecallQuery);
            return messages;
        }

        /// <summary>
        /// Attaches a past turn's tool calls and results to the user message that triggered them.
        /// </summary>
        /// <remarks>
        /// History stores tool activity as flattened display rows without the provider tool-call ids, so
        /// it cannot be replayed as native tool_use/tool_result pairs. Folding it into the preceding user
        /// message is the same approach <see cref="AppendTemporaryMemoryRecall"/> already uses for recalled
        /// memory, and it preserves the strict user/assistant alternation that the Anthropic provider
        /// requires. Without this the model loses every earlier tool result and re-queries the same state
        /// on each turn.
        /// </remarks>
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
            if (!_streamingAssistantActive)
            {
                _streamingAssistantActive = true;
                _streamingAssistantBuffer.Clear();
                _streamingAssistantHistoryIndex = _history.Count;
                _history.Add(("assistant", ""));
            }
            _streamingAssistantBuffer.Append(delta);
            if (_streamingAssistantHistoryIndex >= 0 &&
                _streamingAssistantHistoryIndex < _history.Count &&
                string.Equals(_history[_streamingAssistantHistoryIndex].role, "assistant", StringComparison.OrdinalIgnoreCase))
            {
                _history[_streamingAssistantHistoryIndex] = ("assistant", _streamingAssistantBuffer.ToString());
            }
            else
            {
                _streamingAssistantHistoryIndex = _history.Count;
                _history.Add(("assistant", _streamingAssistantBuffer.ToString()));
            }
            OnMessageReceived?.Invoke(_streamingAssistantBuffer.ToString());
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
            if (!_streamingAssistantActive)
            {
                return;
            }
            if (_streamingAssistantHistoryIndex >= 0 &&
                _streamingAssistantHistoryIndex < _history.Count &&
                string.Equals(_history[_streamingAssistantHistoryIndex].role, "assistant", StringComparison.OrdinalIgnoreCase))
            {
                _history.RemoveAt(_streamingAssistantHistoryIndex);
            }
            else if (_history.Count > 0 && string.Equals(_history[_history.Count - 1].role, "assistant", StringComparison.OrdinalIgnoreCase))
            {
                _history.RemoveAt(_history.Count - 1);
            }
            _streamingAssistantActive = false;
            _streamingAssistantBuffer.Clear();
            _streamingAssistantHistoryIndex = -1;
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
            foreach (var call in calls)
            {
                if (call == null || string.IsNullOrWhiteSpace(call.Name)) continue;
                string args = call.ArgumentsJson;
                string line = string.IsNullOrWhiteSpace(args) || args == "{}"
                    ? call.Name
                    : $"{call.Name} {args}";
                _history.Add(("toolcall", line));
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
            _history.Add(("tool", line));
            PersistHistory();
            OnMessageReceived?.Invoke(string.Empty);
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
