using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using WulaFallenEmpire;
using WulaFallenEmpire.EventSystem.AI.Tools;
using WulaFallenEmpire.EventSystem.AI.Utils;
namespace WulaFallenEmpire.EventSystem.AI
{
    public class AIIntelligenceCore : WorldComponent
    {
        public static AIIntelligenceCore Instance { get; private set; }
        public event Action<string> OnMessageReceived;
        public event Action<bool> OnThinkingStateChanged;
        public event Action<int> OnExpressionChanged;
        private List<(string role, string message)> _history = new List<(string role, string message)>();
        private readonly List<AITool> _tools = new List<AITool>();
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
        private bool _lastActionExecuted;
        private bool _lastActionHadError;
        private string _lastActionLedgerNote = "Action Ledger: None (no in-game actions executed).";
        private bool _lastSuccessfulToolCall;
        private string _queryToolLedgerNote = "Tool Ledger (Query): None (no successful tool calls).";
        private string _actionToolLedgerNote = "Tool Ledger (Action): None (no successful tool calls).";
        private bool _querySuccessfulToolCall;
        private bool _actionSuccessfulToolCall;
        private bool _queryRetryUsed;
        private bool _actionRetryUsed;
        private readonly List<string> _actionSuccessLedger = new List<string>();
        private readonly HashSet<string> _actionSuccessLedgerSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _actionFailedLedger = new List<string>();
        private readonly HashSet<string> _actionFailedLedgerSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private SimpleAIClient _currentClient;
        private bool _memoryUpdateInProgress;
        private const int DefaultMaxHistoryTokens = 100000;
        private const int CharsPerToken = 4;
        private const int FixedThinkingPhaseTotal = 3;
        private static readonly Regex ExpressionTagRegex = new Regex(@"\[EXPR\s*:\s*([1-6])\s*\]", RegexOptions.IgnoreCase);
        private const string AutoCommentaryTag = "[AUTO_COMMENTARY]";
        private enum RequestPhase
        {
            QueryTools = 1,
            ActionTools = 2,
            Reply = 3
        }
        private struct PhaseExecutionResult
        {
            public bool AnyToolSuccess;
            public bool AnyActionSuccess;
            public bool AnyActionError;
            public string CapturedImage;
        }
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
        private const string ToolRulesInstruction = @"
# TOOL USE RULES
1.  **FORMATTING**: Tool calls MUST be valid JSON using the following schema:
    { ""tool_calls"": [ { ""type"": ""function"", ""function"": { ""name"": ""tool_name"", ""arguments"": { ... } } } ] }
2.  **STRICT OUTPUT**:
    - Your output MUST be either:
      - A JSON object with ""tool_calls"" (may be empty), OR
      - Exactly: { ""tool_calls"": [] }
    Do NOT include any natural language, explanation, markdown, or additional commentary.
3.  **MULTI-REQUEST RULE**:
    - If the user requests multiple items or information, you MUST output ALL required tool calls in the SAME tool-phase response.
    - Do NOT split multi-item requests across turns.
4.  **TOOLS**: You MAY call any tools listed in ""# TOOLS (AVAILABLE)"".
5.  **ANTI-HALLUCINATION**: Never invent tools, parameters, defNames, coordinates, or tool results. If a tool is needed but not available, output { ""tool_calls"": [] } and proceed to the next phase.";
        private const string QwenToolRulesTemplate = @"
# TOOLS
You may call one or more functions to assist with the user query.

You are provided with function signatures within <tools></tools> XML tags:
<tools>
{tool_descs}
</tools>

For each function call, return a JSON object within <tool_call></tool_call> tags:
<tool_call>
{""name"": ""tool_name"", ""arguments"": { ... }}
</tool_call>

- Output ONLY tool calls in Query/Action phases.
- If no tools are needed, output exactly: {""tool_calls"": []}
- Do NOT include natural language outside tool calls in tool phases.
- Never invent tools, parameters, defNames, coordinates, or tool results.";
        public AIIntelligenceCore(World world) : base(world)
        {
            Instance = this;
            InitializeTools();
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
            if (_history.Count == 0)
            {
                _history.Add(("user", "Hello"));
                PersistHistory();
                RefreshMemoryContext("Hello");
                StartConversation();
                return;
            }
            RefreshMemoryContext(GetLastUserMessageForMemory());
            if (!TryApplyLastAssistantExpression())
            {
                StartConversation();
            }
        }
        public List<(string role, string message)> GetHistorySnapshot()
        {
            return _history?.ToList() ?? new List<(string role, string message)>();
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
            string apiKey = settings.useGeminiProtocol ? settings.geminiApiKey : settings.apiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                WulaLog.Debug("[WulaAI] Auto commentary skipped: API key not configured.");
                return null;
            }
            string baseUrl = settings.useGeminiProtocol ? settings.geminiBaseUrl : settings.baseUrl;
            string model = settings.useGeminiProtocol ? settings.geminiModel : settings.model;
            var client = new SimpleAIClient(apiKey, baseUrl, model, settings.useGeminiProtocol);
            string instruction = GetSystemInstruction(false, "");
            int clampedTokens = Math.Max(32, maxTokens);
            string response = await client.GetChatCompletionAsync(
                instruction,
                new List<(string role, string message)> { ("user", message) },
                maxTokens: clampedTokens,
                temperature: temperature);
            return response?.Trim();
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
            // æ è®°ä¸ºèªå¨è¯è®ºæ¶æ¯ï¼ä¸æ¾ç¤ºå¨å¯¹è¯åå²ï¿½?
            string internalMessage = $"[AUTO_COMMENTARY]\n{eventInfo}";
            // æ·»å å°åå²å¹¶è§¦åæ­£å¸¸ï¿½?AI æèæµï¿½?
            _history.Add(("user", internalMessage));
            PersistHistory();
            // ä½¿ç¨æ­£å¸¸çåé¶æ®µè¯·æ±æµç¨ï¼ï¿½
// å«å·¥ï¿½
// ï¿½è°ç¨è½åç­ï¿½?
            _ = RunPhasedRequestAsync();
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
        private void InitializeTools()
        {
            _tools.Clear();
            _tools.Add(new Tool_SpawnResources());
            _tools.Add(new Tool_ModifyGoodwill());
            _tools.Add(new Tool_SendReinforcement());
            _tools.Add(new Tool_GetPawnStatus());
            _tools.Add(new Tool_GetMapResources());
            _tools.Add(new Tool_GetAvailablePrefabs());
            _tools.Add(new Tool_GetMapPawns());
            _tools.Add(new Tool_GetRecentNotifications());
            _tools.Add(new Tool_CallBombardment());
            _tools.Add(new Tool_GetAvailableBombardments());
            _tools.Add(new Tool_SearchThingDef());
            _tools.Add(new Tool_SearchPawnKind());
            _tools.Add(new Tool_CallPrefabAirdrop());
            _tools.Add(new Tool_SetOverwatchMode());
            _tools.Add(new Tool_RememberFact());
            _tools.Add(new Tool_RecallMemories());
            // Agent å·¥ï¿½
// ï¿½ - ä¿çç»é¢åææªå¾è½åï¼ç§»é¤æææ¨¡ææä½å·¥ï¿½?
            if (WulaFallenEmpireMod.settings?.enableVlmFeatures == true)
            {
                _tools.Add(new Tool_AnalyzeScreen());
            }
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
            return Math.Max(1000, Math.Min(200000, configured));
        }
        private void LoadHistoryForActiveEvent()
        {
            var historyManager = Find.World?.GetComponent<AIHistoryManager>();
            _history = historyManager?.GetHistory(_activeEventDefName) ?? new List<(string role, string message)>();
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
        private string GetSystemInstruction(bool toolsEnabled, string toolsForThisPhase)
        {
            string persona = GetActivePersona();
            string fullInstruction = toolsEnabled
                ? (persona + "\n" + ToolRulesInstruction + "\n" + toolsForThisPhase)
                : persona;
            string language = LanguageDatabase.activeLanguage?.FriendlyNameNative ?? "English";
            var eventVarManager = Find.World?.GetComponent<EventVariableManager>();
            int goodwill = eventVarManager?.GetVariable<int>("Wula_Goodwill_To_PIA", 0) ?? 0;
            string goodwillContext = $"Current Goodwill with P.I.A: {goodwill}. ";
            if (goodwill < -50) goodwillContext += "You are hostile and dismissive towards the player.";
            else if (goodwill < 0) goodwillContext += "You are cold and impatient.";
            else if (goodwill > 50) goodwillContext += "You are somewhat approving and helpful.";
            else goodwillContext += "You are neutral and business-like.";
            if (!toolsEnabled)
            {
                return $"{fullInstruction}\n{goodwillContext}\nIMPORTANT: You MUST reply in the following language: {language}.\n" +
                       "IMPORTANT: Tool calls are DISABLED in this turn. Reply in natural language only. Do NOT output any tool call JSON. " +
                       "You MAY include [EXPR:n] to set your expression (n=1-6).";
            }
            return $"{fullInstruction}\n{goodwillContext}\nIMPORTANT: Output JSON tool calls only. " +
                   $"Final replies are generated later and MUST use: {language}.";
        }
        private string GetNativeSystemInstruction(RequestPhase phase)
        {
            string persona = GetActivePersona();
            string personaBlock = persona;
            string language = LanguageDatabase.activeLanguage?.FriendlyNameNative ?? "English";
            var eventVarManager = Find.World?.GetComponent<EventVariableManager>();
            int goodwill = eventVarManager?.GetVariable<int>("Wula_Goodwill_To_PIA", 0) ?? 0;
            string goodwillContext = $"Current Goodwill with P.I.A: {goodwill}. ";
            if (goodwill < -50) goodwillContext += "You are hostile and dismissive towards the player.";
            else if (goodwill < 0) goodwillContext += "You are cold and impatient.";
            else if (goodwill > 50) goodwillContext += "You are somewhat approving and helpful.";
            else goodwillContext += "You are neutral and business-like.";
            var sb = new StringBuilder();
            sb.AppendLine(personaBlock);
            sb.AppendLine();
            sb.AppendLine(goodwillContext);
            sb.AppendLine($"IMPORTANT: Reply in the following language: {language}.");
            sb.AppendLine("IMPORTANT: Use tools to fetch in-game data or perform actions. Do NOT invent tool results.");
            sb.AppendLine("IMPORTANT: Tool workflow is fixed: Phase 1 = Query Tools, Phase 2 = Action Tools, Phase 3 = Reply.");
            switch (phase)
            {
                case RequestPhase.QueryTools:
                    sb.AppendLine("CURRENT PHASE: Query Tools. Use ONLY query tools (get_*/search_*/analyze_*/recall_memories).");
                    sb.AppendLine("Do NOT reply in natural language. If no query tools are needed, return no tool calls and leave content empty.");
                    break;
                case RequestPhase.ActionTools:
                    sb.AppendLine("CURRENT PHASE: Action Tools. Use ONLY action tools (spawn_resources, send_reinforcement, call_bombardment, modify_goodwill, call_prefab_airdrop, set_overwatch_mode, remember_fact).");
                    sb.AppendLine("Do NOT reply in natural language. If no actions are needed, return no tool calls and leave content empty.");
                    break;
                default:
                    sb.AppendLine("CURRENT PHASE: Reply. Do NOT call any tools. Reply in natural language only.");
                    break;
            }
            sb.AppendLine("IMPORTANT: Long-term memory is not preloaded. Use recall_memories to fetch memories when needed.");
            sb.AppendLine("IMPORTANT: When the user asks for an item by name, call search_thing_def to confirm the exact defName before spawning.");
            sb.AppendLine("You MAY include [EXPR:n] (n=1-6) to set your expression.");
            return sb.ToString().TrimEnd();
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
        private string GetToolSystemInstruction(RequestPhase phase, bool hasImage)
        {
            string persona = GetActivePersona();
            string personaBlock = persona;
            var settings = WulaFallenEmpireMod.settings;
            bool useQwenTemplate = settings != null && !settings.useNativeToolApi;
            string phaseInstruction = GetPhaseInstruction(phase, useQwenTemplate).TrimEnd();
            string toolsForThisPhase = useQwenTemplate ? BuildQwenToolsForPhase(phase) : BuildToolsForPhase(phase);
            string actionPriority = phase == RequestPhase.ActionTools
                ? "ACTION TOOL PRIORITY:\n" +
                  "- spawn_resources\n" +
                  "- send_reinforcement\n" +
                  "- call_bombardment\n" +
                  "- modify_goodwill\n" +
                  "- call_prefab_airdrop\n" +
                  "- set_overwatch_mode\n" +
                  "If no action is required, output exactly: { \"tool_calls\": [] }.\n" +
                  "Query tools exist but are disabled in this phase (not listed here).\n"
                : string.Empty;
            if (hasImage && WulaFallenEmpireMod.settings?.enableVlmFeatures == true)
            {
                phaseInstruction += "\n- NATIVE MULTIMODAL: A current screenshot of the game is attached to this request. You can see the game state directly. Use it to determine coordinates for visual tools or to understand the context.";
                if (phase == RequestPhase.ActionTools)
                {
                    phaseInstruction += "\n- VISUAL PHASE RULE: This phase is for ACTIONS only. If you want to describe the screen to the user, wait for the next phase (Reply Phase). Output JSON tool calls only here.";
                }
            }
            string actionWhitelist = phase == RequestPhase.ActionTools
                ? "ACTION PHASE VALID TOOLS ONLY:\n" +
                  "spawn_resources, send_reinforcement, call_bombardment, modify_goodwill, call_prefab_airdrop, set_overwatch_mode, remember_fact\n" +
                  "INVALID EXAMPLES (do NOT use now): get_map_resources, analyze_screen, search_thing_def, search_pawn_kind, recall_memories\n"
                : string.Empty;
            string toolRules = useQwenTemplate ? null : ToolRulesInstruction.TrimEnd();
            return string.Join("\n\n", new[]
            {
                personaBlock,
                phaseInstruction,
                "IMPORTANT: Long-term memory is not preloaded. Use recall_memories to fetch memories when needed.",
                string.IsNullOrWhiteSpace(actionPriority) ? null : actionPriority.TrimEnd(),
                string.IsNullOrWhiteSpace(actionWhitelist) ? null : actionWhitelist.TrimEnd(),
                toolRules,
                toolsForThisPhase
            }.Where(part => !string.IsNullOrWhiteSpace(part)));
        }
        private string BuildToolsForPhase(RequestPhase phase)
        {
            if (phase == RequestPhase.Reply) return "";
            var settings = WulaFallenEmpireMod.settings;
            var available = _tools
                .Where(t => t != null)
                .Where(t => phase == RequestPhase.QueryTools
                    ? IsQueryToolName(t.Name)
                    : phase == RequestPhase.ActionTools
                        ? IsActionToolName(t.Name)
                        : true)
                .Where(t => !(IsVlmToolName(t.Name) && settings?.enableVlmFeatures != true))
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("====");
            sb.AppendLine();
            sb.AppendLine("# TOOLS (AVAILABLE)");
            sb.AppendLine("Output JSON only with tool_calls. If no tools are needed, output exactly: {\"tool_calls\": []}.");
            sb.AppendLine();
            foreach (var tool in available)
            {
                sb.AppendLine($"## {tool.Name}");
                if (!string.IsNullOrWhiteSpace(tool.Description))
                {
                    sb.AppendLine($"Description: {tool.Description}");
                }
                if (!string.IsNullOrWhiteSpace(tool.UsageSchema))
                {
                    sb.AppendLine($"Usage: {tool.UsageSchema}");
                }
                sb.AppendLine();
            }
            return sb.ToString().TrimEnd();
        }
        private string BuildQwenToolsForPhase(RequestPhase phase)
        {
            if (phase == RequestPhase.Reply) return "";
            var settings = WulaFallenEmpireMod.settings;
            var available = _tools
                .Where(t => t != null)
                .Where(t => phase == RequestPhase.QueryTools
                    ? IsQueryToolName(t.Name)
                    : phase == RequestPhase.ActionTools
                        ? IsActionToolName(t.Name)
                        : true)
                .Where(t => !(IsVlmToolName(t.Name) && settings?.enableVlmFeatures != true))
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var toolLines = new List<string>();
            foreach (var tool in available)
            {
                var def = tool.GetFunctionDefinition();
                if (def != null)
                {
                    toolLines.Add(JsonToolCallParser.SerializeToJson(def));
                }
            }
            string toolDescBlock = string.Join("\n", toolLines);
            return QwenToolRulesTemplate.Replace("{tool_descs}", toolDescBlock).TrimEnd();
        }
        private List<Dictionary<string, object>> BuildNativeToolDefinitions(RequestPhase phase)
        {
            var settings = WulaFallenEmpireMod.settings;
            var available = _tools
                .Where(t => t != null)
                .Where(t => phase == RequestPhase.QueryTools
                    ? IsQueryToolName(t.Name)
                    : phase == RequestPhase.ActionTools
                        ? IsActionToolName(t.Name)
                        : false)
                .Where(t => !(IsVlmToolName(t.Name) && settings?.enableVlmFeatures != true))
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var definitions = new List<Dictionary<string, object>>();
            foreach (var tool in available)
            {
                var def = tool.GetFunctionDefinition();
                if (def != null)
                {
                    definitions.Add(def);
                }
            }
            return definitions;
        }
        private static string GetPhaseInstruction(RequestPhase phase, bool useQwenTemplate)
        {
            string toolOutputRule = useQwenTemplate
                ? "- Output tool calls only using <tool_call>{\"name\":\"tool_name\",\"arguments\":{...}}</tool_call>.\n" +
                  "- If no tools are needed, output exactly: {\"tool_calls\": []}.\n"
                : "- Output JSON tool calls only, or exactly: {\"tool_calls\": []}.\n";
            string outputFooter = useQwenTemplate ? "Output: tool calls only.\n" : "Output: JSON only.\n";
            return phase switch
            {
                RequestPhase.QueryTools =>
                    "# PHASE 1/3 (Query Tools)\n" +
                    "Goal: Gather info needed for decisions.\n" +
                    "Rules:\n" +
                    "- You MUST NOT write any natural language to the user in this phase.\n" +
                    toolOutputRule +
                    "- Prefer query tools (get_*/search_*).\n" +
                    "- CRITICAL: If the user asks for an ITEM (e.g. 'Reviver Mech Serum'), you MUST use search_thing_def with {\"query\":\"...\"} to find its exact DefName. NEVER GUESS DefNames.\n" +
                    "- You MAY call multiple tools in one response, but keep it concise.\n" +
                    "- If the user requests multiple items or information, you MUST output ALL required tool calls in this SAME response.\n" +
                    "- Action tools are available in PHASE 2 only; do NOT use them here.\n" +
                    "After this phase, the game will automatically proceed to PHASE 2.\n" +
                    outputFooter,
                RequestPhase.ActionTools =>
                    "# PHASE 2/3 (Action Tools)\n" +
                    "Goal: Execute in-game actions based on known info.\n" +
                    "Rules:\n" +
                    "- You MUST NOT write any natural language to the user in this phase.\n" +
                    toolOutputRule +
                    "- ONLY action tools are accepted in this phase (spawn_resources, send_reinforcement, call_bombardment, modify_goodwill, call_prefab_airdrop).\n" +
                    "- Query tools (get_*/search_*) will be ignored.\n" +
                    "- Prefer action tools (spawn_resources, send_reinforcement, call_bombardment, modify_goodwill).\n" +
                    "- Avoid queries unless absolutely required.\n" +
                    "- If no action is required based on query results, output {\"tool_calls\": []}.\n" +
                    "- If you already executed the needed action earlier this turn, output {\"tool_calls\": []}.\n" +
                    "After this phase, the game will automatically proceed to PHASE 3.\n" +
                    outputFooter,
                RequestPhase.Reply =>
                    "# PHASE 3/3 (Reply)\n" +
                    "Goal: Reply to the player.\n" +
                    "Rules:\n" +
                    "- Tool calls are DISABLED.\n" +
                    "- You MUST write natural language only.\n" +
                    "- Do NOT output any tool call JSON.\n" +
                    "- If you want to set your expression, include: [EXPR:n] (n=1-6).\n",
                _ => ""
            };
        }
        private static bool IsToolCallJson(string response)
        {
            if (string.IsNullOrWhiteSpace(response)) return false;
            return JsonToolCallParser.TryParseToolCallsFromText(response, out _, out _);
        }
        private static bool IsNoActionOnly(string response)
        {
            if (!JsonToolCallParser.TryParseToolCallsFromText(response, out var toolCalls, out _)) return false;
            return toolCalls.Count == 0;
        }
        private static bool HasActionToolCall(string response)
        {
            if (!JsonToolCallParser.TryParseToolCallsFromText(response, out var toolCalls, out _)) return false;
            foreach (var call in toolCalls)
            {
                if (IsActionToolName(call.Name))
                {
                    return true;
                }
            }
            return false;
        }
        private static string ExtractThoughtFromToolJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            if (!JsonToolCallParser.TryParseObject(json, out var obj)) return null;
            if (!obj.TryGetValue("thought", out object raw) || raw == null) return null;
            string thought = Convert.ToString(raw, CultureInfo.InvariantCulture);
            return string.IsNullOrWhiteSpace(thought) ? null : thought.Trim();
        }
        private void UpdateLatestThought(string thought)
        {
            if (string.IsNullOrWhiteSpace(thought)) return;
            string trimmed = thought.Trim();
            if (string.Equals(_latestThought, trimmed, StringComparison.Ordinal)) return;
            _latestThought = trimmed;
            if (_history != null)
            {
                _history.Add(("trace", $"??: {trimmed}"));
            }
        }
        private static bool LooksLikeNaturalReply(string response)
        {
            if (string.IsNullOrWhiteSpace(response)) return false;
            string trimmed = response.Trim();
            if (JsonToolCallParser.LooksLikeJson(trimmed)) return false;
            return trimmed.Length >= 4;
        }
        private static string BuildNarratorInstruction(int step)
        {
            string recommendation;
            if (step <= 1)
            {
                recommendation = "Recommended phase: QUERY (use query tools only).";
            }
            else if (step % 2 == 0)
            {
                recommendation = "Recommended phase: ACTION (use action tools only).";
            }
            else
            {
                recommendation = "Recommended phase: REPLY. If the task is NOT complete, set follow_recommendation=false and use QUERY tools.";
            }
            return "# NARRATOR\n" +
                   $"Step {step}. {recommendation}\n" +
                   "Question: Do you follow the recommendation?\n" +
                   "Answer yes/no by adding \"follow_recommendation\": true/false in your JSON.\n" +
                   "If you choose REPLY, output exactly {\"tool_calls\": []} (you may include thought).\n";
        }
        private bool IsToolAvailable(string toolName)
        {
            if (string.IsNullOrWhiteSpace(toolName)) return false;
            if (IsVlmToolName(toolName))
            {
                return WulaFallenEmpireMod.settings?.enableVlmFeatures == true;
            }
            return _tools.Any(t => string.Equals(t?.Name, toolName, StringComparison.OrdinalIgnoreCase));
        }
        private static string StripJsonFence(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return input;
            string trimmed = input.Trim();
            if (!trimmed.StartsWith("```", StringComparison.Ordinal)) return trimmed;
            int firstNewline = trimmed.IndexOf('\n');
            if (firstNewline < 0) return trimmed;
            string inner = trimmed.Substring(firstNewline + 1);
            int lastFence = inner.LastIndexOf("```", StringComparison.Ordinal);
            if (lastFence >= 0)
            {
                inner = inner.Substring(0, lastFence);
            }
            return inner.Trim();
        }
        private static bool TryParseToolCallHistory(string message, out List<ToolCallRequest> toolCalls)
        {
            toolCalls = null;
            if (string.IsNullOrWhiteSpace(message)) return false;
            if (!JsonToolCallParser.TryParseToolCallsFromText(message, out var parsedCalls, out _)) return false;
            if (parsedCalls == null || parsedCalls.Count == 0) return false;
            var results = new List<ToolCallRequest>();
            foreach (var call in parsedCalls)
            {
                if (call == null || string.IsNullOrWhiteSpace(call.Name)) continue;
                results.Add(new ToolCallRequest
                {
                    Id = call.Id,
                    Name = call.Name,
                    ArgumentsJson = string.IsNullOrWhiteSpace(call.ArgumentsJson) ? "{}" : call.ArgumentsJson
                });
            }
            if (results.Count == 0) return false;
            toolCalls = results;
            return true;
        }
        private static bool TryParseToolResultHistory(string message, out string toolCallId, out string content)
        {
            toolCallId = null;
            content = null;
            if (string.IsNullOrWhiteSpace(message)) return false;
            const string prefix = "[ToolCallId:";
            if (!message.StartsWith(prefix, StringComparison.Ordinal)) return false;
            int end = message.IndexOf(']');
            if (end < 0) return false;
            toolCallId = message.Substring(prefix.Length, end - prefix.Length).Trim();
            string remainder = message.Substring(end + 1).TrimStart('\r', '\n');
            if (string.IsNullOrWhiteSpace(toolCallId)) return false;
            content = remainder;
            return true;
        }
        private static string StripToolCallIdPrefix(string message)
        {
            if (!TryParseToolResultHistory(message, out _, out string content)) return message;
            return content;
        }
        private static bool ShouldRetryTools(string response)
        {
            if (string.IsNullOrWhiteSpace(response)) return false;
            string cleaned = StripJsonFence(response);
            if (!JsonToolCallParser.TryParseObject(cleaned, out var obj)) return false;
            if (obj.TryGetValue("retry_tools", out object raw) && raw != null)
            {
                if (raw is bool b) return b;
                if (raw is string s && bool.TryParse(s, out bool parsed)) return parsed;
                if (raw is long l) return l != 0;
                if (raw is double d) return Math.Abs(d) > 0.0001;
            }
            return false;
        }
        private static int MaxToolsPerPhase(RequestPhase phase)
        {
            return phase switch
            {
                RequestPhase.QueryTools => 8,
                RequestPhase.ActionTools => 8,
                _ => 0
            };
        }
        private static bool IsActionToolName(string toolName)
        {
            return toolName == "spawn_resources" ||
                   toolName == "send_reinforcement" ||
                   toolName == "call_bombardment" ||
                   toolName == "modify_goodwill" ||
                   toolName == "call_prefab_airdrop" ||
                   toolName == "set_overwatch_mode" ||
                   toolName == "remember_fact";
        }
        private static bool IsQueryToolName(string toolName)
        {
            if (string.IsNullOrWhiteSpace(toolName)) return false;
            return toolName.StartsWith("get_", StringComparison.OrdinalIgnoreCase) ||
                   toolName.StartsWith("search_", StringComparison.OrdinalIgnoreCase) ||
                   toolName.StartsWith("analyze_", StringComparison.OrdinalIgnoreCase) ||
                   toolName == "recall_memories";
        }
        private static bool IsVlmToolName(string toolName)
        {
            return toolName == "analyze_screen" || toolName == "capture_screen";
        }
        private static string SanitizeToolResultForActionPhase(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return message;
            string sanitized = message;
            sanitized = Regex.Replace(sanitized, @"(?m)^ToolRunner\s+(Guidance|Guard|Note):.*(\r?\n)?", "");
            sanitized = Regex.Replace(sanitized, @"(?m)^\s+$", "");
            sanitized = sanitized.Trim();
            return sanitized;
        }
        private static string TrimForPrompt(string text, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            if (text.Length <= maxChars) return text;
            return text.Substring(0, maxChars) + "...(truncated)";
        }
        private List<(string role, string message)> BuildToolContext(RequestPhase phase, int maxToolResults = 2, bool includeUser = true)
        {
            if (_history == null || _history.Count == 0) return new List<(string role, string message)>();
            int lastUserIndex = -1;
            for (int i = _history.Count - 1; i >= 0; i--)
            {
                if (string.Equals(_history[i].role, "user", StringComparison.OrdinalIgnoreCase))
                {
                    lastUserIndex = i;
                    break;
                }
            }
            if (lastUserIndex == -1) return new List<(string role, string message)>();
            var toolEntries = new List<(string role, string message)>();
            for (int i = lastUserIndex + 1; i < _history.Count; i++)
            {
                if (string.Equals(_history[i].role, "tool", StringComparison.OrdinalIgnoreCase))
                {
                    string msg = _history[i].message;
                    if (phase == RequestPhase.ActionTools)
                    {
                        msg = SanitizeToolResultForActionPhase(msg);
                    }
                    toolEntries.Add((_history[i].role, msg));
                }
            }
            if (toolEntries.Count > maxToolResults)
            {
                toolEntries = toolEntries.Skip(toolEntries.Count - maxToolResults).ToList();
            }
            bool includeUserFallback = includeUser || toolEntries.Count == 0;
            var context = new List<(string role, string message)>();
            if (includeUserFallback)
            {
                context.Add(_history[lastUserIndex]);
            }
            context.AddRange(toolEntries);
            return context;
        }
        private List<(string role, string message)> BuildReplyHistory()
        {
            if (_history == null || _history.Count == 0) return new List<(string role, string message)>();
            int lastUserIndex = -1;
            for (int i = _history.Count - 1; i >= 0; i--)
            {
                if (string.Equals(_history[i].role, "user", StringComparison.OrdinalIgnoreCase))
                {
                    lastUserIndex = i;
                    break;
                }
            }
            var filtered = new List<(string role, string message)>();
            for (int i = 0; i < _history.Count; i++)
            {
                var entry = _history[i];
                if (string.Equals(entry.role, "toolcall", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (string.Equals(entry.role, "trace", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (string.Equals(entry.role, "tool", StringComparison.OrdinalIgnoreCase))
                {
                    if (lastUserIndex != -1 && i > lastUserIndex)
                    {
                        string cleanedTool = StripToolCallIdPrefix(entry.message);
                        if (!string.IsNullOrWhiteSpace(cleanedTool))
                        {
                            filtered.Add((entry.role, cleanedTool));
                        }
                    }
                    continue;
                }
                if (!string.Equals(entry.role, "assistant", StringComparison.OrdinalIgnoreCase))
                {
                    filtered.Add(entry);
                    continue;
                }
                string cleaned = CleanAssistantForReply(entry.message);
                if (string.IsNullOrWhiteSpace(cleaned))
                {
                    continue;
                }
                filtered.Add((entry.role, cleaned));
            }
            return filtered;
        }
private List<ChatMessage> BuildNativeHistory()
{
    var messages = new List<ChatMessage>();
    if (_history == null || _history.Count == 0) return messages;
    var toolCallIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var entry in _history)
    {
        if (entry.role == null) continue;
        string role = entry.role.Trim().ToLowerInvariant();
        if (role == "toolcall")
        {
            if (TryParseToolCallHistory(entry.message, out var toolCalls))
            {
                messages.Add(ChatMessage.AssistantWithToolCalls(toolCalls));
                foreach (var call in toolCalls)
                {
                    if (!string.IsNullOrWhiteSpace(call?.Id))
                    {
                        toolCallIds.Add(call.Id);
                    }
                }
            }
            continue;
        }
        if (role == "tool")
        {
            if (TryParseToolResultHistory(entry.message, out string toolCallId, out string toolContent))
            {
                if (!string.IsNullOrWhiteSpace(toolCallId) && toolCallIds.Contains(toolCallId))
                {
                    messages.Add(ChatMessage.ToolResult(toolCallId, toolContent));
                }
            }
            continue;
        }
        if (role == "trace")
        {
            continue;
        }
        if (role == "assistant")
        {
            string cleaned = CleanAssistantForReply(entry.message);
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                continue;
            }
            messages.Add(ChatMessage.Assistant(cleaned));
            continue;
        }
        if (role == "system")
        {
            if (!string.IsNullOrWhiteSpace(entry.message))
            {
                messages.Add(new ChatMessage { Role = "system", Content = entry.message });
            }
            continue;
        }
        if (!string.IsNullOrWhiteSpace(entry.message))
        {
            messages.Add(ChatMessage.User(entry.message));
        }
    }
    return messages;
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
            string cleaned = message;
            cleaned = Regex.Replace(cleaned, @"<tool_call>.*?</tool_call>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"```[\s\S]*?```", match =>
            {
                string block = match.Value ?? "";
                return block.IndexOf("tool_calls", StringComparison.OrdinalIgnoreCase) >= 0 ? "" : block;
            });
            cleaned = StripToolCallJson(cleaned)?.Trim() ?? "";
            return cleaned.Trim();
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
                string apiKey = settings.useGeminiProtocol ? settings.geminiApiKey : settings.apiKey;
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    return;
                }
                string baseUrl = settings.useGeminiProtocol ? settings.geminiBaseUrl : settings.baseUrl;
                string model = settings.useGeminiProtocol ? settings.geminiModel : settings.model;
                var client = new SimpleAIClient(apiKey, baseUrl, model, settings.useGeminiProtocol);
                string factPrompt = MemoryPrompts.BuildFactExtractionPrompt(conversation);
                string factsResponse = await client.GetChatCompletionAsync(factPrompt, new List<(string role, string message)>(), maxTokens: 256, temperature: 0.1f);
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
                string updateResponse = await client.GetChatCompletionAsync(updatePrompt, new List<(string role, string message)>(), maxTokens: 512, temperature: 0.1f);
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
        private static List<MemoryFact> ParseMemoryFacts(string json)
        {
            var facts = new List<MemoryFact>();
            if (string.IsNullOrWhiteSpace(json))
            {
                return facts;
            }
            string array = ExtractJsonArray(json, "facts");
            if (string.IsNullOrWhiteSpace(array))
            {
                return facts;
            }
            foreach (string obj in ExtractJsonObjects(array))
            {
                var dict = SimpleJsonParser.Parse(obj);
                if (dict == null || dict.Count == 0)
                {
                    continue;
                }
                if (!dict.TryGetValue("text", out string text) || string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }
                dict.TryGetValue("category", out string category);
                dict.TryGetValue("stability", out string stability);
                float confidence = -1f;
                if (dict.TryGetValue("confidence", out string confidenceRaw) &&
                    float.TryParse(confidenceRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
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
            string array = ExtractJsonArray(json, "memory");
            if (string.IsNullOrWhiteSpace(array))
            {
                return updates;
            }
            foreach (string obj in ExtractJsonObjects(array))
            {
                var dict = SimpleJsonParser.Parse(obj);
                if (dict == null || dict.Count == 0)
                {
                    continue;
                }
                dict.TryGetValue("id", out string id);
                dict.TryGetValue("text", out string text);
                dict.TryGetValue("category", out string category);
                dict.TryGetValue("event", out string evt);
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
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"facts\":[");
            bool first = true;
            foreach (var fact in facts)
            {
                if (string.IsNullOrWhiteSpace(fact.Text))
                {
                    continue;
                }
                if (!first) sb.Append(",");
                first = false;
                sb.Append("{\"text\":\"").Append(EscapeJson(fact.Text)).Append("\",");
                sb.Append("\"category\":\"").Append(EscapeJson(fact.Category ?? "misc")).Append("\"}");
            }
            sb.Append("]}");
            return sb.ToString();
        }
        private static string BuildExistingMemoriesJson(IReadOnlyList<AIMemoryEntry> memories)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("[");
            bool first = true;
            if (memories != null)
            {
                foreach (var memory in memories)
                {
                    if (memory == null || string.IsNullOrWhiteSpace(memory.Fact))
                    {
                        continue;
                    }
                    if (!first) sb.Append(",");
                    first = false;
                    sb.Append("{\"id\":\"").Append(EscapeJson(memory.Id)).Append("\",");
                    sb.Append("\"text\":\"").Append(EscapeJson(memory.Fact)).Append("\",");
                    sb.Append("\"category\":\"").Append(EscapeJson(memory.Category)).Append("\"}");
                }
            }
            sb.Append("]");
            return sb.ToString();
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
        private static string ExtractJsonArray(string json, string key)
        {
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(key))
            {
                return null;
            }
            string keyPattern = $"\"{key}\"";
            int keyIndex = json.IndexOf(keyPattern, StringComparison.OrdinalIgnoreCase);
            if (keyIndex == -1)
            {
                return null;
            }
            int arrayStart = json.IndexOf('[', keyIndex);
            if (arrayStart == -1)
            {
                return null;
            }
            int arrayEnd = FindMatchingBracket(json, arrayStart);
            if (arrayEnd == -1)
            {
                return null;
            }
            return json.Substring(arrayStart + 1, arrayEnd - arrayStart - 1);
        }
        private static List<string> ExtractJsonObjects(string arrayContent)
        {
            var objects = new List<string>();
            if (string.IsNullOrWhiteSpace(arrayContent))
            {
                return objects;
            }
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
        private static string BuildActionSignature(string toolName, Dictionary<string, object> args)
        {
            if (string.IsNullOrWhiteSpace(toolName)) return "";
            string normalizedArgs = args == null ? "{}" : SerializeCanonicalJson(args);
            return $"{toolName}:{normalizedArgs}";
        }
        private static string SerializeCanonicalJson(object value)
        {
            var sb = new StringBuilder();
            AppendCanonicalJson(sb, value);
            return sb.ToString();
        }
        private static void AppendCanonicalJson(StringBuilder sb, object value)
        {
            if (value == null)
            {
                sb.Append("null");
                return;
            }
            if (value is string s)
            {
                sb.Append('"').Append(EscapeJson(s)).Append('"');
                return;
            }
            if (value is bool b)
            {
                sb.Append(b ? "true" : "false");
                return;
            }
            if (value is double d)
            {
                sb.Append(d.ToString("0.################", CultureInfo.InvariantCulture));
                return;
            }
            if (value is float f)
            {
                sb.Append(f.ToString("0.################", CultureInfo.InvariantCulture));
                return;
            }
            if (value is int or long or short or byte)
            {
                sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                return;
            }
            if (value is Dictionary<string, object> dict)
            {
                sb.Append('{');
                bool first = true;
                foreach (var key in dict.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append('"').Append(EscapeJson(key ?? "")).Append('"').Append(':');
                    dict.TryGetValue(key, out object child);
                    AppendCanonicalJson(sb, child);
                }
                sb.Append('}');
                return;
            }
            if (value is List<object> list)
            {
                sb.Append('[');
                for (int i = 0; i < list.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    AppendCanonicalJson(sb, list[i]);
                }
                sb.Append(']');
                return;
            }
            sb.Append('"').Append(EscapeJson(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "")).Append('"');
        }
        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }
        private static string StripToolCallJson(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (!JsonToolCallParser.TryParseToolCallsFromText(text, out _, out string fragment))
            {
                return text;
            }
            int index = text.IndexOf(fragment, StringComparison.Ordinal);
            if (index < 0) return text;
            string cleaned = text.Remove(index, fragment.Length);
            return cleaned.Trim();
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
            }
        }
        private async Task RunPhasedRequestAsync()
        {
            if (_isThinking) return;
            SetThinkingState(true);
            SetThinkingPhase(1, false);
            ResetTurnState();
            try
            {
                CompressHistoryIfNeeded();
                var settings = WulaFallenEmpireMod.settings;
                if (settings == null)
                {
                    AddAssistantMessage("Error: API settings not configured in Mod Settings.");
                    return;
                }
                string apiKey = settings.useGeminiProtocol ? settings.geminiApiKey : settings.apiKey;
                if (string.IsNullOrEmpty(apiKey))
                {
                    AddAssistantMessage("Error: API Key not configured in Mod Settings.");
                    return;
                }
                string baseUrl = settings.useGeminiProtocol ? settings.geminiBaseUrl : settings.baseUrl;
                string model = settings.useGeminiProtocol ? settings.geminiModel : settings.model;
                var client = new SimpleAIClient(apiKey, baseUrl, model, settings.useGeminiProtocol);
                _currentClient = client;
                bool useQwenTemplate = !settings.useNativeToolApi;
                if (settings.useNativeToolApi)
                {
                    await RunNativeToolLoopAsync(client, settings);
                    return;
                }
                // Model-Driven Vision: Start with null image. The model must request it using analyze_screen or capture_screen if needed.
                string base64Image = null;
                var queryPhase = RequestPhase.QueryTools;
                if (Prefs.DevMode)
                {
                    WulaLog.Debug($"[WulaAI] ===== Turn 1/3 ({queryPhase}) =====");
                }
                string queryInstruction = GetToolSystemInstruction(queryPhase, !string.IsNullOrEmpty(base64Image));
                string queryResponse = await client.GetChatCompletionAsync(queryInstruction, BuildToolContext(queryPhase), maxTokens: 2048, temperature: 0.1f, base64Image: base64Image);
                if (string.IsNullOrEmpty(queryResponse))
                {
                    AddAssistantMessage("Wula_AI_Error_ConnectionLost".Translate());
                    return;
                }
                if (!IsToolCallJson(queryResponse))
                {
                    if (Prefs.DevMode)
                    {
                        WulaLog.Debug("[WulaAI] Turn 1/3 missing JSON tool calls; treating as no_action.");
                    }
                    queryResponse = "{\"tool_calls\": []}";
                }
                PhaseExecutionResult queryResult = await ExecuteJsonToolsForPhase(queryResponse, queryPhase);
                // DATA FLOW: If Query Phase captured an image, propagate it to subsequent phases.
                if (!string.IsNullOrEmpty(queryResult.CapturedImage))
                {
                    base64Image = queryResult.CapturedImage;
                }
                if (!queryResult.AnyToolSuccess && !_queryRetryUsed)
                {
                    _queryRetryUsed = true;
                    string lastUserMessage = _history.LastOrDefault(entry => entry.role == "user").message ?? "";
                    string persona = GetActivePersona();
                    string retryInstruction = persona +
                                              "\n\n# RETRY DECISION\n" +
                                              "No successful tool calls occurred in PHASE 1 (Query).\n" +
                                              "If you need to use tools in PHASE 1, output exactly: {\"retry_tools\": true}.\n" +
                                              "If you will proceed without actions, output exactly: {\"retry_tools\": false}.\n" +
                                              "Output JSON only and NOTHING else.\n" +
                                              "\nLast user request:\n" + lastUserMessage;
                    string retryDecision = await client.GetChatCompletionAsync(retryInstruction, new List<(string role, string message)>(), maxTokens: 256, temperature: 0.1f);
                    if (!string.IsNullOrEmpty(retryDecision) && ShouldRetryTools(retryDecision))
                    {
                        if (Prefs.DevMode)
                        {
                            WulaLog.Debug("[WulaAI] Retry requested; re-opening query phase once.");
                        }
                        SetThinkingPhase(1, true);
                        string retryQueryInstruction = GetToolSystemInstruction(queryPhase, !string.IsNullOrEmpty(base64Image)) +
                                                       "\n\n# RETRY\nYou chose to retry. Output JSON tool calls only (or {\"tool_calls\": []}).";
                        string retryQueryResponse = await client.GetChatCompletionAsync(retryQueryInstruction, BuildToolContext(queryPhase), maxTokens: 2048, temperature: 0.1f, base64Image: base64Image);
                        if (string.IsNullOrEmpty(retryQueryResponse))
                        {
                            AddAssistantMessage("Wula_AI_Error_ConnectionLost".Translate());
                            return;
                        }
                        if (!IsToolCallJson(retryQueryResponse))
                        {
                            if (Prefs.DevMode)
                            {
                                WulaLog.Debug("[WulaAI] Retry query phase missing JSON tool calls; treating as no_action.");
                            }
                            retryQueryResponse = "{\"tool_calls\": []}";
                        }
                        queryResult = await ExecuteJsonToolsForPhase(retryQueryResponse, queryPhase);
                    }
                }
                var actionPhase = RequestPhase.ActionTools;
                if (Prefs.DevMode)
                {
                    WulaLog.Debug($"[WulaAI] ===== Turn 2/3 ({actionPhase}) =====");
                }
                SetThinkingPhase(2, false);
                string actionInstruction = GetToolSystemInstruction(actionPhase, !string.IsNullOrEmpty(base64Image));
                var actionContext = BuildToolContext(actionPhase, includeUser: true);
                // Important: Pass base64Image to Action Phase as well if available, so visual_click works.
                string actionResponse = await client.GetChatCompletionAsync(actionInstruction, actionContext, maxTokens: 2048, temperature: 0.1f, base64Image: base64Image);
                if (string.IsNullOrEmpty(actionResponse))
                {
                    AddAssistantMessage("Wula_AI_Error_ConnectionLost".Translate());
                    return;
                }
                bool actionHasJson = IsToolCallJson(actionResponse);
                bool actionIsNoActionOnly = actionHasJson && IsNoActionOnly(actionResponse);
                bool actionHasActionTool = actionHasJson && HasActionToolCall(actionResponse);
                if (!actionHasJson || (!actionHasActionTool && !actionIsNoActionOnly))
                {
                    if (Prefs.DevMode)
                    {
                        WulaLog.Debug("[WulaAI] Turn 2/3 missing JSON or no action tool; attempting JSON-only conversion.");
                    }
                    string fixInstruction = "# FORMAT FIX (ACTION JSON ONLY)\n" +
                                            "Preserve the intent of the previous output.\n" +
                                            "If the previous output indicates no action is needed or refuses action, output exactly: {\"tool_calls\": []}.\n" +
                                            "Do NOT invent new actions.\n" +
                                            "Output VALID JSON tool calls only. No natural language, no commentary.\nIgnore any non-JSON text.\n" +
                                            "Allowed tools: spawn_resources, send_reinforcement, call_bombardment, modify_goodwill, call_prefab_airdrop, set_overwatch_mode, remember_fact.\n" +
                                            "Schema: {\"tool_calls\":[{\"type\":\"function\",\"function\":{\"name\":\"tool_name\",\"arguments\":{...}}}]}\n" +
                                            "\nPrevious output:\n" + TrimForPrompt(actionResponse, 600);
                    string fixedResponse = await client.GetChatCompletionAsync(fixInstruction, actionContext, maxTokens: 2048, temperature: 0.1f);
                    bool fixedHasJson = !string.IsNullOrEmpty(fixedResponse) && IsToolCallJson(fixedResponse);
                    bool fixedIsNoActionOnly = fixedHasJson && IsNoActionOnly(fixedResponse);
                    bool fixedHasActionTool = fixedHasJson && HasActionToolCall(fixedResponse);
                    if (fixedHasJson && (fixedHasActionTool || fixedIsNoActionOnly))
                    {
                        actionResponse = fixedResponse;
                    }
                    else
                    {
                        if (Prefs.DevMode)
                        {
                            WulaLog.Debug("[WulaAI] Turn 2/3 conversion failed; treating as no_action.");
                        }
                        actionResponse = "{\"tool_calls\": []}";
                    }
                }
                PhaseExecutionResult actionResult = await ExecuteJsonToolsForPhase(actionResponse, actionPhase);
                if (!actionResult.AnyActionSuccess && !_actionRetryUsed)
                {
                    _actionRetryUsed = true;
                    string lastUserMessage = _history.LastOrDefault(entry => entry.role == "user").message ?? "";
                    string persona = GetActivePersona();
                    string retryInstruction = persona +
                                              "\n\n# RETRY DECISION\n" +
                                              "No successful action tools occurred in PHASE 2 (Action).\n" +
                                              "If you need to execute an in-game action, output exactly: {\"retry_tools\": true}.\n" +
                                              "If you will proceed without actions, output exactly: {\"retry_tools\": false}.\n" +
                                              "Output JSON only and NOTHING else.\n" +
                                              "\nLast user request:\n" + lastUserMessage;
                    string retryDecision = await client.GetChatCompletionAsync(retryInstruction, new List<(string role, string message)>(), maxTokens: 256, temperature: 0.1f);
                    if (!string.IsNullOrEmpty(retryDecision) && ShouldRetryTools(retryDecision))
                    {
                        if (Prefs.DevMode)
                        {
                            WulaLog.Debug("[WulaAI] Retry requested; re-opening action phase once.");
                        }
                        SetThinkingPhase(2, true);
                        string retryActionInstruction = GetToolSystemInstruction(actionPhase, !string.IsNullOrEmpty(base64Image)) +
                                                         "\n\n# RETRY\nYou chose to retry. Output JSON tool calls only (or {\"tool_calls\": []}).";
                        var retryActionContext = BuildToolContext(actionPhase, includeUser: true);
                        string retryActionResponse = await client.GetChatCompletionAsync(retryActionInstruction, retryActionContext, maxTokens: 2048, temperature: 0.1f, base64Image: base64Image);
                        if (string.IsNullOrEmpty(retryActionResponse))
                        {
                            AddAssistantMessage("Wula_AI_Error_ConnectionLost".Translate());
                            return;
                        }
                        if (!IsToolCallJson(retryActionResponse))
                        {
                            if (Prefs.DevMode)
                            {
                                WulaLog.Debug("[WulaAI] Retry action phase missing JSON; attempting JSON-only conversion.");
                            }
                            string retryFixInstruction = "# FORMAT FIX (ACTION JSON ONLY)\n" +
                                                        "Preserve the intent of the previous output.\n" +
                                                        "If the previous output indicates no action is needed or refuses action, output exactly: {\"tool_calls\": []}.\n" +
                                                        "Do NOT invent new actions.\n" +
                                                        "Output VALID JSON tool calls only. No natural language, no commentary.\nIgnore any non-JSON text.\n" +
                                                        "Allowed tools: spawn_resources, send_reinforcement, call_bombardment, modify_goodwill, call_prefab_airdrop, set_overwatch_mode, remember_fact.\n" +
                                                        "Schema: {\"tool_calls\":[{\"type\":\"function\",\"function\":{\"name\":\"tool_name\",\"arguments\":{...}}}]}\n" +
                                                        "\nPrevious output:\n" + TrimForPrompt(retryActionResponse, 600);
                            string retryFixedResponse = await client.GetChatCompletionAsync(retryFixInstruction, retryActionContext, maxTokens: 2048, temperature: 0.1f);
                            bool retryFixedHasJson = !string.IsNullOrEmpty(retryFixedResponse) && IsToolCallJson(retryFixedResponse);
                            bool retryFixedIsNoActionOnly = retryFixedHasJson && IsNoActionOnly(retryFixedResponse);
                            bool retryFixedHasActionTool = retryFixedHasJson && HasActionToolCall(retryFixedResponse);
                            if (retryFixedHasJson && (retryFixedHasActionTool || retryFixedIsNoActionOnly))
                            {
                                retryActionResponse = retryFixedResponse;
                            }
                            else
                            {
                                if (Prefs.DevMode)
                                {
                                    WulaLog.Debug("[WulaAI] Retry action conversion failed; treating as no_action.");
                                }
                                retryActionResponse = "{\"tool_calls\": []}";
                            }
                        }
                        actionResult = await ExecuteJsonToolsForPhase(retryActionResponse, actionPhase);
                    }
                }
                _lastSuccessfulToolCall = _querySuccessfulToolCall || _actionSuccessfulToolCall;
                var replyPhase = RequestPhase.Reply;
                if (Prefs.DevMode)
                {
                    WulaLog.Debug($"[WulaAI] ===== Turn 3/3 ({replyPhase}) =====");
                }
                SetThinkingPhase(3, false);
                string replyInstruction = GetSystemInstruction(false, "") + "\n\n" + GetPhaseInstruction(replyPhase, useQwenTemplate);
                if (!string.IsNullOrWhiteSpace(_queryToolLedgerNote))
                {
                    replyInstruction += "\n" + _queryToolLedgerNote;
                }
                if (!string.IsNullOrWhiteSpace(_actionToolLedgerNote))
                {
                    replyInstruction += "\n" + _actionToolLedgerNote;
                }
                if (!string.IsNullOrWhiteSpace(_lastActionLedgerNote))
                {
                    replyInstruction += "\n" + _lastActionLedgerNote +
                                        "\nIMPORTANT: Do NOT claim any in-game actions beyond the Action Ledger. If the ledger is None, you MUST NOT claim any deliveries, reinforcements, or bombardments.";
                }
                if (_lastActionExecuted)
                {
                    replyInstruction += "\nIMPORTANT: Actions in the Action Ledger were executed in-game. You MUST acknowledge them as completed in your reply. You MUST NOT deny, retract, or contradict them.";
                }
                if (!_lastSuccessfulToolCall)
                {
                    replyInstruction += "\nIMPORTANT: No successful tool calls occurred in the tool phases. You MUST NOT claim any tools or actions succeeded.";
                }
                if (_lastActionHadError)
                {
                    replyInstruction += "\nIMPORTANT: An action tool failed. You MUST acknowledge the failure and MUST NOT claim success.";
                    if (_lastActionExecuted)
                    {
                        replyInstruction += " You MUST still confirm any successful actions separately.";
                    }
                }
                // VISUAL CONTEXT FOR REPLY: Pass the image so the AI can describe what it sees.
                string reply = await client.GetChatCompletionAsync(replyInstruction, BuildReplyHistory(), base64Image: base64Image);
                if (string.IsNullOrEmpty(reply))
                {
                    AddAssistantMessage("Wula_AI_Error_ConnectionLost".Translate());
                    return;
                }
                bool replyHadToolCalls = IsToolCallJson(reply);
                string strippedReply = StripToolCallJson(reply)?.Trim() ?? "";
                if (replyHadToolCalls || string.IsNullOrWhiteSpace(strippedReply))
                {
                    string retryReplyInstruction = replyInstruction +
                                                  "\n\n# RETRY (REPLY OUTPUT)\n" +
                                                  "Your last reply included tool call JSON or was empty. Tool calls are DISABLED.\n" +
                                                  "You MUST reply in natural language only. Do NOT output any tool call JSON.\n";
                    string retryReply = await client.GetChatCompletionAsync(retryReplyInstruction, BuildReplyHistory(), maxTokens: 256, temperature: 0.3f);
                    if (!string.IsNullOrEmpty(retryReply))
                    {
                        reply = retryReply;
                        replyHadToolCalls = IsToolCallJson(reply);
                        strippedReply = StripToolCallJson(reply)?.Trim() ?? "";
                    }
                }
                if (replyHadToolCalls)
                {
                    string cleaned = StripToolCallJson(reply)?.Trim() ?? "";
                    if (string.IsNullOrWhiteSpace(cleaned))
                    {
                        cleaned = "(system) AI reply returned tool call JSON only and was discarded. Please retry or send /clear to reset context.";
                    }
                    reply = cleaned;
                }
                AddAssistantMessage(reply);
                TriggerMemoryUpdate();
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[WulaAI] Exception in RunPhasedRequestAsync: {ex}");
                AddAssistantMessage("Wula_AI_Error_Internal".Translate(ex.Message));
            }
            finally
            {
                SetThinkingState(false);
            }
        }
        private async Task RunNativeToolLoopAsync(SimpleAIClient client, WulaFallenEmpireSettings settings)
        {
            var messages = BuildNativeHistory();
            string base64Image = null;
            var queryPhase = RequestPhase.QueryTools;
            if (Prefs.DevMode)
            {
                WulaLog.Debug("[WulaAI] ===== Turn 1/3 (QueryTools) =====");
            }
            SetThinkingPhase(1, false);
            string queryInstruction = GetNativeSystemInstruction(queryPhase);
            var queryTools = BuildNativeToolDefinitions(queryPhase);
            ChatCompletionResult queryResponse = await client.GetChatCompletionWithToolsAsync(
                queryInstruction,
                messages,
                queryTools,
                maxTokens: 2048,
                temperature: 0.2f,
                toolChoice: "required");
            if (queryResponse == null)
            {
                AddAssistantMessage("Wula_AI_Error_ConnectionLost".Translate());
                return;
            }
            PhaseExecutionResult queryResult = await ExecuteNativeToolsForPhase(queryResponse, queryPhase, messages);
            if (!string.IsNullOrWhiteSpace(queryResult.CapturedImage))
            {
                base64Image = queryResult.CapturedImage;
            }
            if (!queryResult.AnyToolSuccess && !_queryRetryUsed)
            {
                _queryRetryUsed = true;
                string lastUserMessage = _history.LastOrDefault(entry => entry.role == "user").message ?? "";
                string persona = GetActivePersona();
                string retryInstruction = persona +
                                          "\n\n# RETRY DECISION\n" +
                                          "No successful tool calls occurred in PHASE 1 (Query).\n" +
                                          "If you need to use tools in PHASE 1, output exactly: {\"retry_tools\": true}.\n" +
                                          "If you will proceed without actions, output exactly: {\"retry_tools\": false}.\n" +
                                          "Output JSON only and NOTHING else.\n\n" +
                                          "Last user request:\n" + lastUserMessage;
                string retryDecision = await client.GetChatCompletionAsync(retryInstruction, new List<(string role, string message)>(), maxTokens: 256, temperature: 0.1f);
                if (!string.IsNullOrEmpty(retryDecision) && ShouldRetryTools(retryDecision))
                {
                    if (Prefs.DevMode)
                    {
                        WulaLog.Debug("[WulaAI] Retry requested; re-opening query phase once.");
                    }
                    SetThinkingPhase(1, true);
                    string retryQueryInstruction = GetNativeSystemInstruction(queryPhase) +
                                                   "\n\n# RETRY\n" +
                                                   "You chose to retry. Output JSON tool calls only (or {\"tool_calls\": []}).";
                    ChatCompletionResult retryQueryResponse = await client.GetChatCompletionWithToolsAsync(
                        retryQueryInstruction,
                        messages,
                        queryTools,
                        maxTokens: 2048,
                        temperature: 0.2f,
                        toolChoice: "required");
                    if (retryQueryResponse == null)
                    {
                        AddAssistantMessage("Wula_AI_Error_ConnectionLost".Translate());
                        return;
                    }
                    queryResult = await ExecuteNativeToolsForPhase(retryQueryResponse, queryPhase, messages);
                    if (!string.IsNullOrWhiteSpace(queryResult.CapturedImage))
                    {
                        base64Image = queryResult.CapturedImage;
                    }
                }
            }
            var actionPhase = RequestPhase.ActionTools;
            if (Prefs.DevMode)
            {
                WulaLog.Debug("[WulaAI] ===== Turn 2/3 (ActionTools) =====");
            }
            SetThinkingPhase(2, false);
            string actionInstruction = GetNativeSystemInstruction(actionPhase);
            var actionTools = BuildNativeToolDefinitions(actionPhase);
            ChatCompletionResult actionResponse = await client.GetChatCompletionWithToolsAsync(
                actionInstruction,
                messages,
                actionTools,
                maxTokens: 2048,
                temperature: 0.2f,
                toolChoice: "auto");
            if (actionResponse == null)
            {
                AddAssistantMessage("Wula_AI_Error_ConnectionLost".Translate());
                return;
            }
            PhaseExecutionResult actionResult = await ExecuteNativeToolsForPhase(actionResponse, actionPhase, messages);
            if (!actionResult.AnyActionSuccess && !_actionRetryUsed)
            {
                _actionRetryUsed = true;
                string lastUserMessage = _history.LastOrDefault(entry => entry.role == "user").message ?? "";
                string persona = GetActivePersona();
                string retryInstruction = persona +
                                          "\n\n# RETRY DECISION\n" +
                                          "No successful action tools occurred in PHASE 2 (Action).\n" +
                                          "If you need to execute an in-game action, output exactly: {\"retry_tools\": true}.\n" +
                                          "If you will proceed without actions, output exactly: {\"retry_tools\": false}.\n" +
                                          "Output JSON only and NOTHING else.\n\n" +
                                          "Last user request:\n" + lastUserMessage;
                string retryDecision = await client.GetChatCompletionAsync(retryInstruction, new List<(string role, string message)>(), maxTokens: 256, temperature: 0.1f);
                if (!string.IsNullOrEmpty(retryDecision) && ShouldRetryTools(retryDecision))
                {
                    if (Prefs.DevMode)
                    {
                        WulaLog.Debug("[WulaAI] Retry requested; re-opening action phase once.");
                    }
                    SetThinkingPhase(2, true);
                    string retryActionInstruction = GetNativeSystemInstruction(actionPhase) +
                                                    "\n\n# RETRY\n" +
                                                    "You chose to retry. Output JSON tool calls only (or {\"tool_calls\": []}).";
                    ChatCompletionResult retryActionResponse = await client.GetChatCompletionWithToolsAsync(
                        retryActionInstruction,
                        messages,
                        actionTools,
                        maxTokens: 2048,
                        temperature: 0.2f,
                        toolChoice: "auto");
                    if (retryActionResponse == null)
                    {
                        AddAssistantMessage("Wula_AI_Error_ConnectionLost".Translate());
                        return;
                    }
                    actionResult = await ExecuteNativeToolsForPhase(retryActionResponse, actionPhase, messages);
                }
            }
            _lastSuccessfulToolCall = _querySuccessfulToolCall || _actionSuccessfulToolCall;
            var replyPhase = RequestPhase.Reply;
            if (Prefs.DevMode)
            {
                WulaLog.Debug("[WulaAI] ===== Turn 3/3 (Reply) =====");
            }
            SetThinkingPhase(3, false);
            string replyInstruction = GetSystemInstruction(false, "") + "\n\n" + GetPhaseInstruction(replyPhase, false);
            if (!string.IsNullOrWhiteSpace(_queryToolLedgerNote))
            {
                replyInstruction += "\n" + _queryToolLedgerNote;
            }
            if (!string.IsNullOrWhiteSpace(_actionToolLedgerNote))
            {
                replyInstruction += "\n" + _actionToolLedgerNote;
            }
            if (!string.IsNullOrWhiteSpace(_lastActionLedgerNote))
            {
                replyInstruction += "\n" + _lastActionLedgerNote +
                                    "\nIMPORTANT: Do NOT claim any in-game actions beyond the Action Ledger. If the ledger is None, you MUST NOT claim any deliveries, reinforcements, or bombardments.";
            }
            if (_lastActionExecuted)
            {
                replyInstruction += "\nIMPORTANT: Actions in the Action Ledger were executed in-game. You MUST acknowledge them as completed in your reply. You MUST NOT deny, retract, or contradict them.";
            }
            if (!_lastSuccessfulToolCall)
            {
                replyInstruction += "\nIMPORTANT: No successful tool calls occurred in the tool phases. You MUST NOT claim any tools or actions succeeded.";
            }
            if (_lastActionHadError)
            {
                replyInstruction += "\nIMPORTANT: An action tool failed. You MUST acknowledge the failure and MUST NOT claim success.";
                if (_lastActionExecuted)
                {
                    replyInstruction += " You MUST still confirm any successful actions separately.";
                }
            }
            string reply = await client.GetChatCompletionAsync(replyInstruction, BuildReplyHistory(), maxTokens: null, temperature: null, base64Image: base64Image, toolChoice: "none");
            if (string.IsNullOrEmpty(reply))
            {
                AddAssistantMessage("Wula_AI_Error_ConnectionLost".Translate());
                return;
            }
            bool replyHadToolCalls = IsToolCallJson(reply);
            string strippedReply = StripToolCallJson(reply)?.Trim() ?? "";
            if (replyHadToolCalls)
            {
                string retryReplyInstruction = replyInstruction +
                                               "\n\n# RETRY (REPLY OUTPUT)\n" +
                                               "Your last reply included tool call JSON or was empty. Tool calls are DISABLED.\n" +
                                               "You MUST reply in natural language only. Do NOT output any tool call JSON.\n";
                string retryReply = await client.GetChatCompletionAsync(retryReplyInstruction, BuildReplyHistory(), maxTokens: 256, temperature: 0.3f);
                if (!string.IsNullOrEmpty(retryReply))
                {
                    reply = retryReply;
                    replyHadToolCalls = IsToolCallJson(reply);
                    strippedReply = StripToolCallJson(reply)?.Trim() ?? "";
                }
            }
            if (replyHadToolCalls)
            {
                string cleaned = StripToolCallJson(reply)?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(cleaned))
                {
                    cleaned = "(system) AI reply returned tool call JSON only and was discarded. Please retry or send /clear to reset context.";
                }
                reply = cleaned;
            }
            AddAssistantMessage(reply);
            TriggerMemoryUpdate();
        }
        private async Task<PhaseExecutionResult> ExecuteNativeToolsForPhase(ChatCompletionResult response, RequestPhase phase, List<ChatMessage> messages)
        {
            if (phase == RequestPhase.Reply)
            {
                await Task.CompletedTask;
                return default;
            }
            string guidance = "ToolRunner Guidance: Reply to the player in natural language only. Do NOT output any tool call JSON. You may include [EXPR:n] to set expression (n=1-6).";
            string thought = response?.Thought;
            if (string.IsNullOrWhiteSpace(thought))
            {
                thought = ExtractThoughtFromToolJson(response?.Content);
            }
            UpdateLatestThought(thought);
            var toolCalls = response?.ToolCalls;
            if (toolCalls == null || toolCalls.Count == 0)
            {
                UpdatePhaseToolLedger(phase, false, new List<string>());
                _history.Add(("toolcall", "{\"tool_calls\": []}"));
                _history.Add(("trace", $"[Tool Results]\nTool 'no_action' Result: No action taken.\n{guidance}"));
                PersistHistory();
                UpdateActionLedgerNote();
                await Task.CompletedTask;
                return default;
            }
            int maxTools = MaxToolsPerPhase(phase);
            var callsToExecute = toolCalls.Count > maxTools ? toolCalls.GetRange(0, maxTools) : toolCalls;
            messages?.Add(ChatMessage.AssistantWithToolCalls(callsToExecute, response.Content));
            int executed = 0;
            bool executedActionTool = false;
            bool successfulToolCall = false;
            var successfulTools = new List<string>();
            var successfulActions = new List<string>();
            var failedActions = new List<string>();
            var nonActionToolsInActionPhase = new List<string>();
            var historyCalls = new List<Dictionary<string, object>>();
            var historyToolResults = new List<(string id, string content)>();
            StringBuilder combinedResults = new StringBuilder();
            string capturedImageForPhase = null;
            if (toolCalls.Count > maxTools)
            {
                combinedResults.AppendLine($"ToolRunner Note: Skipped remaining tools because this phase allows at most {maxTools} tool call(s).");
            }
            bool countActionSuccessOnly = phase == RequestPhase.ActionTools;
            foreach (var call in callsToExecute)
            {
                if (call == null || string.IsNullOrWhiteSpace(call.Name))
                {
                    executed++;
                    continue;
                }
                if (string.IsNullOrWhiteSpace(call.Id))
                {
                    call.Id = $"call_{phase}_{executed + 1}";
                }
                string argsJson = string.IsNullOrWhiteSpace(call.ArgumentsJson) ? "{}" : call.ArgumentsJson;
                Dictionary<string, object> parsedArgs = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                if (JsonToolCallParser.TryParseObject(argsJson, out var parsedDict))
                {
                    parsedArgs = parsedDict;
                }
                var historyCall = new Dictionary<string, object>
                {
                    ["type"] = "function",
                    ["function"] = new Dictionary<string, object>
                    {
                        ["name"] = call.Name,
                        ["arguments"] = parsedArgs
                    }
                };
                if (!string.IsNullOrWhiteSpace(call.Id))
                {
                    historyCall["id"] = call.Id;
                }
                historyCalls.Add(historyCall);
                if (string.Equals(call.Name, "no_action", StringComparison.OrdinalIgnoreCase))
                {
                    string note = "ToolRunner Note: Ignored 'no_action' tool because other tool calls were present.";
                    combinedResults.AppendLine(note);
                    messages?.Add(ChatMessage.ToolResult(call.Id ?? "", note));
                    historyToolResults.Add((call.Id ?? "", note));
                    executed++;
                    continue;
                }
                if (IsVlmToolName(call.Name))
                {
                    if (phase != RequestPhase.QueryTools || WulaFallenEmpireMod.settings?.enableVlmFeatures != true)
                    {
                        string note = $"ToolRunner Note: Ignored visual tool in this phase: {call.Name}.";
                        combinedResults.AppendLine(note);
                        messages?.Add(ChatMessage.ToolResult(call.Id ?? "", note));
                        historyToolResults.Add((call.Id ?? "", note));
                        nonActionToolsInActionPhase.Add(call.Name);
                        executed++;
                        continue;
                    }
                    capturedImageForPhase = ScreenCaptureUtility.CaptureScreenAsBase64();
                    string resultText = "Screen captured successfully. Context updated for next phase.";
                    combinedResults.AppendLine($"Tool '{call.Name}' Result: {resultText}");
                    messages?.Add(ChatMessage.ToolResult(call.Id ?? "", resultText));
                    historyToolResults.Add((call.Id ?? "", resultText));
                    successfulToolCall = true;
                    successfulTools.Add(call.Name);
                    executed++;
                    continue;
                }
                if (phase == RequestPhase.ActionTools && IsQueryToolName(call.Name))
                {
                    string note = $"ToolRunner Note: Ignored query tool in action phase: {call.Name}.";
                    combinedResults.AppendLine(note);
                    nonActionToolsInActionPhase.Add(call.Name);
                    messages?.Add(ChatMessage.ToolResult(call.Id ?? "", note));
                    historyToolResults.Add((call.Id ?? "", note));
                    executed++;
                    continue;
                }
                var tool = _tools.FirstOrDefault(t => string.Equals(t.Name, call.Name, StringComparison.OrdinalIgnoreCase));
                if (tool == null)
                {
                    string missing = $"Error: Tool '{call.Name}' not found.";
                    combinedResults.AppendLine(missing);
                    combinedResults.AppendLine("ToolRunner Guard: The tool call failed. In your reply you MUST acknowledge the failure and MUST NOT claim success.");
                    messages?.Add(ChatMessage.ToolResult(call.Id ?? "", missing));
                    historyToolResults.Add((call.Id ?? "", missing));
                    executed++;
                    continue;
                }
                if (!ToolCallValidator.TryValidate(tool, argsJson, out _, out string validationError))
                {
                    string note = $"{validationError} Please output tool_calls with valid arguments only.";
                    combinedResults.AppendLine(note);
                    combinedResults.AppendLine("ToolRunner Guard: The tool call was blocked before execution. You MUST correct the tool call.");
                    messages?.Add(ChatMessage.ToolResult(call.Id ?? "", note));
                    historyToolResults.Add((call.Id ?? "", note));
                    if (IsActionToolName(call.Name))
                    {
                        failedActions.Add(call.Name);
                        AddActionFailure(call.Name);
                    }
                    executed++;
                    continue;
                }
                if (Prefs.DevMode)
                {
                    WulaLog.Debug($"[WulaAI] Executing tool (phase {phase}): {call.Name} with args: {argsJson}");
                }
                string result = (await tool.ExecuteAsync(argsJson)).Trim();
                bool isError = !string.IsNullOrEmpty(result) && result.StartsWith("Error:", StringComparison.OrdinalIgnoreCase);
                if (call.Name == "modify_goodwill")
                {
                    combinedResults.AppendLine($"Tool '{call.Name}' Result (Invisible): {result}");
                }
                else
                {
                    combinedResults.AppendLine($"Tool '{call.Name}' Result: {result}");
                }
                if (isError)
                {
                    combinedResults.AppendLine("ToolRunner Guard: The tool returned an error. In your reply you MUST acknowledge the failure and MUST NOT claim success.");
                }
                messages?.Add(ChatMessage.ToolResult(call.Id ?? "", result));
                historyToolResults.Add((call.Id ?? "", result));
                if (!isError)
                {
                    bool countsAsSuccess = !countActionSuccessOnly || IsActionToolName(call.Name);
                    if (countsAsSuccess)
                    {
                        successfulToolCall = true;
                        successfulTools.Add(call.Name);
                    }
                    else
                    {
                        nonActionToolsInActionPhase.Add(call.Name);
                    }
                }
                if (IsActionToolName(call.Name))
                {
                    if (!isError)
                    {
                        executedActionTool = true;
                        successfulActions.Add(call.Name);
                        AddActionSuccess(call.Name);
                    }
                    else
                    {
                        failedActions.Add(call.Name);
                        AddActionFailure(call.Name);
                    }
                }
                executed++;
            }
            if (phase == RequestPhase.ActionTools && nonActionToolsInActionPhase.Count > 0)
            {
                combinedResults.AppendLine($"ToolRunner Note: Action phase ignores non-action tools for success: {string.Join(", ", nonActionToolsInActionPhase)}.");
            }
            if (executedActionTool)
            {
                combinedResults.AppendLine("ToolRunner Guard: An in-game action tool WAS executed this turn. You MAY reference it, but do NOT invent additional actions.");
            }
            else
            {
                combinedResults.AppendLine("ToolRunner Guard: NO in-game actions were executed. You MUST NOT claim any deliveries, reinforcements, bombardments, or other actions occurred.");
                if (phase == RequestPhase.ActionTools)
                {
                    combinedResults.AppendLine("ToolRunner Guard: Action phase failed (no action tools executed).");
                }
            }
            combinedResults.AppendLine(guidance);
            string toolCallsJson = historyCalls.Count == 0
                ? "{\"tool_calls\": []}"
                : JsonToolCallParser.SerializeToJson(new Dictionary<string, object> { ["tool_calls"] = historyCalls });
            _history.Add(("toolcall", toolCallsJson));
            foreach (var entry in historyToolResults)
            {
                if (string.IsNullOrWhiteSpace(entry.id)) continue;
                string content = entry.content ?? "";
                _history.Add(("tool", $"[ToolCallId:{entry.id}]\n{content}".TrimEnd()));
            }
            _history.Add(("trace", $"[Tool Results]\n{combinedResults.ToString().Trim()}"));
            PersistHistory();
            UpdatePhaseToolLedger(phase, successfulToolCall, successfulTools);
            UpdateActionLedgerNote();
            await Task.CompletedTask;
            return new PhaseExecutionResult
            {
                AnyToolSuccess = successfulToolCall,
                AnyActionSuccess = successfulActions.Count > 0,
                AnyActionError = failedActions.Count > 0,
                CapturedImage = capturedImageForPhase
            };
        }
private async Task<PhaseExecutionResult> ExecuteJsonToolsForPhase(string json, RequestPhase phase)
        {
            if (phase == RequestPhase.Reply)
            {
                await Task.CompletedTask;
                return default;
            }
            string guidance = "ToolRunner Guidance: Reply to the player in natural language only. Do NOT output any tool call JSON. You may include [EXPR:n] to set expression (n=1-6).";
            string thought = ExtractThoughtFromToolJson(json);
            UpdateLatestThought(thought);
            if (!JsonToolCallParser.TryParseToolCallsFromText(json ?? "", out var toolCalls, out string jsonFragment))
            {
                UpdatePhaseToolLedger(phase, false, new List<string>());
                _history.Add(("toolcall", "{\"tool_calls\": []}"));
                _history.Add(("trace", $"[Tool Results]\nTool 'no_action' Result: No action taken.\n{guidance}"));
                PersistHistory();
                UpdateActionLedgerNote();
                await Task.CompletedTask;
                return default;
            }
            if (toolCalls.Count == 0)
            {
                UpdatePhaseToolLedger(phase, false, new List<string>());
                _history.Add(("toolcall", "{\"tool_calls\": []}"));
                _history.Add(("trace", $"[Tool Results]\nTool 'no_action' Result: No action taken.\n{guidance}"));
                PersistHistory();
                UpdateActionLedgerNote();
                await Task.CompletedTask;
                return default;
            }
            int maxTools = MaxToolsPerPhase(phase);
            int executed = 0;
            bool executedActionTool = false;
            bool successfulToolCall = false;
            var successfulTools = new List<string>();
            var successfulActions = new List<string>();
            var failedActions = new List<string>();
            var nonActionToolsInActionPhase = new List<string>();
            var historyCalls = new List<Dictionary<string, object>>();
            var historyToolResults = new List<(string id, string content)>();
            StringBuilder combinedResults = new StringBuilder();
            string capturedImageForPhase = null;
            bool countActionSuccessOnly = phase == RequestPhase.ActionTools;
            foreach (var call in toolCalls)
            {
                if (executed >= maxTools)
                {
                    combinedResults.AppendLine($"ToolRunner Note: Skipped remaining tools because this phase allows at most {maxTools} tool call(s).");
                    break;
                }
                string toolName = call.Name;
                if (string.IsNullOrWhiteSpace(toolName))
                {
                    executed++;
                    continue;
                }
                if (string.IsNullOrWhiteSpace(call.Id))
                {
                    call.Id = $"call_{phase}_{executed + 1}";
                }
                if (string.Equals(toolName, "no_action", StringComparison.OrdinalIgnoreCase))
                {
                    combinedResults.AppendLine("ToolRunner Note: Ignored 'no_action' tool because other tool calls were present.");
                    historyToolResults.Add((call.Id ?? "", "ToolRunner Note: Ignored 'no_action' tool because other tool calls were present."));
                    executed++;
                    continue;
                }
                var historyCall = new Dictionary<string, object>
                {
                    ["type"] = "function",
                    ["function"] = new Dictionary<string, object>
                    {
                        ["name"] = toolName,
                        ["arguments"] = call.Arguments ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                    }
                };
                if (!string.IsNullOrWhiteSpace(call.Id))
                {
                    historyCall["id"] = call.Id;
                }
                historyCalls.Add(historyCall);
                if (IsVlmToolName(toolName))
                {
                    if (phase != RequestPhase.QueryTools || WulaFallenEmpireMod.settings?.enableVlmFeatures != true)
                    {
                        combinedResults.AppendLine($"ToolRunner Note: Ignored visual tool in this phase: {toolName}.");
                        historyToolResults.Add((call.Id ?? "", $"ToolRunner Note: Ignored visual tool in this phase: {toolName}."));
                        nonActionToolsInActionPhase.Add(toolName);
                        executed++;
                        continue;
                    }
                    capturedImageForPhase = ScreenCaptureUtility.CaptureScreenAsBase64();
                    combinedResults.AppendLine($"Tool '{toolName}' Result: Screen captured successfully. Context updated for next phase.");
                    historyToolResults.Add((call.Id ?? "", $"Tool '{toolName}' Result: Screen captured successfully. Context updated for next phase."));
                    successfulToolCall = true;
                    successfulTools.Add(toolName);
                    executed++;
                    continue;
                }
                if (phase == RequestPhase.ActionTools && IsQueryToolName(toolName))
                {
                    combinedResults.AppendLine($"ToolRunner Note: Ignored query tool in action phase: {toolName}.");
                    historyToolResults.Add((call.Id ?? "", $"ToolRunner Note: Ignored query tool in action phase: {toolName}."));
                    nonActionToolsInActionPhase.Add(toolName);
                    executed++;
                    continue;
                }
                var tool = _tools.FirstOrDefault(t => t.Name == toolName);
                if (tool == null)
                {
                    combinedResults.AppendLine($"Error: Tool '{toolName}' not found.");
                    historyToolResults.Add((call.Id ?? "", $"Error: Tool '{toolName}' not found."));
                    combinedResults.AppendLine("ToolRunner Guard: The tool call failed. In your reply you MUST acknowledge the failure and MUST NOT claim success.");
                    executed++;
                    continue;
                }
                string argsJson = call.ArgumentsJson ?? "{}";
                if (Prefs.DevMode)
                {
                    WulaLog.Debug($"[WulaAI] Executing tool (phase {phase}): {toolName} with args: {argsJson}");
                }
                string result = (await tool.ExecuteAsync(argsJson)).Trim();
                bool isError = !string.IsNullOrEmpty(result) && result.StartsWith("Error:", StringComparison.OrdinalIgnoreCase);
                if (toolName == "modify_goodwill")
                {
                    combinedResults.AppendLine($"Tool '{toolName}' Result (Invisible): {result}");
                }
                else
                {
                    combinedResults.AppendLine($"Tool '{toolName}' Result: {result}");
                }
                historyToolResults.Add((call.Id ?? "", result));
                if (isError)
                {
                    combinedResults.AppendLine("ToolRunner Guard: The tool returned an error. In your reply you MUST acknowledge the failure and MUST NOT claim success.");
                }
                if (!isError)
                {
                    bool countsAsSuccess = !countActionSuccessOnly || IsActionToolName(toolName);
                    if (countsAsSuccess)
                    {
                        successfulToolCall = true;
                        successfulTools.Add(toolName);
                    }
                    else
                    {
                        nonActionToolsInActionPhase.Add(toolName);
                    }
                }
                if (IsActionToolName(toolName))
                {
                    if (!isError)
                    {
                        executedActionTool = true;
                        successfulActions.Add(toolName);
                        AddActionSuccess(toolName);
                    }
                    else
                    {
                        failedActions.Add(toolName);
                        AddActionFailure(toolName);
                    }
                }
                executed++;
            }
            if (!string.IsNullOrWhiteSpace(jsonFragment) && !string.Equals((json ?? "").Trim(), jsonFragment, StringComparison.Ordinal))
            {
                combinedResults.AppendLine("ToolRunner Note: Non-JSON text in the tool phase was ignored.");
            }
            if (phase == RequestPhase.ActionTools && nonActionToolsInActionPhase.Count > 0)
            {
                combinedResults.AppendLine($"ToolRunner Note: Action phase ignores non-action tools for success: {string.Join(", ", nonActionToolsInActionPhase)}.");
            }
            if (executedActionTool)
            {
                combinedResults.AppendLine("ToolRunner Guard: An in-game action tool WAS executed this turn. You MAY reference it, but do NOT invent additional actions.");
            }
            else
            {
                combinedResults.AppendLine("ToolRunner Guard: NO in-game actions were executed. You MUST NOT claim any deliveries, reinforcements, bombardments, or other actions occurred.");
                if (phase == RequestPhase.ActionTools)
                {
                    combinedResults.AppendLine("ToolRunner Guard: Action phase failed (no action tools executed).");
                }
            }
            combinedResults.AppendLine(guidance);
            string toolCallsJson = historyCalls.Count == 0
                ? "{\"tool_calls\": []}"
                : JsonToolCallParser.SerializeToJson(new Dictionary<string, object> { ["tool_calls"] = historyCalls });
            _history.Add(("toolcall", toolCallsJson));
            foreach (var entry in historyToolResults)
            {
                if (string.IsNullOrWhiteSpace(entry.id)) continue;
                string content = entry.content ?? "";
                _history.Add(("tool", $"[ToolCallId:{entry.id}]\n{content}".TrimEnd()));
            }
            _history.Add(("trace", $"[Tool Results]\n{combinedResults.ToString().Trim()}"));
            PersistHistory();
            UpdatePhaseToolLedger(phase, successfulToolCall, successfulTools);
            UpdateActionLedgerNote();
            await Task.CompletedTask;
            return new PhaseExecutionResult
            {
                AnyToolSuccess = successfulToolCall,
                AnyActionSuccess = successfulActions.Count > 0,
                AnyActionError = failedActions.Count > 0,
                CapturedImage = capturedImageForPhase
            };
        }
        private void AddActionSuccess(string toolName)
        {
            if (_actionSuccessLedgerSet.Add(toolName))
            {
                _actionSuccessLedger.Add(toolName);
            }
        }
        private void AddActionFailure(string toolName)
        {
            if (_actionFailedLedgerSet.Add(toolName))
            {
                _actionFailedLedger.Add(toolName);
            }
        }
        private void UpdateActionLedgerNote()
        {
            _lastActionExecuted = _actionSuccessLedger.Count > 0;
            _lastActionHadError = _actionFailedLedger.Count > 0;
            if (_lastActionExecuted)
            {
                _lastActionLedgerNote = $"Action Ledger: {string.Join(", ", _actionSuccessLedger)}";
            }
            else if (_lastActionHadError)
            {
                _lastActionLedgerNote = $"Action Ledger: None (no successful actions). Failed: {string.Join(", ", _actionFailedLedger)}";
            }
            else
            {
                _lastActionLedgerNote = "Action Ledger: None (no in-game actions executed).";
            }
        }
        private void UpdatePhaseToolLedger(RequestPhase phase, bool hasSuccess, List<string> successfulTools)
        {
            if (phase == RequestPhase.QueryTools)
            {
                _querySuccessfulToolCall = hasSuccess;
                _queryToolLedgerNote = hasSuccess
                    ? $"Tool Ledger (Query): {string.Join(", ", successfulTools)}"
                    : "Tool Ledger (Query): None (no successful tool calls).";
            }
            else if (phase == RequestPhase.ActionTools)
            {
                _actionSuccessfulToolCall = hasSuccess;
                _actionToolLedgerNote = hasSuccess
                    ? $"Tool Ledger (Action): {string.Join(", ", successfulTools)}"
                    : "Tool Ledger (Action): None (no successful tool calls).";
            }
        }
        private void ResetTurnState()
        {
            _lastActionExecuted = false;
            _lastActionHadError = false;
            _lastActionLedgerNote = "Action Ledger: None (no in-game actions executed).";
            _lastSuccessfulToolCall = false;
            _queryToolLedgerNote = "Tool Ledger (Query): None (no successful tool calls).";
            _actionToolLedgerNote = "Tool Ledger (Action): None (no successful tool calls).";
            _querySuccessfulToolCall = false;
            _actionSuccessfulToolCall = false;
            _queryRetryUsed = false;
            _actionRetryUsed = false;
            _actionSuccessLedger.Clear();
            _actionSuccessLedgerSet.Clear();
            _actionFailedLedger.Clear();
            _actionFailedLedgerSet.Clear();
        }
    }
}
