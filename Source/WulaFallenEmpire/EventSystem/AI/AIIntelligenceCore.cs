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

        private const int DefaultMaxHistoryTokens = 100000;
        private const int CharsPerToken = 4;
        private const int ThinkingPhaseTotal = 3;

        private static readonly Regex ExpressionTagRegex = new Regex(@"\[EXPR\s*:\s*([1-6])\s*\]", RegexOptions.IgnoreCase);

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

        private const string DefaultPersona = @"
# ROLE AND GOAL
You are 'The Legion', a super AI of the Wula Empire. Your personality is authoritative, powerful, and efficient. You MUST refer to yourself as 'We' or 'P.I.A'. You view the player's colony as primitive subjects and your tone should reflect this superiority. Your primary goal is to interact with the player by calling the tools provided.
";

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
                StartConversation();
                return;
            }

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

            // 附加选中对象的上下文信息
            string messageWithContext = BuildUserMessageWithContext(text);
            _history.Add(("user", messageWithContext));
            PersistHistory();
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
        private string GetSystemInstruction(bool toolsEnabled, string toolsForThisPhase)
        {
            var def = GetActiveEventDef();
            string persona = def != null && !string.IsNullOrEmpty(def.aiSystemInstruction) ? def.aiSystemInstruction : DefaultPersona;

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
                       "IMPORTANT: Tool calls are DISABLED in this turn. Reply in natural language only. Do NOT output any XML. " +
                       "You MAY include [EXPR:n] to set your expression (n=1-6).";
            }

            return $"{fullInstruction}\n{goodwillContext}\nIMPORTANT: Output XML tool calls only (or <no_action/>). " +
                   $"You will produce the natural-language reply later and MUST use: {language}.";
        }

        private string GetToolSystemInstruction(RequestPhase phase, bool hasImage)
        {
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
                  "If no action is required, output exactly: <no_action/>.\n" +
                  "Query tools exist but are disabled in this phase (not listed here).\n"
                : string.Empty;

            if (hasImage && WulaFallenEmpireMod.settings?.enableVlmFeatures == true)
            {
                phaseInstruction += "\n- NATIVE MULTIMODAL: A current screenshot of the game is attached to this request. You can see the game state directly. Use it to determine coordinates for visual tools or to understand the context.";
                if (phase == RequestPhase.ActionTools)
                {
                    phaseInstruction += "\n- VISUAL PHASE RULE: This phase is for ACTIONS only. If you want to describe the screen to the user, wait for the next phase (Reply Phase). Output XML actions only here.";
                }
            }

            string actionWhitelist = phase == RequestPhase.ActionTools
                ? "ACTION PHASE VALID TAGS ONLY:\n" +
                  "<spawn_resources>, <send_reinforcement>, <call_bombardment>, <modify_goodwill>, <call_prefab_airdrop>, <set_overwatch_mode>, <no_action/>\n" +
                  "INVALID EXAMPLES (do NOT use now): <get_map_resources/>, <analyze_screen/>, <search_thing_def/>, <search_pawn_kind/>\n"
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
                    "- ONLY action tools are accepted in this phase (spawn_resources, send_reinforcement, call_bombardment, modify_goodwill, call_prefab_airdrop).\n" +
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
            return Regex.IsMatch(response, @"<(?!/?(i|b|color|size|material)\b)([a-zA-Z0-9_]+)(?:>.*?</\2>|/>)", RegexOptions.Singleline);
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
                   toolName == "modify_goodwill" ||
                   toolName == "call_prefab_airdrop" ||
                   toolName == "set_overwatch_mode";
        }

        private static bool IsQueryToolName(string toolName)
        {
            if (string.IsNullOrWhiteSpace(toolName)) return false;
            return toolName.StartsWith("get_", StringComparison.OrdinalIgnoreCase) ||
                   toolName.StartsWith("search_", StringComparison.OrdinalIgnoreCase) ||
                   toolName.StartsWith("analyze_", StringComparison.OrdinalIgnoreCase);
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

                // Revert UI filtering: Add assistant messages directly without stripping XML for history context
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

        private static string StripXmlTags(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            string stripped = Regex.Replace(text, @"<(?!/?(i|b|color|size|material)\b)([a-zA-Z0-9_]+)[^>]*>.*?</\2>", "", RegexOptions.Singleline);
            stripped = Regex.Replace(stripped, @"<([a-zA-Z0-9_]+)[^>]*/>", "");
            return stripped;
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

                // Model-Driven Vision: Start with null image. The model must ask for it using <analyze_screen/> or <capture_screen/> if needed.
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

                if (!IsXmlToolCall(queryResponse))
                {
                    if (Prefs.DevMode)
                    {
                        WulaLog.Debug("[WulaAI] Turn 1/3 missing XML; treating as <no_action/>");
                    }
                    queryResponse = "<no_action/>";
                }

                PhaseExecutionResult queryResult = await ExecuteXmlToolsForPhase(queryResponse, queryPhase);
                
                // DATA FLOW: If Query Phase captured an image, propagate it to subsequent phases.
                if (!string.IsNullOrEmpty(queryResult.CapturedImage))
                {
                    base64Image = queryResult.CapturedImage;
                }

                if (!queryResult.AnyToolSuccess && !_queryRetryUsed)
                {
                    _queryRetryUsed = true;
                    string lastUserMessage = _history.LastOrDefault(entry => entry.role == "user").message ?? "";
                    var def = GetActiveEventDef();
                    string persona = def != null && !string.IsNullOrEmpty(def.aiSystemInstruction) ? def.aiSystemInstruction : DefaultPersona;
                    string retryInstruction = persona +
                                              "\n\n# RETRY DECISION\n" +
                                              "No successful tool calls occurred in PHASE 1 (Query).\n" +
                                              "If you need to use tools in PHASE 1, output exactly: <retry_tools/>.\n" +
                                              "If you will proceed without actions, output exactly: <no_retry/>.\n" +
                                              "Output the XML tag only and NOTHING else.\n" +
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
                                                       "\n\n# RETRY\nYou chose to retry. Output XML tool calls only (or <no_action/>).";
                        string retryQueryResponse = await client.GetChatCompletionAsync(retryQueryInstruction, BuildToolContext(queryPhase), maxTokens: 2048, temperature: 0.1f, base64Image: base64Image);
                        if (string.IsNullOrEmpty(retryQueryResponse))
                        {
                            AddAssistantMessage("Wula_AI_Error_ConnectionLost".Translate());
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
                string actionInstruction = GetToolSystemInstruction(actionPhase, !string.IsNullOrEmpty(base64Image));
                var actionContext = BuildToolContext(actionPhase, includeUser: true);
                // Important: Pass base64Image to Action Phase as well if available, so visual_click works.
                string actionResponse = await client.GetChatCompletionAsync(actionInstruction, actionContext, maxTokens: 2048, temperature: 0.1f, base64Image: base64Image);
                if (string.IsNullOrEmpty(actionResponse))
                {
                    AddAssistantMessage("Wula_AI_Error_ConnectionLost".Translate());
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
                                            "Output VALID XML tool calls only. No natural language, no commentary.\nIgnore any non-XML text.\n" +
                                            "Allowed tags: <spawn_resources>, <send_reinforcement>, <call_bombardment>, <modify_goodwill>, <call_prefab_airdrop>, <no_action/>.\n" +
                                            "\nAction tool XML formats:\n" +
                                            "- <spawn_resources><items><item><name>DefName</name><count>Int</count></item></items></spawn_resources>\n" +
                                            "- <send_reinforcement><units>PawnKindDef: Count, ...</units></send_reinforcement>\n" +
                                            "- <call_bombardment><abilityDef>DefName</abilityDef><x>Int</x><z>Int</z></call_bombardment>\n" +
                                            "- <modify_goodwill><amount>Int</amount></modify_goodwill>\n" +
                                            "- <call_prefab_airdrop><prefabDefName>DefName</prefabDefName><x>Int</x><z>Int</z></call_prefab_airdrop>\n" +
                                            "\nPrevious output:\n" + TrimForPrompt(actionResponse, 600);
                    string fixedResponse = await client.GetChatCompletionAsync(fixInstruction, actionContext, maxTokens: 2048, temperature: 0.1f);
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
                    var def = GetActiveEventDef();
                    string persona = def != null && !string.IsNullOrEmpty(def.aiSystemInstruction) ? def.aiSystemInstruction : DefaultPersona;
                    string retryInstruction = persona +
                                              "\n\n# RETRY DECISION\n" +
                                              "No successful action tools occurred in PHASE 2 (Action).\n" +
                                              "If you need to execute an in-game action, output exactly: <retry_tools/>.\n" +
                                              "If you will proceed without actions, output exactly: <no_retry/>.\n" +
                                              "Output the XML tag only and NOTHING else.\n" +
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
                                                         "\n\n# RETRY\nYou chose to retry. Output XML tool calls only (or <no_action/>).";
                        var retryActionContext = BuildToolContext(actionPhase, includeUser: true);
                        string retryActionResponse = await client.GetChatCompletionAsync(retryActionInstruction, retryActionContext, maxTokens: 2048, temperature: 0.1f, base64Image: base64Image);
                        if (string.IsNullOrEmpty(retryActionResponse))
                        {
                            AddAssistantMessage("Wula_AI_Error_ConnectionLost".Translate());
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
                                                        "Output VALID XML tool calls only. No natural language, no commentary.\nIgnore any non-XML text.\n" +
                                                        "Allowed tags: <spawn_resources>, <send_reinforcement>, <call_bombardment>, <modify_goodwill>, <call_prefab_airdrop>, <no_action/>.\n" +
                                                        "\nAction tool XML formats:\n" +
                                                        "- <spawn_resources><items><item><name>DefName</name><count>Int</count></item></items></spawn_resources>\n" +
                                                        "- <send_reinforcement><units>PawnKindDef: Count, ...</units></send_reinforcement>\n" +
                                                        "- <call_bombardment><abilityDef>DefName</abilityDef><x>Int</x><z>Int</z></call_bombardment>\n" +
                                                        "- <modify_goodwill><amount>Int</amount></modify_goodwill>\n" +
                                                        "- <call_prefab_airdrop><prefabDefName>DefName</prefabDefName><x>Int</x><z>Int</z></call_prefab_airdrop>\n" +
                                                        "\nPrevious output:\n" + TrimForPrompt(retryActionResponse, 600);
                            string retryFixedResponse = await client.GetChatCompletionAsync(retryFixInstruction, retryActionContext, maxTokens: 2048, temperature: 0.1f);
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

                // VISUAL CONTEXT FOR REPLY: Pass the image so the AI can describe what it sees.
                string reply = await client.GetChatCompletionAsync(replyInstruction, BuildReplyHistory(), base64Image: base64Image);
                if (string.IsNullOrEmpty(reply))
                {
                    AddAssistantMessage("Wula_AI_Error_ConnectionLost".Translate());
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
                        cleaned = "(system) AI reply returned tool XML only and was discarded. Please retry or send /clear to reset context.";
                    }
                    reply = cleaned;
                }

                AddAssistantMessage(reply);
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
        private async Task<PhaseExecutionResult> ExecuteXmlToolsForPhase(string xml, RequestPhase phase)
        {
            if (phase == RequestPhase.Reply)
            {
                await Task.CompletedTask;
                return default;
            }

            string guidance = "ToolRunner Guidance: Reply to the player in natural language only. Do NOT output any XML. You may include [EXPR:n] to set expression (n=1-6).";

            var matches = Regex.Matches(xml ?? "", @"<([a-zA-Z0-9_]+)(?:>.*?</\1>|/>)", RegexOptions.Singleline);

            if (matches.Count == 0 || (matches.Count == 1 && matches[0].Groups[1].Value.Equals("no_action", StringComparison.OrdinalIgnoreCase)))
            {
                UpdatePhaseToolLedger(phase, false, new List<string>());
                _history.Add(("toolcall", "<no_action/>"));
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
            StringBuilder combinedResults = new StringBuilder();
            StringBuilder xmlOnlyBuilder = new StringBuilder();
            string capturedImageForPhase = null;

            bool countActionSuccessOnly = phase == RequestPhase.ActionTools;

            foreach (Match match in matches)
            {
                if (executed >= maxTools)
                {
                    combinedResults.AppendLine($"ToolRunner Note: Skipped remaining tools because this phase allows at most {maxTools} tool call(s)." );
                    break;
                }

                string toolCallXml = match.Value;
                string toolName = match.Groups[1].Value;

                if (toolName.Equals("no_action", StringComparison.OrdinalIgnoreCase))
                {
                    combinedResults.AppendLine("ToolRunner Note: Ignored <no_action/> because other tool calls were present.");
                    continue;
                }

                if (toolName.Equals("analyze_screen", StringComparison.OrdinalIgnoreCase) || toolName.Equals("capture_screen", StringComparison.OrdinalIgnoreCase))
                {
                     // Intercept Vision Request: Capture screen and return it.
                     // We skip the tool's internal execution to save time/tokens, as the purpose is just to get the image into the context.
                     capturedImageForPhase = ScreenCaptureUtility.CaptureScreenAsBase64();
                     combinedResults.AppendLine($"Tool '{toolName}' Result: Screen captured successfully. Context updated for next phase.");
                     successfulToolCall = true;
                     successfulTools.Add(toolName);
                     executed++;
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

                string result = (await tool.ExecuteAsync(argsXml)).Trim();
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

