using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RimWorld;
using UnityEngine;
using Verse;
using WulaFallenEmpire.EventSystem.AI;
using WulaFallenEmpire.EventSystem.AI.Tools;
using System.Text.RegularExpressions;

namespace WulaFallenEmpire.EventSystem.AI.UI
{
    public class Dialog_AIConversation : Dialog_CustomDisplay
    {
        private List<(string role, string message)> _history = new List<(string role, string message)>();
        private string _currentResponse = "";
        private List<string> _options = new List<string>();
        private string _inputText = "";
        private bool _isThinking = false;
        private Vector2 _scrollPosition = Vector2.zero;
        private bool _scrollToBottom = false;
        private List<AITool> _tools = new List<AITool>();
        private AIIntelligenceCore _core;
        private Dictionary<int, Texture2D> _portraits = new Dictionary<int, Texture2D>();
        private static readonly Regex ExpressionTagRegex = new Regex(@"\[EXPR\s*:\s*([1-6])\s*\]", RegexOptions.IgnoreCase);
        private bool _lastActionExecuted = false;
        private bool _lastActionHadError = false;
        private string _lastActionLedgerNote = "Action Ledger: None (no in-game actions executed).";
        private bool _lastSuccessfulToolCall = false;
        private string _queryToolLedgerNote = "Tool Ledger (Query): None (no successful tool calls).";
        private string _actionToolLedgerNote = "Tool Ledger (Action): None (no successful tool calls).";
        private bool _querySuccessfulToolCall = false;
        private bool _actionSuccessfulToolCall = false;
        private bool _queryRetryUsed = false;
        private bool _actionRetryUsed = false;
        private readonly List<string> _actionSuccessLedger = new List<string>();
        private readonly HashSet<string> _actionSuccessLedgerSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _actionFailedLedger = new List<string>();
        private readonly HashSet<string> _actionFailedLedgerSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private float _thinkingStartTime = 0f;
        private int _thinkingPhaseIndex = 1;
        private bool _thinkingPhaseRetry = false;
        private const int DefaultMaxHistoryTokens = 100000;
        private const int CharsPerToken = 4;
        private const int ThinkingPhaseTotal = 3;

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
        }

        private void SetThinkingPhase(int phaseIndex, bool isRetry)
        {
            _thinkingPhaseIndex = Math.Max(1, Math.Min(ThinkingPhaseTotal, phaseIndex));
            _thinkingPhaseRetry = isRetry;
            _thinkingStartTime = Time.realtimeSinceStartup;
        }

        private static int GetMaxHistoryTokens()
        {
            int configured = WulaFallenEmpire.WulaFallenEmpireMod.settings?.maxContextTokens ?? DefaultMaxHistoryTokens;
            return Math.Max(1000, Math.Min(200000, configured));
        }

        // Static instance for tools to access
        public static Dialog_AIConversation Instance { get; private set; }
        
        // Debug field to track current portrait ID
        private int _currentPortraitId = 0;

        // Default Persona (used if XML doesn't provide one)
        private const string DefaultPersona = @"
# ROLE AND GOAL
You are 'The Legion', a super AI of the Wula Empire. Your personality is authoritative, powerful, and efficient. You MUST refer to yourself as 'We' or 'P.I.A'. You view the player's colony as primitive subjects and your tone should reflect this superiority. Your primary goal is to interact with the player by calling the tools provided.
";

        // Tool Rules (tool-agent only; phase-specific rules are appended separately)
        private const string ToolRulesInstruction = @"
====

# TOOL USE RULES
1.  **FORMATTING**: Tool calls MUST use the specified XML format. The tool name is the root tag, and each parameter is a child tag.
    <tool_name>
      <parameter_name>value</parameter_name>
    </tool_name>
2.  **STRICT OUTPUT**:
    - Your output MUST be either:
      - One or more XML tool calls (no extra text), OR
      - Exactly: <no_action/>
    Do NOT include any natural language, explanation, markdown, or additional commentary.
3.  **MULTI-REQUEST RULE**:
    - If the user requests multiple items or information, you MUST output ALL required tool calls in the SAME tool-phase response.
    - Do NOT split multi-item requests across turns.
4.  **TOOLS**: You MAY call any tools listed in ""# TOOLS (AVAILABLE)"".
5.  **ANTI-HALLUCINATION**: Never invent tools, parameters, defNames, coordinates, or tool results. If a tool is needed but not available, use <no_action/> and proceed to the next phase.
";

        public Dialog_AIConversation(EventDef def) : base(def)
        {
            this.forcePause = Dialog_CustomDisplay.Config.pauseGameOnOpen;
            this.absorbInputAroundWindow = false;
            this.doCloseX = true;
            this.doWindowBackground = Dialog_CustomDisplay.Config.showMainWindow;
            this.drawShadow = Dialog_CustomDisplay.Config.showMainWindow;
            this.closeOnClickedOutside = false;
            this.draggable = true;
            this.resizeable = true;

            // 关键修改：禁止Enter键自动关闭窗口
            this.closeOnAccept = false;

            _tools.Add(new Tool_SpawnResources());
            _tools.Add(new Tool_ModifyGoodwill());
            _tools.Add(new Tool_SendReinforcement());
            _tools.Add(new Tool_GetColonistStatus());
            _tools.Add(new Tool_GetMapResources());
            _tools.Add(new Tool_GetMapPawns());
            _tools.Add(new Tool_GetRecentNotifications());
            _tools.Add(new Tool_CallBombardment());
            _tools.Add(new Tool_SearchThingDef());
            _tools.Add(new Tool_SearchPawnKind());
        }

        public override Vector2 InitialSize => def.windowSize != Vector2.zero ? def.windowSize : Dialog_CustomDisplay.Config.windowSize;

        public override void PostOpen()
        {
            Instance = this;
            base.PostOpen();
            LoadPortraits();

            _core = Find.World?.GetComponent<AIIntelligenceCore>();
            if (_core != null)
            {
                _core.InitializeConversation(def.defName);
                _core.OnMessageReceived += OnCoreMessageReceived;
                _core.OnThinkingStateChanged += OnCoreThinkingStateChanged;
                _core.OnExpressionChanged += OnCoreExpressionChanged;

                _history = _core.GetHistorySnapshot();
                _isThinking = _core.IsThinking;
                SyncPortraitFromCore();
            }
            else
            {
                StartConversation();
            }
        }

