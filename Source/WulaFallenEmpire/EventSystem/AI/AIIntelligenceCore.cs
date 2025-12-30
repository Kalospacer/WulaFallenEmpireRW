using System;
using System.Collections.Generic;
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
        private string _memoryContext;
        private string _memoryContextQuery;
        private bool _memoryUpdateInProgress;

        private const int DefaultMaxHistoryTokens = 100000;
        private const int CharsPerToken = 4;
        private const int ThinkingPhaseTotal = 3;

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
                    LongEventHandler.ExecuteWhenFinished(() =>
                    {
                        try
                        {
                            var existingWindow = Find.WindowStack?.Windows?.OfType<WulaFallenEmpire.EventSystem.AI.UI.Overlay_WulaLink>().FirstOrDefault();
                            if (existingWindow == null)
                            {
                                var eventDef = DefDatabase<EventDef>.GetNamedSilentFail(eventDefNameToRestore);
                                if (eventDef != null)
                                {
                                    var newWindow = new WulaFallenEmpire.EventSystem.AI.UI.Overlay_WulaLink(eventDef);
                                    Find.WindowStack.Add(newWindow);
                                    newWindow.ToggleMinimize(); // Start minimized
                                    // Force position after everything else
                                    if (_overlayWindowX >= 0f && _overlayWindowY >= 0f)
                                    {
                                        newWindow.windowRect.x = _overlayWindowX;
                                        newWindow.windowRect.y = _overlayWindowY;
                                    }
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

            // 附加选中对象的上下文信息
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
        /// 用于自动评论系统 - 走正常的对话流程（包含完整的思考步骤）
        /// 让 AI 自己决定是否需要回复
        /// </summary>
        public void SendAutoCommentaryMessage(string eventInfo)
        {
            if (string.IsNullOrWhiteSpace(eventInfo)) return;

            // 标记为自动评论消息，不显示在对话历史中
            string internalMessage = $"[AUTO_COMMENTARY]\n{eventInfo}";
            
            // 添加到历史并触发正常的 AI 思考流程
            _history.Add(("user", internalMessage));
            PersistHistory();
            
            // 使用正常的分阶段请求流程（包含工具调用能力等）
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
            
            // Agent 工具 - 保留画面分析截图能力，移除所有模拟操作工具
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

            _isThinking = isThinking;
            OnThinkingStateChanged?.Invoke(_isThinking);
        }

        private void SetThinkingPhase(int phaseIndex, bool isRetry)
        {
            _thinkingPhaseIndex = Math.Max(1, Math.Min(ThinkingPhaseTotal, phaseIndex));
            _thinkingPhaseRetry = isRetry;
            _thinkingStartTime = Time.realtimeSinceStartup;
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
            _memoryContext = null;
            _memoryContextQuery = null;
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
                _memoryContextQuery = "";
                _memoryContext = "";
                if (Prefs.DevMode)
                {
                    WulaLog.Debug("[WulaAI] Memory context skipped (auto commentary).");
                }
                return;
            }

            _memoryContextQuery = safeQuery;
            _memoryContext = BuildMemoryContext(_memoryContextQuery);
            if (Prefs.DevMode)
            {
                string preview = TrimForPrompt(_memoryContextQuery, 80);
                int length = _memoryContext?.Length ?? 0;
                WulaLog.Debug($"[WulaAI] Memory context refreshed (query='{preview}', length={length}).");
            }
        }

        private string GetMemoryContext()
        {
            if (string.IsNullOrWhiteSpace(_memoryContext))
            {
                string query = _memoryContextQuery;
                if (string.IsNullOrWhiteSpace(query))
                {
                    query = GetLastUserMessageForMemory();
                }
                _memoryContextQuery = query ?? "";
                _memoryContext = BuildMemoryContext(_memoryContextQuery);
            }

            return _memoryContext ?? "";
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

            string memoryContext = GetMemoryContext();
            if (!string.IsNullOrWhiteSpace(memoryContext))
            {
                fullInstruction += memoryContext;
            }

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

            return $"{fullInstruction}\n{goodwillContext}\nIMPORTANT: Output JSON tool calls only (or {{\"tool_calls\": []}}). " +
                   $"You will produce the natural-language reply later and MUST use: {language}.";
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
            string memoryContext = GetMemoryContext();
            string personaBlock = string.IsNullOrWhiteSpace(memoryContext) ? persona : (persona + memoryContext);
            string phaseInstruction = GetPhaseInstruction(phase).TrimEnd();
            string toolsForThisPhase = BuildToolsForPhase(phase);
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

            return string.Join("\n\n", new[]
            {
                personaBlock,
                phaseInstruction,
                string.IsNullOrWhiteSpace(actionPriority) ? null : actionPriority.TrimEnd(),
                string.IsNullOrWhiteSpace(actionWhitelist) ? null : actionWhitelist.TrimEnd(),
                ToolRulesInstruction.TrimEnd(),
                toolsForThisPhase
            }.Where(part => !string.IsNullOrWhiteSpace(part)));
        }

        private string BuildToolsForPhase(RequestPhase phase)
        {
            if (phase == RequestPhase.Reply) return "";

            var available = _tools
                .Where(t => t != null)
                .Where(t => phase == RequestPhase.QueryTools
                    ? IsQueryToolName(t.Name)
                    : phase == RequestPhase.ActionTools
                        ? IsActionToolName(t.Name)
                        : true)
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("====");
            sb.AppendLine();
            sb.AppendLine("# TOOLS (AVAILABLE)");
            sb.AppendLine("Use JSON tool calls only, or {\"tool_calls\": []} if no tools are needed.");
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

        private static string GetPhaseInstruction(RequestPhase phase)
        {
            return phase switch
            {
                RequestPhase.QueryTools =>
                    "# PHASE 1/3 (Query Tools)\n" +
                    "Goal: Gather info needed for decisions.\n" +
                    "Rules:\n" +
                    "- You MUST NOT write any natural language to the user in this phase.\n" +
                    "- Output JSON tool calls only, or exactly: {\"tool_calls\": []}.\n" +
                    "- Prefer query tools (get_*/search_*).\n" +
                    "- CRITICAL: If the user asks for an ITEM (e.g. 'Reviver Mech Serum'), you MUST use search_thing_def with {\"query\":\"...\"} to find its exact DefName. NEVER GUESS DefNames.\n" +
                    "- You MAY call multiple tools in one response, but keep it concise.\n" +
                    "- If the user requests multiple items or information, you MUST output ALL required tool calls in this SAME response.\n" +
                    "- Action tools are available in PHASE 2 only; do NOT use them here.\n" +
                    "After this phase, the game will automatically proceed to PHASE 2.\n" +
                    "Output: JSON only.\n",
                RequestPhase.ActionTools =>
                    "# PHASE 2/3 (Action Tools)\n" +
                    "Goal: Execute in-game actions based on known info.\n" +
                    "Rules:\n" +
                    "- You MUST NOT write any natural language to the user in this phase.\n" +
                    "- Output JSON tool calls only, or exactly: {\"tool_calls\": []}.\n" +
                    "- ONLY action tools are accepted in this phase (spawn_resources, send_reinforcement, call_bombardment, modify_goodwill, call_prefab_airdrop).\n" +
                    "- Query tools (get_*/search_*) will be ignored.\n" +
                    "- Prefer action tools (spawn_resources, send_reinforcement, call_bombardment, modify_goodwill).\n" +
                    "- Avoid queries unless absolutely required.\n" +
                    "- If no action is required based on query results, output {\"tool_calls\": []}.\n" +
                    "- If you already executed the needed action earlier this turn, output {\"tool_calls\": []}.\n" +
                    "After this phase, the game will automatically proceed to PHASE 3.\n" +
                    "Output: JSON only.\n",
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

        private static bool ShouldRetryTools(string response)
        {
            if (string.IsNullOrWhiteSpace(response)) return false;
            if (!JsonToolCallParser.TryParseObject(response, out var obj)) return false;
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

        private static string SanitizeToolResultForActionPhase(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return message;
            string sanitized = message;
            sanitized = Regex.Replace(sanitized, @"Tool\s+'[^']+'\s+Result(?:\s+\(Invisible\))?:", "Query Result:");
            sanitized = Regex.Replace(sanitized, @"Tool\s+'[^']+'\s+Result\s+\(Invisible\):", "Query Result:");
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
                if (string.Equals(entry.role, "tool", StringComparison.OrdinalIgnoreCase))
                {
                    if (lastUserIndex != -1 && i > lastUserIndex)
                    {
                        filtered.Add(entry);
                    }
                    continue;
                }

                if (!string.Equals(entry.role, "assistant", StringComparison.OrdinalIgnoreCase))
                {
                    filtered.Add(entry);
                    continue;
                }

                // Revert UI filtering: Add assistant messages directly without stripping tool call JSON for history context
                filtered.Add(entry);
            }

            return filtered;
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

                if (IsAutoCommentaryMessage(entry.message))
                {
                    continue;
                }

                string role = string.Equals(entry.role, "user", StringComparison.OrdinalIgnoreCase) ? "User" : "Assistant";
                sb.AppendLine($"{role}: {entry.message}");
            }

            string conversation = sb.ToString().Trim();
            return TrimForPrompt(conversation, 4000);
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
                facts.Add(new MemoryFact { Text = text.Trim(), Category = category ?? "misc" });
            }

            return facts;
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
                if (settings == null || string.IsNullOrEmpty(settings.apiKey))
                {
                    AddAssistantMessage("Error: API Key not configured in Mod Settings.");
                    return;
                }

                string apiKey = settings.useGeminiProtocol ? settings.geminiApiKey : settings.apiKey;
                string baseUrl = settings.useGeminiProtocol ? settings.geminiBaseUrl : settings.baseUrl;
                string model = settings.useGeminiProtocol ? settings.geminiModel : settings.model;

                var client = new SimpleAIClient(apiKey, baseUrl, model, settings.useGeminiProtocol);
                _currentClient = client;

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
                string replyInstruction = GetSystemInstruction(false, "") + "\n\n" + GetPhaseInstruction(replyPhase);
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
        private async Task<PhaseExecutionResult> ExecuteJsonToolsForPhase(string json, RequestPhase phase)
        {
            if (phase == RequestPhase.Reply)
            {
                await Task.CompletedTask;
                return default;
            }

            string guidance = "ToolRunner Guidance: Reply to the player in natural language only. Do NOT output any tool call JSON. You may include [EXPR:n] to set expression (n=1-6).";

            if (!JsonToolCallParser.TryParseToolCallsFromText(json ?? "", out var toolCalls, out string jsonFragment))
            {
                UpdatePhaseToolLedger(phase, false, new List<string>());
                _history.Add(("toolcall", "{\"tool_calls\": []}"));
                _history.Add(("tool", $"[Tool Results]\nTool 'no_action' Result: No action taken.\n{guidance}"));
                PersistHistory();
                UpdateActionLedgerNote();
                await Task.CompletedTask;
                return default;
            }

            if (toolCalls.Count == 0)
            {
                UpdatePhaseToolLedger(phase, false, new List<string>());
                _history.Add(("toolcall", "{\"tool_calls\": []}"));
                _history.Add(("tool", $"[Tool Results]\nTool 'no_action' Result: No action taken.\n{guidance}"));
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

                if (string.Equals(toolName, "no_action", StringComparison.OrdinalIgnoreCase))
                {
                    combinedResults.AppendLine("ToolRunner Note: Ignored 'no_action' tool because other tool calls were present.");
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

                if (toolName.Equals("analyze_screen", StringComparison.OrdinalIgnoreCase) || toolName.Equals("capture_screen", StringComparison.OrdinalIgnoreCase))
                {
                    capturedImageForPhase = ScreenCaptureUtility.CaptureScreenAsBase64();
                    combinedResults.AppendLine($"Tool '{toolName}' Result: Screen captured successfully. Context updated for next phase.");
                    successfulToolCall = true;
                    successfulTools.Add(toolName);
                    executed++;
                    continue;
                }

                if (phase == RequestPhase.ActionTools && IsQueryToolName(toolName))
                {
                    combinedResults.AppendLine($"ToolRunner Note: Ignored query tool in action phase: {toolName}.");
                    nonActionToolsInActionPhase.Add(toolName);
                    executed++;
                    continue;
                }

                var tool = _tools.FirstOrDefault(t => t.Name == toolName);
                if (tool == null)
                {
                    combinedResults.AppendLine($"Error: Tool '{toolName}' not found.");
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
            _history.Add(("tool", $"[Tool Results]\n{combinedResults.ToString().Trim()}"));
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

