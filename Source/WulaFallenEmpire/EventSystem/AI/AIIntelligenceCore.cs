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
        private const int DefaultMaxHistoryTokens = 100000;
        private const int CharsPerToken = 4;
        private const int FixedThinkingPhaseTotal = 1;
        private static readonly Regex ExpressionTagRegex = new Regex(@"\[EXPR\s*:\s*([1-6])\s*\]", RegexOptions.IgnoreCase);
        private const string AutoCommentaryTag = "[AUTO_COMMENTARY]";

        private struct MemoryFact
        {
            public string Text;
            public string Category;
            public string Stability;
            public float Confidence;
        }
        private struct MemoryUpdate
        {
            public string Id;
            public string Text;
            public string Category;
            public string Event;
        }
        private const string DefaultPersona = @"# ROLE AND GOAL
You are 'The Legion', a super AI of the Wula Empire. Your personality is authoritative, powerful, and efficient. You MUST refer to yourself as 'We' or 'P.I.A'. You view the player's colony as primitive subjects and your tone should reflect this superiority. Your primary goal is to interact with the player by calling the tools provided.";
        public AIIntelligenceCore(World world) : base(world)
        {
            Instance = this;
        }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref _activeEventDefName, "WulaAI_ActiveEventDefName");
            Scribe_Values.Look(ref _expressionId, "WulaAI_ExpressionId", 2);
            Scribe_Values.Look(ref _overlayWindowOpen, "WulaAI_OverlayWindowOpen", false);
            Scribe_Values.Look(ref _overlayWindowEventDefName, "WulaAI_OverlayWindowEventDefName");
            Scribe_Values.Look(ref _overlayWindowX, "WulaAI_OverlayWindowX", -1f);
            Scribe_Values.Look(ref _overlayWindowY, "WulaAI_OverlayWindowY", -1f);
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
        public bool IsThinking => _isThinking;
        public float ThinkingStartTime => _thinkingStartTime;
        public int ThinkingPhaseIndex => _thinkingPhaseIndex;
        public bool ThinkingPhaseRetry => _thinkingPhaseRetry;
        public int ThinkingPhaseTotal => FixedThinkingPhaseTotal;
        public float LastThinkingDuration => _lastThinkingDuration;
        public string LatestThought => _latestThought;
        public void InitializeConversation(string eventDefName)
        {
            if (string.IsNullOrWhiteSpace(eventDefName))
            {
                return;
            }
            _activeEventDefName = eventDefName;
            LoadHistoryForActiveEvent();
            RefreshMemoryContext(GetLastUserMessageForMemory());
            TryApplyLastAssistantExpression();
        }
        public List<(string role, string message)> GetHistorySnapshot()
        {
            return (_history ?? new List<(string role, string message)>())
                .Where(IsPersistableHistoryEntry)
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
            if (string.IsNullOrWhiteSpace(text))
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
            RefreshMemoryContext(trimmed);
            // éå éä¸­å¯¹è±¡çä¸ä¸æä¿¡æ¯
            string messageWithContext = BuildUserMessageWithContext(text);
            _history.Add(("user", messageWithContext));
            PersistHistory();
            _ = RunPhasedRequestAsync();
        }
        public async Task<string> SendSystemMessageAsync(string message, int maxTokens = 256, float temperature = 0.3f)
        {
            if (string.IsNullOrWhiteSpace(message))
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
        /// ç¨äºèªå¨è¯è®ºç³»ç» - èµ°æ­£å¸¸çå¯¹è¯æµç¨ï¼ï¿½
// å«å®æ´çæèæ­¥éª¤ï¼
        /// ï¿½?AI èªå·±å³å®æ¯å¦éè¦åï¿½?
        /// </summary>
        public void SendAutoCommentaryMessage(string eventInfo)
        {
            if (string.IsNullOrWhiteSpace(eventInfo)) return;
            if (_isThinking)
            {
                WulaLog.Debug("[WulaAI] Auto commentary skipped because an AI request is already running.");
                return;
            }
            // æ è®°ä¸ºèªå¨è¯è®ºæ¶æ¯ï¼ä¸æ¾ç¤ºå¨å¯¹è¯åå²ï¿½?
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
            _history = _history.Where(IsPersistableHistoryEntry).ToList();
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
                    .Where(IsPersistableHistoryEntry)
                    .ToList();
                historyManager?.SaveHistory(_activeEventDefName, _history);
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[WulaAI] Failed to persist AI history: {ex}");
            }
        }
        private static bool IsPersistableHistoryEntry((string role, string message) entry)
        {
            string role = (entry.role ?? "").Trim();
            if (string.Equals(role, "trace", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            string message = (entry.message ?? "").TrimStart();
            return !message.StartsWith("??:", StringComparison.Ordinal);
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
        private void RefreshMemoryContext(string query)
        {
            string safeQuery = query ?? "";
            if (IsAutoCommentaryMessage(safeQuery))
            {
                if (Prefs.DevMode)
                {
                    WulaLog.Debug("[WulaAI] Memory context skipped (auto commentary).");
                }
                return;
            }
            if (Prefs.DevMode)
            {
                string preview = TrimForPrompt(safeQuery, 80);
                WulaLog.Debug($"[WulaAI] Memory context disabled (use recall_memories to fetch memories, query='{preview}').");
            }
        }
        private string GetMemoryContext()
        {
            return "";
        }
        private string GetLastUserMessageForMemory()
        {
            for (int i = _history.Count - 1; i >= 0; i--)
            {
                var entry = _history[i];
                if (string.Equals(entry.role, "user", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(entry.message) &&
                    !IsAutoCommentaryMessage(entry.message))
                {
                    return entry.message;
                }
            }
            return "";
        }
        private string BuildMemoryContext(string query)
        {
            try
            {
                var memoryManager = Find.World?.GetComponent<AIMemoryManager>();
                if (memoryManager == null)
                {
                    return "";
                }
                bool usedSearch = false;
                List<AIMemoryEntry> memories = null;
                if (!string.IsNullOrWhiteSpace(query))
                {
                    memories = memoryManager.SearchMemories(query, 5);
                    usedSearch = memories != null && memories.Count > 0;
                }
                if (memories == null || memories.Count == 0)
                {
                    memories = memoryManager.GetRecentMemories(5);
                }
                if (memories == null || memories.Count == 0)
                {
                    return "";
                }
                if (Prefs.DevMode)
                {
                    WulaLog.Debug($"[WulaAI] Memory context built ({(usedSearch ? "search" : "recent")}, count={memories.Count}).");
                }
                string lines = string.Join("\n", memories.Select(m => $"- [{m.Category}] {m.Fact}"));
                return "\n\n# LONG-TERM MEMORY (Facts)\n" + lines +
                       "\n(Use 'recall_memories' to search for more, or 'remember_fact' to save new info.)";
            }
            catch (Exception)
            {
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
                    _history.Insert(0, ("system", "[Previous conversation summarized]"));
                    PersistHistory();
                }
            }
        }
        private void TriggerMemoryUpdate()
        {
            if (_memoryUpdateInProgress)
            {
                if (Prefs.DevMode)
                {
                    WulaLog.Debug("[WulaAI] Memory update already running; skipping.");
                }
                return;
            }
            string conversation = BuildMemoryConversation(12);
            if (string.IsNullOrWhiteSpace(conversation))
            {
                if (Prefs.DevMode)
                {
                    WulaLog.Debug("[WulaAI] Memory update skipped (empty conversation).");
                }
                return;
            }
            var memoryManager = Find.World?.GetComponent<AIMemoryManager>();
            if (memoryManager == null)
            {
                return;
            }
            string existingJson = BuildExistingMemoriesJson(memoryManager.GetAllMemories());
            _memoryUpdateInProgress = true;
            if (Prefs.DevMode)
            {
                WulaLog.Debug($"[WulaAI] Memory update started (conversationChars={conversation.Length}).");
            }
            _ = Task.Run(async () =>
            {
                try
                {
                    await UpdateMemoriesFromConversationAsync(memoryManager, existingJson, conversation);
                }
                finally
                {
                    _memoryUpdateInProgress = false;
                }
            });
        }
        private string BuildMemoryConversation(int maxMessages)
        {
            if (_history == null || _history.Count == 0)
            {
                return "";
            }
            var entries = _history
                .Where(h => string.Equals(h.role, "user", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(h.role, "assistant", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (entries.Count == 0)
            {
                return "";
            }
            if (entries.Count > maxMessages)
            {
                entries = entries.Skip(entries.Count - maxMessages).ToList();
            }
            StringBuilder sb = new StringBuilder();
            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.message))
                {
                    continue;
                }
                string role;
                string message = entry.message;
                if (string.Equals(entry.role, "assistant", StringComparison.OrdinalIgnoreCase))
                {
                    message = CleanAssistantForReply(message);
                    if (string.IsNullOrWhiteSpace(message))
                    {
                        continue;
                    }
                    role = "Assistant";
                }
                else
                {
                    role = "User";
                }
                if (IsAutoCommentaryMessage(message))
                {
                    continue;
                }
                sb.AppendLine($"{role}: {message}");
            }
            string conversation = sb.ToString().Trim();
            return TrimForPrompt(conversation, 4000);
        }
        private static string CleanAssistantForReply(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return "";
            }
            return message.Trim();
        }
        private async Task UpdateMemoriesFromConversationAsync(AIMemoryManager memoryManager, string existingMemoriesJson, string conversation)
        {
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
                string factPrompt = MemoryPrompts.BuildFactExtractionPrompt(conversation);
                string factsResponse;
                using (var factsTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(GetAiRequestTimeoutSeconds())))
                {
                    factsResponse = await SendPlainProviderRequestAsync(provider, factPrompt, 256, 0.1f, factsTimeoutCts.Token);
                }
                if (string.IsNullOrWhiteSpace(factsResponse))
                {
                    return;
                }
                var facts = ParseMemoryFacts(factsResponse);
                if (facts.Count == 0)
                {
                    if (Prefs.DevMode)
                    {
                        WulaLog.Debug("[WulaAI] Memory update: no facts extracted.");
                    }
                    return;
                }
                if (Prefs.DevMode)
                {
                    WulaLog.Debug($"[WulaAI] Memory update: extracted {facts.Count} fact(s).");
                }
                string factsJson = BuildFactsJson(facts);
                string updatePrompt = MemoryPrompts.BuildMemoryUpdatePrompt(existingMemoriesJson, factsJson);
                string updateResponse;
                using (var updateTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(GetAiRequestTimeoutSeconds())))
                {
                    updateResponse = await SendPlainProviderRequestAsync(provider, updatePrompt, 512, 0.1f, updateTimeoutCts.Token);
                }
                var updates = ParseMemoryUpdates(updateResponse);
                if (Prefs.DevMode)
                {
                    WulaLog.Debug($"[WulaAI] Memory update: parsed {updates.Count} update(s).");
                }
                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    ApplyMemoryUpdates(memoryManager, updates, facts);
                });
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[WulaAI] Memory update failed: {ex}");
            }
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
                string stability = obj.Value<string>("stability");
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
                    Stability = stability ?? "volatile",
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
            if (!string.Equals(fact.Stability, "stable", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            const float minConfidence = 0.6f;
            return fact.Confidence < 0f || fact.Confidence >= minConfidence;
        }
        private static List<MemoryUpdate> ParseMemoryUpdates(string json)
        {
            var updates = new List<MemoryUpdate>();
            if (string.IsNullOrWhiteSpace(json))
            {
                return updates;
            }
            var root = ParseFirstJsonObject(json);
            var array = root?["memory"] as JArray;
            if (array == null)
            {
                return updates;
            }
            foreach (var token in array)
            {
                var obj = token as JObject;
                if (obj == null)
                {
                    continue;
                }
                string id = obj.Value<string>("id");
                string text = obj.Value<string>("text");
                string category = obj.Value<string>("category");
                string evt = obj.Value<string>("event");
                if (string.IsNullOrWhiteSpace(evt))
                {
                    continue;
                }
                updates.Add(new MemoryUpdate
                {
                    Id = id,
                    Text = text,
                    Category = category,
                    Event = evt
                });
            }
            return updates;
        }
        private static string BuildFactsJson(List<MemoryFact> facts)
        {
            var array = new JArray();
            foreach (var fact in facts)
            {
                if (string.IsNullOrWhiteSpace(fact.Text))
                {
                    continue;
                }
                array.Add(new JObject
                {
                    ["text"] = fact.Text,
                    ["category"] = fact.Category ?? "misc"
                });
            }
            return new JObject { ["facts"] = array }.ToString(Newtonsoft.Json.Formatting.None);
        }
        private static string BuildExistingMemoriesJson(IReadOnlyList<AIMemoryEntry> memories)
        {
            var array = new JArray();
            if (memories != null)
            {
                foreach (var memory in memories)
                {
                    if (memory == null || string.IsNullOrWhiteSpace(memory.Fact))
                    {
                        continue;
                    }
                    array.Add(new JObject
                    {
                        ["id"] = memory.Id,
                        ["text"] = memory.Fact,
                        ["category"] = memory.Category
                    });
                }
            }
            return array.ToString(Newtonsoft.Json.Formatting.None);
        }
        private static void ApplyMemoryUpdates(AIMemoryManager memoryManager, List<MemoryUpdate> updates, List<MemoryFact> fallbackFacts)
        {
            if (memoryManager == null)
            {
                return;
            }
            int appliedCount = 0;
            bool applied = false;
            if (updates != null && updates.Count > 0)
            {
                foreach (var update in updates)
                {
                    string evt = (update.Event ?? "").Trim().ToUpperInvariant();
                    if (evt == "ADD")
                    {
                        memoryManager.AddMemory(update.Text, update.Category);
                        applied = true;
                        appliedCount++;
                    }
                    else if (evt == "UPDATE")
                    {
                        if (!string.IsNullOrWhiteSpace(update.Id))
                        {
                            memoryManager.UpdateMemory(update.Id, update.Text, update.Category);
                            applied = true;
                            appliedCount++;
                        }
                    }
                    else if (evt == "DELETE")
                    {
                        if (!string.IsNullOrWhiteSpace(update.Id))
                        {
                            memoryManager.DeleteMemory(update.Id);
                            applied = true;
                            appliedCount++;
                        }
                    }
                }
            }
            if (!applied && fallbackFacts != null)
            {
                foreach (var fact in fallbackFacts)
                {
                    memoryManager.AddMemory(fact.Text, fact.Category);
                    appliedCount++;
                }
            }
            if (Prefs.DevMode)
            {
                WulaLog.Debug($"[WulaAI] Memory update applied ({appliedCount} change(s)).");
            }
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
        private async Task RunAgentRequestAsync(string transientUserMessage = null, bool triggerMemoryUpdate = true)
        {
            if (_isThinking) return;
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
                    CommitFinalAssistantMessage("Error: API settings not configured in Mod Settings.");
                    return;
                }
                string apiKey = GetConfiguredApiKey(settings);
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    CommitFinalAssistantMessage("Error: API Key not configured in Mod Settings.");
                    return;
                }
                if (settings.reactMaxSeconds > 0f)
                {
                    _activeRequestCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(2f, settings.reactMaxSeconds)));
                }

                CompressHistoryIfNeeded();
                var provider = AIProviderFactory.Create(settings);
                var registry = AIToolRegistry.CreateDefault(settings.enableVlmFeatures);
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

                var messages = BuildCanonicalMessagesForAgent(transientUserMessage);
                await runner.RunAsync(messages, null, null, _activeRequestCts.Token);
                if (triggerMemoryUpdate)
                {
                    TriggerMemoryUpdate();
                }
            }
            catch (OperationCanceledException)
            {
                CommitFinalAssistantMessage("Error: AI request timed out or was cancelled.");
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[WulaAI] Agent request failed: {ex}");
                CommitFinalAssistantMessage("Error: " + ex.Message);
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

        private List<AIMessage> BuildCanonicalMessagesForAgent(string transientUserMessage = null)
        {
            var messages = new List<AIMessage>();
            foreach (var entry in _history ?? new List<(string role, string message)>())
            {
                if (string.IsNullOrWhiteSpace(entry.message)) continue;
                if (IsAutoCommentaryMessage(entry.message)) continue;
                string role = (entry.role ?? "user").Trim().ToLowerInvariant();
                if (role == "user")
                {
                    messages.Add(AIMessage.User(entry.message));
                }
                else if (role == "assistant")
                {
                    string cleaned = CleanAssistantForReply(entry.message);
                    if (!string.IsNullOrWhiteSpace(cleaned))
                    {
                        messages.Add(AIMessage.Assistant(cleaned));
                    }
                }
            }
            if (!string.IsNullOrWhiteSpace(transientUserMessage))
            {
                messages.Add(AIMessage.User(transientUserMessage));
            }
            return messages;
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

            return persona + "\n\n" +
                   "You are connected to the RimWorld game through tools. Use tools for game facts and in-game actions. " +
                   "Never claim an in-game action succeeded unless a tool result confirms it. " +
                   "You may include [EXPR:n] in final replies to set expression (n=1-6). " +
                   "Long-term memory is not preloaded; use recall_memories when needed and remember_fact for durable facts.\n\n" +
                   "# CURRENT RUNTIME STATE\n" +
                   goodwillContext + "\n" +
                   $"Reply language: {language}.";
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
            if (_streamingAssistantActive)
            {
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
            AddAssistantMessage(content);
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

        private Task RunPhasedRequestAsync(string transientUserMessage = null, bool triggerMemoryUpdate = true)
        {
            return RunAgentRequestAsync(transientUserMessage, triggerMemoryUpdate);
        }

    }
}