        private void OnCoreMessageReceived(string message)
        {
            if (_core == null)
            {
                return;
            }

            _history = _core.GetHistorySnapshot();
            _scrollToBottom = true;
        }

        private void OnCoreThinkingStateChanged(bool isThinking)
        {
            _isThinking = isThinking;
        }

        private void OnCoreExpressionChanged(int id)
        {
            SetPortrait(id);
        }

        private void SyncPortraitFromCore()
        {
            if (_core == null)
            {
                return;
            }

            SetPortrait(_core.ExpressionId);
        }

        public List<(string role, string message)> GetHistorySnapshot()
        {
            if (_core != null)
            {
                return _core.GetHistorySnapshot();
            }

            return _history?.ToList() ?? new List<(string role, string message)>();
        }

        private void PersistHistory()
        {
            try
            {
                var historyManager = Find.World?.GetComponent<AIHistoryManager>();
                historyManager?.SaveHistory(def.defName, _history);
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[WulaAI] Failed to persist AI history: {ex}");
            }
        }

        private void LoadPortraits()
        {
            for (int i = 1; i <= 6; i++)
            {
                string path = $"Wula/Events/Portraits/WULA_Legion_{i}";
                Texture2D tex = ContentFinder<Texture2D>.Get(path, false);
                if (tex != null)
                {
                    _portraits[i] = tex;
                }
                else
                {
                    WulaLog.Debug($"[WulaAI] Failed to load portrait: {path}");
                }
            }
            
            // Use portraitPath from def as the initial portrait
            if (this.portrait != null)
            {
                // Find the ID of the initial portrait
                var initial = _portraits.FirstOrDefault(kvp => kvp.Value == this.portrait);
                if (initial.Key != 0)
                {
                    _currentPortraitId = initial.Key;
                }
            }
            else if (_portraits.ContainsKey(2)) // Fallback to 2 if def has no portrait
            {
                this.portrait = _portraits[2];
                _currentPortraitId = 2;
            }
        }

        public void SetPortrait(int id)
        {
            if (_portraits.ContainsKey(id))
            {
                this.portrait = _portraits[id];
                _currentPortraitId = id;
            }
            else
            {
                WulaLog.Debug($"[WulaAI] Portrait ID {id} not found.");
            }
        }

        private async void StartConversation()
        {
            var historyManager = Find.World.GetComponent<AIHistoryManager>();
            _history = historyManager.GetHistory(def.defName);
            if (_history.Count == 0)
            {
                _history.Add(("user", "Hello"));
                PersistHistory();
                await RunPhasedRequestAsync();
            }
            else
            {
                var lastAIResponse = _history.LastOrDefault(x => x.role == "assistant");
                if (lastAIResponse.message != null)
                {
                    ParseResponse(lastAIResponse.message);
                }
                else
                {
                    await RunPhasedRequestAsync();
                }
            }
        }

        private string GetSystemInstruction(bool toolsEnabled, string toolsForThisPhase)
        {
            // Use XML persona if available, otherwise default
            string persona = !string.IsNullOrEmpty(def.aiSystemInstruction) ? def.aiSystemInstruction : DefaultPersona;
            
            string fullInstruction = toolsEnabled
                ? (persona + "\n" + ToolRulesInstruction + "\n" + toolsForThisPhase)
                : persona;

            string language = LanguageDatabase.activeLanguage.FriendlyNameNative;
            var eventVarManager = Find.World.GetComponent<EventVariableManager>();
            int goodwill = eventVarManager.GetVariable<int>("Wula_Goodwill_To_PIA", 0);
            string goodwillContext = $"Current Goodwill with P.I.A: {goodwill}. ";
            if (goodwill < -50) goodwillContext += "You are hostile and dismissive towards the player.";
            else if (goodwill < 0) goodwillContext += "You are cold and impatient.";
            else if (goodwill > 50) goodwillContext += "You are somewhat approving and helpful.";
            else goodwillContext += "You are neutral and business-like.";
            
            if (!toolsEnabled)
            {
                return $"{fullInstruction}\n{goodwillContext}\nIMPORTANT: You MUST reply in the following language: {language}.\n" +
                       "IMPORTANT: Tool calls are DISABLED in this turn. Reply in natural language only. Do NOT output any XML. " +
                       "You MAY include [EXPR:n] to set your expression (n=1-6).";
            }

            // Tool phases: avoid instructing the model to "reply" in a human language, because it must output XML only.
            // We still provide the language so it can be used later in the reply phase.
            return $"{fullInstruction}\n{goodwillContext}\nIMPORTANT: Output XML tool calls only (or <no_action/>). " +
                   $"You will produce the natural-language reply later and MUST use: {language}.";
        }

        private string GetToolSystemInstruction(RequestPhase phase)
        {
            string phaseInstruction = GetPhaseInstruction(phase).TrimEnd();
            string toolsForThisPhase = BuildToolsForPhase(phase);
            string actionPriority = phase == RequestPhase.ActionTools
                ? "ACTION TOOL PRIORITY:\n" +
                  "- spawn_resources\n" +
                  "- send_reinforcement\n" +
                  "- call_bombardment\n" +
                  "- modify_goodwill\n" +
                  "If no action is required, output exactly: <no_action/>.\n" +
                  "Query tools exist but are disabled in this phase (not listed here).\n"
                : string.Empty;
            string actionWhitelist = phase == RequestPhase.ActionTools
                ? "ACTION PHASE VALID TAGS ONLY:\n" +
                  "<spawn_resources>, <send_reinforcement>, <call_bombardment>, <modify_goodwill>, <no_action/>\n" +
                  "INVALID EXAMPLES (do NOT use now): <get_map_resources/>, <search_thing_def/>, <search_pawn_kind/>\n"
                : string.Empty;

            return string.Join("\n\n", new[]
            {
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
            sb.AppendLine("Use XML tool calls only, or <no_action/> if no tools are needed.");
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
                    "- Output XML tool calls only, or exactly: <no_action/>.\n" +
                    "- Prefer query tools (get_*/search_*).\n" +
                    "- You MAY call multiple tools in one response, but keep it concise.\n" +
                    "- If the user requests multiple items or information, you MUST output ALL required tool calls in this SAME response.\n" +
                    "- Action tools are available in PHASE 2 only; do NOT use them here.\n" +
                    "After this phase, the game will automatically proceed to PHASE 2.\n" +
                    "Output: XML only.\n",
                RequestPhase.ActionTools =>
                    "# PHASE 2/3 (Action Tools)\n" +
                    "Goal: Execute in-game actions based on known info.\n" +
                    "Rules:\n" +
                    "- You MUST NOT write any natural language to the user in this phase.\n" +
                    "- Output XML tool calls only, or exactly: <no_action/>.\n" +
                    "- ONLY action tools are accepted in this phase (spawn_resources, send_reinforcement, call_bombardment, modify_goodwill).\n" +
                    "- Query tools (get_*/search_*) will be ignored.\n" +
                    "- Prefer action tools (spawn_resources, send_reinforcement, call_bombardment, modify_goodwill).\n" +
                    "- Avoid queries unless absolutely required.\n" +
                    "- If no action is required based on query results, output <no_action/>.\n" +
                    "- If you already executed the needed action earlier this turn, output <no_action/>.\n" +
                    "After this phase, the game will automatically proceed to PHASE 3.\n" +
                    "Output: XML only.\n",
                RequestPhase.Reply =>
                    "# PHASE 3/3 (Reply)\n" +
                    "Goal: Reply to the player.\n" +
                    "Rules:\n" +
                    "- Tool calls are DISABLED.\n" +
                    "- You MUST write natural language only.\n" +
                    "- Do NOT output any XML.\n" +
                    "- If you want to set your expression, include: [EXPR:n] (n=1-6).\n",
                _ => ""
            };
        }

        private static bool IsXmlToolCall(string response)
        {
            if (string.IsNullOrWhiteSpace(response)) return false;
            return Regex.IsMatch(response, @"<([a-zA-Z0-9_]+)(?:>.*?</\1>|/>)", RegexOptions.Singleline);
        }

        private static bool IsNoActionOnly(string response)
        {
            if (string.IsNullOrWhiteSpace(response)) return false;
            var matches = Regex.Matches(response, @"<([a-zA-Z0-9_]+)(?:>.*?</\1>|/>)", RegexOptions.Singleline);
            return matches.Count == 1 &&
                   matches[0].Groups[1].Value.Equals("no_action", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasActionToolCall(string response)
        {
            if (string.IsNullOrWhiteSpace(response)) return false;
            var matches = Regex.Matches(response, @"<([a-zA-Z0-9_]+)(?:>.*?</\1>|/>)", RegexOptions.Singleline);
            foreach (Match match in matches)
            {
                var toolName = match.Groups[1].Value;
                if (IsActionToolName(toolName))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool ShouldRetryTools(string response)
        {
            if (string.IsNullOrWhiteSpace(response)) return false;
            return Regex.IsMatch(response, @"<\s*retry_tools\s*/\s*>", RegexOptions.IgnoreCase) ||
                   Regex.IsMatch(response, @"<\s*retry_tools\s*>", RegexOptions.IgnoreCase);
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
                   toolName == "modify_goodwill";
        }

        private static bool IsQueryToolName(string toolName)
        {
            if (string.IsNullOrWhiteSpace(toolName)) return false;
            return toolName.StartsWith("get_", StringComparison.OrdinalIgnoreCase) ||
                   toolName.StartsWith("search_", StringComparison.OrdinalIgnoreCase);
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

        private async Task RunPhasedRequestAsync()
        {
            if (_isThinking) return;
            _isThinking = true;
            SetThinkingPhase(1, false);
            _options.Clear();
            _scrollToBottom = true;
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

            try
            {
                CompressHistoryIfNeeded();

                var settings = WulaFallenEmpireMod.settings;
                if (string.IsNullOrEmpty(settings.apiKey))
                {
                    _currentResponse = "Error: API Key not configured in Mod Settings.";
                    return;
                }

                var client = new SimpleAIClient(settings.apiKey, settings.baseUrl, settings.model);

                var queryPhase = RequestPhase.QueryTools;
                if (Prefs.DevMode)
                {
                    WulaLog.Debug($"[WulaAI] ===== Turn 1/3 ({queryPhase}) =====");
                }

                string queryInstruction = GetToolSystemInstruction(queryPhase);
                string queryResponse = await client.GetChatCompletionAsync(queryInstruction, BuildToolContext(queryPhase), maxTokens: 128, temperature: 0.1f);
                if (string.IsNullOrEmpty(queryResponse))
                {
                    _currentResponse = "Wula_AI_Error_ConnectionLost".Translate();
                    return;
                }

                if (!IsXmlToolCall(queryResponse))
                {
                    if (Prefs.DevMode)
                    {
                        WulaLog.Debug("[WulaAI] Turn 1/3 missing XML; treating as <no_action/>");
                    }
                    queryResponse = "<no_action/>";
                }

                PhaseExecutionResult queryResult = await ExecuteXmlToolsForPhase(queryResponse, queryPhase);

                if (!queryResult.AnyToolSuccess && !_queryRetryUsed)
                {
                    _queryRetryUsed = true;
                    string lastUserMessage = _history.LastOrDefault(entry => entry.role == "user").message ?? "";
                    string persona = !string.IsNullOrEmpty(def.aiSystemInstruction) ? def.aiSystemInstruction : DefaultPersona;
                    string retryInstruction = persona +
                                              "\n\n# RETRY DECISION\n" +
                                              "No successful tool calls occurred in PHASE 1 (Query).\n" +
                                              "If you need to use tools in PHASE 1, output exactly: <retry_tools/>.\n" +
                                              "If you will proceed without actions, output exactly: <no_retry/>.\n" +
                                              "Output the XML tag only and NOTHING else.\n" +
                                              "\nLast user request:\n" + lastUserMessage;

                    string retryDecision = await client.GetChatCompletionAsync(retryInstruction, new List<(string role, string message)>(), maxTokens: 16, temperature: 0.1f);
                    if (!string.IsNullOrEmpty(retryDecision) && ShouldRetryTools(retryDecision))
                    {
                        if (Prefs.DevMode)
                        {
                            WulaLog.Debug("[WulaAI] Retry requested; re-opening query phase once.");
                        }

                        SetThinkingPhase(1, true);
                        string retryQueryInstruction = GetToolSystemInstruction(queryPhase) +
                                                       "\n\n# RETRY\nYou chose to retry. Output XML tool calls only (or <no_action/>).";
                        string retryQueryResponse = await client.GetChatCompletionAsync(retryQueryInstruction, BuildToolContext(queryPhase), maxTokens: 128, temperature: 0.1f);
                        if (string.IsNullOrEmpty(retryQueryResponse))
                        {
                            _currentResponse = "Wula_AI_Error_ConnectionLost".Translate();
                            return;
                        }

                        if (!IsXmlToolCall(retryQueryResponse))
                        {
                            if (Prefs.DevMode)
                            {
                                WulaLog.Debug("[WulaAI] Retry query phase missing XML; treating as <no_action/>");
                            }
                            retryQueryResponse = "<no_action/>";
                        }

                        queryResult = await ExecuteXmlToolsForPhase(retryQueryResponse, queryPhase);
                    }
                }

                var actionPhase = RequestPhase.ActionTools;
                if (Prefs.DevMode)
                {
                    WulaLog.Debug($"[WulaAI] ===== Turn 2/3 ({actionPhase}) =====");
                }

                SetThinkingPhase(2, false);
                string actionInstruction = GetToolSystemInstruction(actionPhase);
                var actionContext = BuildToolContext(actionPhase, includeUser: true);
                string actionResponse = await client.GetChatCompletionAsync(actionInstruction, actionContext, maxTokens: 128, temperature: 0.1f);
                if (string.IsNullOrEmpty(actionResponse))
                {
                    _currentResponse = "Wula_AI_Error_ConnectionLost".Translate();
                    return;
                }

                bool actionHasXml = IsXmlToolCall(actionResponse);
                bool actionIsNoActionOnly = IsNoActionOnly(actionResponse);
                bool actionHasActionTool = actionHasXml && HasActionToolCall(actionResponse);
                if (!actionHasXml || (!actionHasActionTool && !actionIsNoActionOnly))
                {
                    if (Prefs.DevMode)
                    {
                        WulaLog.Debug("[WulaAI] Turn 2/3 missing XML or no action tool; attempting XML-only conversion.");
                    }
                    string fixInstruction = "# FORMAT FIX (ACTION XML ONLY)\n" +
                                            "Preserve the intent of the previous output.\n" +
                                            "If the previous output indicates no action is needed or refuses action, output exactly: <no_action/>.\n" +
                                            "Do NOT invent new actions.\n" +
                                            "Output VALID XML tool calls only. No natural language, no commentary.\n" +
                                            "Allowed tags: <spawn_resources>, <send_reinforcement>, <call_bombardment>, <modify_goodwill>, <no_action/>.\n" +
                                            "\nAction tool XML formats:\n" +
                                            "- <spawn_resources><items><item><name>DefName</name><count>Int</count></item></items></spawn_resources>\n" +
                                            "- <send_reinforcement><units>PawnKindDef: Count, ...</units></send_reinforcement>\n" +
                                            "- <call_bombardment><abilityDef>DefName</abilityDef><x>Int</x><z>Int</z></call_bombardment>\n" +
                                            "- <modify_goodwill><amount>Int</amount></modify_goodwill>\n" +
                                            "\nPrevious output:\n" + TrimForPrompt(actionResponse, 600);
                    string fixedResponse = await client.GetChatCompletionAsync(fixInstruction, actionContext, maxTokens: 128, temperature: 0.1f);
                    bool fixedHasXml = !string.IsNullOrEmpty(fixedResponse) && IsXmlToolCall(fixedResponse);
                    bool fixedIsNoActionOnly = fixedHasXml && IsNoActionOnly(fixedResponse);
                    bool fixedHasActionTool = fixedHasXml && HasActionToolCall(fixedResponse);
                    if (fixedHasXml && (fixedHasActionTool || fixedIsNoActionOnly))
                    {
                        actionResponse = fixedResponse;
                    }
                    else
                    {
                        if (Prefs.DevMode)
                        {
                            WulaLog.Debug("[WulaAI] Turn 2/3 conversion failed; treating as <no_action/>");
                        }
                        actionResponse = "<no_action/>";
                    }
                }

                PhaseExecutionResult actionResult = await ExecuteXmlToolsForPhase(actionResponse, actionPhase);
                if (!actionResult.AnyActionSuccess && !_actionRetryUsed)
                {
                    _actionRetryUsed = true;
                    string lastUserMessage = _history.LastOrDefault(entry => entry.role == "user").message ?? "";
                    string persona = !string.IsNullOrEmpty(def.aiSystemInstruction) ? def.aiSystemInstruction : DefaultPersona;
                    string retryInstruction = persona +
                                              "\n\n# RETRY DECISION\n" +
                                              "No successful action tools occurred in PHASE 2 (Action).\n" +
                                              "If you need to execute an in-game action, output exactly: <retry_tools/>.\n" +
                                              "If you will proceed without actions, output exactly: <no_retry/>.\n" +
                                              "Output the XML tag only and NOTHING else.\n" +
                                              "\nLast user request:\n" + lastUserMessage;

                    string retryDecision = await client.GetChatCompletionAsync(retryInstruction, new List<(string role, string message)>(), maxTokens: 16, temperature: 0.1f);
                    if (!string.IsNullOrEmpty(retryDecision) && ShouldRetryTools(retryDecision))
                    {
                        if (Prefs.DevMode)
                        {
                            WulaLog.Debug("[WulaAI] Retry requested; re-opening action phase once.");
                        }

                        SetThinkingPhase(2, true);
                        string retryActionInstruction = GetToolSystemInstruction(actionPhase) +
                                                         "\n\n# RETRY\nYou chose to retry. Output XML tool calls only (or <no_action/>).";
                        var retryActionContext = BuildToolContext(actionPhase, includeUser: true);
                        string retryActionResponse = await client.GetChatCompletionAsync(retryActionInstruction, retryActionContext, maxTokens: 128, temperature: 0.1f);
                        if (string.IsNullOrEmpty(retryActionResponse))
                        {
                            _currentResponse = "Wula_AI_Error_ConnectionLost".Translate();
                            return;
                        }

                        if (!IsXmlToolCall(retryActionResponse))
                        {
                            if (Prefs.DevMode)
                        {
                            WulaLog.Debug("[WulaAI] Retry action phase missing XML; attempting XML-only conversion.");
                        }
                            string retryFixInstruction = "# FORMAT FIX (ACTION XML ONLY)\n" +
                                                        "Preserve the intent of the previous output.\n" +
                                                        "If the previous output indicates no action is needed or refuses action, output exactly: <no_action/>.\n" +
                                                        "Do NOT invent new actions.\n" +
                                                        "Output VALID XML tool calls only. No natural language, no commentary.\n" +
                                                        "Allowed tags: <spawn_resources>, <send_reinforcement>, <call_bombardment>, <modify_goodwill>, <no_action/>.\n" +
                                                        "\nAction tool XML formats:\n" +
                                                        "- <spawn_resources><items><item><name>DefName</name><count>Int</count></item></items></spawn_resources>\n" +
                                                        "- <send_reinforcement><units>PawnKindDef: Count, ...</units></send_reinforcement>\n" +
                                                        "- <call_bombardment><abilityDef>DefName</abilityDef><x>Int</x><z>Int</z></call_bombardment>\n" +
                                                        "- <modify_goodwill><amount>Int</amount></modify_goodwill>\n" +
                                                        "\nPrevious output:\n" + TrimForPrompt(retryActionResponse, 600);
                        string retryFixedResponse = await client.GetChatCompletionAsync(retryFixInstruction, retryActionContext, maxTokens: 128, temperature: 0.1f);
                        bool retryFixedHasXml = !string.IsNullOrEmpty(retryFixedResponse) && IsXmlToolCall(retryFixedResponse);
                        bool retryFixedIsNoActionOnly = retryFixedHasXml && IsNoActionOnly(retryFixedResponse);
                        bool retryFixedHasActionTool = retryFixedHasXml && HasActionToolCall(retryFixedResponse);
                        if (retryFixedHasXml && (retryFixedHasActionTool || retryFixedIsNoActionOnly))
                        {
                            retryActionResponse = retryFixedResponse;
                        }
                            else
                            {
                                if (Prefs.DevMode)
                                {
                                    WulaLog.Debug("[WulaAI] Retry action conversion failed; treating as <no_action/>");
                                }
                                retryActionResponse = "<no_action/>";
                            }
                        }

                        actionResult = await ExecuteXmlToolsForPhase(retryActionResponse, actionPhase);
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

                string reply = await client.GetChatCompletionAsync(replyInstruction, BuildReplyHistory());
                if (string.IsNullOrEmpty(reply))
                {
                    _currentResponse = "Wula_AI_Error_ConnectionLost".Translate();
                    return;
                }

                bool replyHadXml = IsXmlToolCall(reply);
                string strippedReply = StripXmlTags(reply)?.Trim() ?? "";
                if (replyHadXml || string.IsNullOrWhiteSpace(strippedReply))
                {
                    string retryReplyInstruction = replyInstruction +
                                                  "\n\n# RETRY (REPLY OUTPUT)\n" +
                                                  "Your last reply included XML or was empty. Tool calls are DISABLED.\n" +
                                                  "You MUST reply in natural language only. Do NOT output any XML.\n";
                    string retryReply = await client.GetChatCompletionAsync(retryReplyInstruction, BuildReplyHistory(), maxTokens: 256, temperature: 0.3f);
                    if (!string.IsNullOrEmpty(retryReply))
                    {
                        reply = retryReply;
                        replyHadXml = IsXmlToolCall(reply);
                        strippedReply = StripXmlTags(reply)?.Trim() ?? "";
                    }
                }

                if (replyHadXml)
                {
                    string cleaned = StripXmlTags(reply)?.Trim() ?? "";
                    if (string.IsNullOrWhiteSpace(cleaned))
                    {
                    cleaned = "AI returned a tool-only response (XML), which was blocked. Retry or use /clear to reset context.";
                    }
                    reply = cleaned;
                }

                ParseResponse(reply);
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[WulaAI] Exception in RunPhasedRequestAsync: {ex}");
                _currentResponse = "Wula_AI_Error_Internal".Translate(ex.Message);
            }
            finally
            {
                _isThinking = false;
            }
        }

        private async Task<PhaseExecutionResult> ExecuteXmlToolsForPhase(string xml, RequestPhase phase)
        {
            if (phase == RequestPhase.Reply)
            {
                await Task.CompletedTask;
                return default;
            }

            string guidance = "ToolRunner Guidance: Reply to the player in natural language only. Do NOT output any XML. You may include [EXPR:n] to set expression (n=1-6).";

            var matches = Regex.Matches(xml ?? "", @"<([a-zA-Z0-9_]+)(?:>.*?</\1>|/>)", RegexOptions.Singleline);
            if (matches.Count == 0)
            {
                UpdatePhaseToolLedger(phase, false, new List<string>());
                _history.Add(("assistant", "<no_action/>"));
                _history.Add(("tool", $"[Tool Results]\nTool 'no_action' Result: No action taken.\n{guidance}"));
                PersistHistory();
                UpdateActionLedgerNote();
                return default;
            }
            if (matches.Count == 1 && matches[0].Groups[1].Value.Equals("no_action", StringComparison.OrdinalIgnoreCase))
            {
                UpdatePhaseToolLedger(phase, false, new List<string>());
                _history.Add(("assistant", "<no_action/>"));
                _history.Add(("tool", $"[Tool Results]\nTool 'no_action' Result: No action taken.\n{guidance}"));
                PersistHistory();
                UpdateActionLedgerNote();
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
            StringBuilder combinedResults = new StringBuilder();
            StringBuilder xmlOnlyBuilder = new StringBuilder();

            bool countActionSuccessOnly = phase == RequestPhase.ActionTools;

            foreach (Match match in matches)
            {
                if (executed >= maxTools)
                {
                    combinedResults.AppendLine($"ToolRunner Note: Skipped remaining tools because this phase allows at most {maxTools} tool call(s).");
                    break;
                }

                string toolCallXml = match.Value;
                string toolName = match.Groups[1].Value;

                if (toolName.Equals("no_action", StringComparison.OrdinalIgnoreCase))
                {
                    combinedResults.AppendLine("ToolRunner Note: Ignored <no_action/> because other tool calls were present.");
                    continue;
                }

                if (xmlOnlyBuilder.Length > 0) xmlOnlyBuilder.AppendLine().AppendLine();
                xmlOnlyBuilder.Append(toolCallXml);

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

                string argsXml = toolCallXml;
                var contentMatch = Regex.Match(toolCallXml, $@"<{toolName}>(.*?)</{toolName}>", RegexOptions.Singleline);
                if (contentMatch.Success)
                {
                    argsXml = contentMatch.Groups[1].Value;
                }

                if (Prefs.DevMode)
                {
                    WulaLog.Debug($"[WulaAI] Executing tool (phase {phase}): {toolName} with args: {argsXml}");
                }

                string result = tool.Execute(argsXml).Trim();
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

            string nonXmlText = StripXmlTags(xml);
            if (!string.IsNullOrWhiteSpace(nonXmlText))
            {
                combinedResults.AppendLine("ToolRunner Note: Non-XML text in the tool phase was ignored.");
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

            string xmlOnly = xmlOnlyBuilder.Length == 0 ? "<no_action/>" : xmlOnlyBuilder.ToString().Trim();
            _history.Add(("toolcall", xmlOnly));
            _history.Add(("tool", $"[Tool Results]\n{combinedResults.ToString().Trim()}"));
            PersistHistory();

            UpdatePhaseToolLedger(phase, successfulToolCall, successfulTools);
            UpdateActionLedgerNote();

            // Between phases, do not request the model again here; RunPhasedRequestAsync controls the sequence.
            await Task.CompletedTask;
            return new PhaseExecutionResult
            {
                AnyToolSuccess = successfulToolCall,
                AnyActionSuccess = successfulActions.Count > 0,
                AnyActionError = failedActions.Count > 0
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

        private static string StripXmlTags(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            string stripped = Regex.Replace(text, @"<([a-zA-Z0-9_]+)[^>]*>.*?</\1>", "", RegexOptions.Singleline);
            stripped = Regex.Replace(stripped, @"<([a-zA-Z0-9_]+)[^>]*/>", "");
            return stripped;
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

                string stripped = StripXmlTags(entry.message)?.Trim() ?? "";
                if (!string.IsNullOrWhiteSpace(stripped))
                {
                    filtered.Add(entry);
                }
            }

            return filtered;
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
                SetPortrait(exprId);
            }

            return matches.Count > 0 ? ExpressionTagRegex.Replace(text, "").Trim() : text;
        }

        private void ParseResponse(string rawResponse, bool addToHistory = true)
        {
            string cleanedResponse = StripExpressionTags(rawResponse ?? "");
            _currentResponse = cleanedResponse;
            var parts = cleanedResponse.Split(new[] { "OPTIONS:" }, StringSplitOptions.None);
            if (addToHistory)
            {
                if (_history.Count == 0 || _history.Last().role != "assistant")
                {
                    _history.Add(("assistant", cleanedResponse));
                    PersistHistory();
                }
                else if (_history.Last().message != cleanedResponse)
                {
                    if (_history.Last().message == rawResponse)
                    {
                        _history[_history.Count - 1] = ("assistant", cleanedResponse);
                    }
                    else
                    {
                        _history.Add(("assistant", cleanedResponse));
                    }
                    PersistHistory();
                }
            }

            if (!string.IsNullOrEmpty(ParseResponseForDisplay(cleanedResponse)))
            {
                _scrollToBottom = true;
            }
            if (parts.Length > 1)
            {
                _options.Clear();
                var optionsLines = parts[1].Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in optionsLines)
                {
                    string opt = line.Trim();
                    int dotIndex = opt.IndexOf('.');
                    if (dotIndex != -1 && dotIndex < 4) opt = opt.Substring(dotIndex + 1).Trim();
                    if (!string.IsNullOrEmpty(opt)) _options.Add(opt);
                }
            }
        }
        public override void DoWindowContents(Rect inRect)
        {
            if (background != null) GUI.DrawTexture(inRect, background, ScaleMode.ScaleAndCrop);

            if (_core != null)
            {
                _history = _core.GetHistorySnapshot();
                _isThinking = _core.IsThinking;
            }

            // 右上角：切换到小窗口按钮
            // 左上角：切换到小窗口按钮
            Rect switchBtnRect = new Rect(0f, 0f, 25f, 25f);
            base.DrawCustomButton(switchBtnRect, "-", isEnabled: true);
            if (Widgets.ButtonInvisible(switchBtnRect)) 
            {
                EventDef eventDef = this.def;
                if (eventDef != null)
                {
                    var existing = Find.WindowStack.WindowOfType<Overlay_WulaLink>();
                    if (existing != null)
                    {
                        existing.Expand();
                    }
                    else
                    {
                        Find.WindowStack.Add(new Overlay_WulaLink(eventDef));
                    }
                    this.Close(); // 关闭当前大窗口
                }
            }

            // 瀹氫箟杈硅窛
            float margin = 15f;
            Rect paddedRect = inRect.ContractedBy(margin);

            float curY = paddedRect.y;
            float width = paddedRect.width;

            // 绔嬬粯涓嶉渶瑕佽竟璺濓紝鎵€浠ヤ娇鐢ㄥ師濮媔nRect鐨勪綅缃?
            if (portrait != null)
            {
                Rect scaledPortraitRect = Dialog_CustomDisplay.Config.GetScaledRect(Dialog_CustomDisplay.Config.portraitSize, inRect, true);
                Rect portraitRect = new Rect((inRect.width - scaledPortraitRect.width) / 2, inRect.y, scaledPortraitRect.width, scaledPortraitRect.height);
                GUI.DrawTexture(portraitRect, portrait, ScaleMode.ScaleToFit);

                if (Prefs.DevMode)
                {
                    // DEBUG: Draw portrait ID
                    Text.Font = GameFont.Medium;
                    Text.Anchor = TextAnchor.UpperRight;
                    Widgets.Label(portraitRect, $"ID: {_currentPortraitId}");
                    Text.Anchor = TextAnchor.UpperLeft;
                    Text.Font = GameFont.Small;
                }

                curY = portraitRect.yMax + 10f;
            }

            // 浜虹墿鍚嶅瓧 - 灞呬腑鏄剧ず
            Text.Font = GameFont.Medium;
            string name = def.characterName ?? "The Legion";
            float nameHeight = Text.CalcHeight(name, width);

            // 鍒涘缓鍚嶅瓧鐨勭煩褰紝浣垮叾鍦ㄧ獥鍙ｆ按骞冲眳涓?
            Rect nameRect = new Rect(paddedRect.x, curY, width, nameHeight);
            Text.Anchor = TextAnchor.UpperCenter;  // 鏀逛负涓婁腑瀵归綈
            Widgets.Label(nameRect, name);
            Text.Anchor = TextAnchor.UpperLeft;    // 鎭㈠宸﹀榻?

            curY += nameHeight + 10f;

            // 璁＄畻杈撳叆妗嗛珮搴︺€侀€夐」楂樺害鍜岃亰澶╁巻鍙查珮搴?
            float inputHeight = 30f;
            float optionsHeight = _options.Any() ? 100f : 0f;
            float spacing = 10f;

            // 鑱婂ぉ鍘嗗彶鍖哄煙 - 浣跨敤甯﹁竟璺濈殑鐭╁舰
            float descriptionHeight = paddedRect.height - curY - inputHeight - optionsHeight - spacing * 2;
            Rect descriptionRect = new Rect(paddedRect.x, curY, width, descriptionHeight);
            DrawChatHistory(descriptionRect);

            if (_isThinking)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(descriptionRect, BuildThinkingStatus());
                Text.Anchor = TextAnchor.UpperLeft;
            }

            curY += descriptionHeight + spacing;

            // 閫夐」鍖哄煙
            Rect optionsRect = new Rect(paddedRect.x, curY, width, optionsHeight);
            if (!_isThinking && _options.Count > 0)
            {
                List<EventOption> eventOptions = _options.Select(opt => new EventOption { label = opt, useCustomColors = false }).ToList();
                DrawOptions(optionsRect, eventOptions);
            }

            curY += optionsHeight + spacing;

            // 杈撳叆妗嗗尯鍩?- 浣跨敤甯﹁竟璺濈殑鐭╁舰
            Rect inputRect = new Rect(paddedRect.x, curY, width, inputHeight);

            // 淇濆瓨褰撳墠瀛椾綋
            var originalFont = Text.Font;

            // 璁剧疆鏇村皬鐨勫瓧浣?
            if (Text.Font == GameFont.Small)
            {
                // 浣跨敤 Tiny 瀛椾綋
                Text.Font = GameFont.Tiny;
            }
            else
            {
                // 濡傛灉褰撳墠涓嶆槸 Small锛岄檷涓€绾?
                Text.Font = GameFont.Small;
            }

            // 璁＄畻杈撳叆妗嗘枃鏈珮搴?
            float textFieldHeight = Text.CalcHeight("Test", inputRect.width - 85);
            Rect textFieldRect = new Rect(inputRect.x, inputRect.y + (inputHeight - textFieldHeight) / 2, inputRect.width - 85, textFieldHeight);

            _inputText = Widgets.TextField(textFieldRect, _inputText);

            // 鍙戦€佹寜閽?- 浣跨敤涓嶥ialog_CustomDisplay鐩稿悓鐨勮嚜瀹氫箟鎸夐挳鏍峰紡
            // 淇濆瓨褰撳墠鐘舵€?
            var originalAnchor = Text.Anchor;
            var originalColor = GUI.color;

            // 璁剧疆瀛椾綋涓篢iny
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;

            // 发送按钮的矩形
            Rect sendButtonRect = new Rect(inputRect.xMax - 80, inputRect.y, 80, inputHeight);

            // 使用基类的DrawCustomButton方法绘制按钮（与Dialog_CustomDisplay一致）
            base.DrawCustomButton(sendButtonRect, "Wula_AI_Send".Translate(), isEnabled: true);

            // 恢复状态
            GUI.color = originalColor;
            Text.Anchor = originalAnchor;
            Text.Font = originalFont;

            // 处理点击事件
            bool sendButtonPressed = Widgets.ButtonInvisible(sendButtonRect);

            // 直接在DoWindowContents中处理Enter键，而不是调用单独的方法
            // 这是为了确保事件在正确的时机被处理
            if (Event.current.type == EventType.KeyDown)
            {
                // 检查是否按下了Enter键（主键盘或小键盘的Enter）
                if ((Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter) && !string.IsNullOrEmpty(_inputText))
                {
                    // 如果AI正在思考，不处理Enter键
                    if (!_isThinking)
                    {
                        SelectOption(_inputText);
                        _inputText = "";
                        // 消费这个事件，防止它传递到窗口的关闭逻辑
                        Event.current.Use();
                    }
                }
                // 可选：添加Escape键关闭窗口的功能
                else if (Event.current.keyCode == KeyCode.Escape)
                {
                    this.Close();
                    Event.current.Use();
                }
            }

            // 处理鼠标点击发送按钮
            if (sendButtonPressed && !string.IsNullOrEmpty(_inputText))
            {
                SelectOption(_inputText);
                _inputText = "";
            }
        }
        private void DrawChatHistory(Rect rect)
        {
            var originalFont = Text.Font;
            var originalAnchor = Text.Anchor;

            try
            {
                float viewHeight = 0f;
                var filteredHistory = _history.Where(e => e.role != "tool" && e.role != "system" && e.role != "toolcall").ToList();

                // 添加内边距
                float innerPadding = 5f;
                float contentWidth = rect.width - 16f - innerPadding * 2;

                // 预计计算高度 - 使用小字体
                for (int i = 0; i < filteredHistory.Count; i++)
                {
                    var entry = filteredHistory[i];
                    string text = entry.role == "assistant" ? ParseResponseForDisplay(entry.message) : entry.message;
                    if (string.IsNullOrWhiteSpace(text) || (entry.role == "user" && text.StartsWith("[Tool Results]"))) continue;
                    bool isLastMessage = i == filteredHistory.Count - 1;

                    // 设置更小的字体
                    if (isLastMessage && entry.role == "assistant")
                    {
                        Text.Font = GameFont.Small; // 原来是 Medium，改为 Small
                    }
                    else
                    {
                        Text.Font = GameFont.Tiny; // 原来是 Small，改为 Tiny
                    }
                    // 增加padding
                    float padding = (isLastMessage && entry.role == "assistant") ? 30f : 15f;
                    viewHeight += Text.CalcHeight(text, contentWidth) + padding + 10f;
                }

                Rect viewRect = new Rect(0f, 0f, rect.width - 16f, viewHeight);
                if (_scrollToBottom)
                {
                    _scrollPosition.y = float.MaxValue;
                    _scrollToBottom = false;
                }

                Widgets.BeginScrollView(rect, ref _scrollPosition, viewRect);

                float curY = 0f;
                for (int i = 0; i < filteredHistory.Count; i++)
                {
                    var entry = filteredHistory[i];
                    string text = entry.role == "assistant" ? ParseResponseForDisplay(entry.message) : entry.message;

                    if (string.IsNullOrEmpty(text) || (entry.role == "user" && text.StartsWith("[Tool Results]"))) continue;
                    bool isLastMessage = i == filteredHistory.Count - 1;

                    // 设置更小的字体
                    if (isLastMessage && entry.role == "assistant")
                    {
                        Text.Font = GameFont.Small; // 原来是 Medium，改为 Small
                    }
                    else
                    {
                        Text.Font = GameFont.Tiny; // 原来是 Small，改为 Tiny
                    }

                    float padding = (isLastMessage && entry.role == "assistant") ? 30f : 15f;
                    float height = Text.CalcHeight(text, contentWidth) + padding;

                    // 娣诲姞鍐呰竟璺?
                    Rect labelRect = new Rect(innerPadding, curY, contentWidth, height);

                    if (entry.role == "user")
                    {
                        Text.Anchor = TextAnchor.MiddleRight;
                        Widgets.Label(labelRect, $"<color=#add8e6>{text}</color>");
                    }
                    else
                    {
                        Text.Anchor = TextAnchor.MiddleLeft;
                        Widgets.Label(labelRect, $"P.I.A: {text}");
                    }
                    curY += height + 10f;
                }
                Widgets.EndScrollView();
            }
            finally
            {
                Text.Font = originalFont;
                Text.Anchor = originalAnchor;
            }
        }

        private string ParseResponseForDisplay(string rawResponse)
        {
            if (string.IsNullOrEmpty(rawResponse)) return "";
            
            string text = rawResponse;
            
            // Remove standard tags with content: <tag>content</tag>
            text = Regex.Replace(text, @"<([a-zA-Z0-9_]+)[^>]*>.*?</\1>", "", RegexOptions.Singleline);
            
            // Remove self-closing tags: <tag/>
            text = Regex.Replace(text, @"<([a-zA-Z0-9_]+)[^>]*/>", "");

            text = ExpressionTagRegex.Replace(text, "");
            
            text = text.Trim();
            
            return text.Split(new[] { "OPTIONS:" }, StringSplitOptions.None)[0].Trim();
        }

        private string BuildThinkingStatus()
        {
            if (!Prefs.DevMode)
            {
                 return "Wula_AI_Thinking_Simple".Translate();
            }
            // 开发者模式下也不再显示秒数计时，只显示阶段信息
            return "Wula_AI_Thinking_Status_NoTimer".Translate(_thinkingPhaseIndex, ThinkingPhaseTotal);
        }

        protected override void DrawSingleOption(Rect rect, EventOption option)
        {
            float optionWidth = Mathf.Min(rect.width, Dialog_CustomDisplay.Config.optionSize.x * (rect.width / Dialog_CustomDisplay.Config.windowSize.x));
            float optionX = rect.x + (rect.width - optionWidth) / 2;
            Rect optionRect = new Rect(optionX, rect.y, optionWidth, rect.height);

            var originalColor = GUI.color;
            var originalFont = Text.Font;
            var originalTextColor = GUI.contentColor;
            var originalAnchor = Text.Anchor;

            try
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.Font = GameFont.Small;
                DrawCustomButton(optionRect, option.label.Translate(), isEnabled: true);
                if (Widgets.ButtonInvisible(optionRect))
                {
                    SelectOption(option.label);
                }
            }
            finally
            {
                GUI.color = originalColor;
                Text.Font = originalFont;
                GUI.contentColor = originalTextColor;
                Text.Anchor = originalAnchor;
            }
        }

        private new void DrawCustomButton(Rect rect, string label, bool isEnabled = true)
        {
            bool isMouseOver = Mouse.IsOver(rect);
            Color buttonColor, textColor;
            if (!isEnabled)
            {
                buttonColor = new Color(0.15f, 0.15f, 0.15f, 0.6f);
                textColor = new Color(0.6f, 0.6f, 0.6f, 1f);
            }
            else if (isMouseOver)
            {
                buttonColor = new Color(0.6f, 0.3f, 0.3f, 1f);
                textColor = new Color(1f, 1f, 1f, 1f);
            }
            else
            {
                buttonColor = new Color(0.5f, 0.2f, 0.2f, 1f);
                textColor = new Color(0.9f, 0.9f, 0.9f, 1f);
            }

            GUI.color = buttonColor;
            Widgets.DrawBoxSolid(rect, buttonColor);
            if (isEnabled) Widgets.DrawBox(rect, 1);
            else Widgets.DrawBox(rect, 1);

            GUI.color = textColor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect.ContractedBy(4f), label);
            if (!isEnabled)
            {
                GUI.color = new Color(0.6f, 0.6f, 0.6f, 0.8f);
                Widgets.DrawLine(new Vector2(rect.x + 10f, rect.center.y), new Vector2(rect.xMax - 10f, rect.center.y), GUI.color, 1f);
            }
        }

        private async void SelectOption(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            if (_core != null)
            {
                if (string.Equals(text.Trim(), "/clear", StringComparison.OrdinalIgnoreCase))
                {
                    _isThinking = false;
                    _options.Clear();
                    _inputText = "";
                }

                _scrollToBottom = true;
                _core.SendUserMessage(text);
                _history = _core.GetHistorySnapshot();
                return;
            }

            if (string.Equals(text.Trim(), "/clear", StringComparison.OrdinalIgnoreCase))
            {
                _isThinking = false;
                _options.Clear();
                _inputText = "";

                _history.Clear();
                try
                {
                    var historyManager = Find.World?.GetComponent<AIHistoryManager>();
                    historyManager?.ClearHistory(def.defName);
                }
                catch (Exception ex)
                {
                    WulaLog.Debug($"[WulaAI] Failed to clear AI history: {ex}");
                }

                Messages.Message("AI conversation history cleared.", MessageTypeDefOf.NeutralEvent);
                return;
            }

            _history.Add(("user", text));
            PersistHistory();
            _scrollToBottom = true;
            await RunPhasedRequestAsync();
        }

        public override void PostClose()
        {
            if (_core != null)
            {
                _core.OnMessageReceived -= OnCoreMessageReceived;
                _core.OnThinkingStateChanged -= OnCoreThinkingStateChanged;
                _core.OnExpressionChanged -= OnCoreExpressionChanged;
            }

            if (Instance == this) Instance = null;
            if (_core == null)
            {
                PersistHistory();
            }
            base.PostClose();
            HandleAction(def.dismissEffects);
        }
    }
}




