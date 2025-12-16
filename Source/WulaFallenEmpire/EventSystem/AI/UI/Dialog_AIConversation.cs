using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RimWorld;
using UnityEngine;
using Verse;
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
        private Dictionary<int, Texture2D> _portraits = new Dictionary<int, Texture2D>();
        private const int DefaultMaxHistoryTokens = 100000;
        private const int CharsPerToken = 4;
        private int _continuationDepth = 0;
        private const int MaxContinuationDepth = 6;

        private readonly List<string> _recentToolSignatures = new List<string>();
        private bool _toolLoopGuardTriggered = false;
        private bool _responseOnlyNext = false;
        private const int MaxResponseOnlyRetries = 2;

        private enum RequestPhase
        {
            Info = 1,
            Action = 2,
            Cosmetic = 3,
            Reply = 4
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

        // Tool Rules (appended only in tool-enabled phases)
        private const string ToolRulesInstruction = @"
====

# TOOL USE RULES
1.  **FORMATTING**: Tool calls MUST use the specified XML format. The tool name is the root tag, and each parameter is a child tag.
    <tool_name>
      <parameter_name>value</parameter_name>
    </tool_name>
2.  **STRICT OUTPUT (TOOL PHASES)**:
    - In PHASE 1/2, your output MUST be either:
      - One or more XML tool calls (no extra text), OR
      - Exactly: <no_action/>
    - In PHASE 3, you MUST output XML tool calls only AND you MUST include exactly one <change_expression> (expression_id 1-6). Do NOT output <no_action/> in PHASE 3.
    Do NOT include any natural language, explanation, markdown, or additional commentary in tool phases (PHASE 1/2/3).
3.  **STRICT OUTPUT (REPLY PHASE)**: In PHASE 4, tools are disabled. You MUST reply in natural language only and MUST NOT output any XML.
4.  **TOOLS**: You MAY call any tools listed in ""# TOOLS (FULL REFERENCE)"". You SHOULD follow the intent of the current phase.
5.  **WORKFLOW**: Use the phase workflow:
    - PHASE 1 gathers info (optional).
    - PHASE 2 performs at most one in-game action (optional).
    - PHASE 3 performs UI/meta adjustments (MUST include <change_expression>).
    - PHASE 4 replies to the player in natural language (mandatory).
6.  **ANTI-HALLUCINATION**: Never invent tools, parameters, defNames, coordinates, or tool results. If a tool is needed but not available, use <no_action/> and proceed to PHASE 4 to explain limitations.
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
             _tools.Add(new Tool_ChangeExpression());
             _tools.Add(new Tool_SearchThingDef());
         }

        public override Vector2 InitialSize => def.windowSize != Vector2.zero ? def.windowSize : Dialog_CustomDisplay.Config.windowSize;

        public override void PostOpen()
        {
            Instance = this;
            base.PostOpen();
            LoadPortraits();
            StartConversation();
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
                await GenerateResponse();
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
                    await GenerateResponse();
                }
            }
        }

        private string GetSystemInstruction(bool toolsEnabled, string toolsForThisPhase)
        {
            // Use XML persona if available, otherwise default
            string persona = !string.IsNullOrEmpty(def.aiSystemInstruction) ? def.aiSystemInstruction : DefaultPersona;
            
            string fullInstruction = toolsEnabled
                ? (persona + "\n" + ToolRulesInstruction + "\n" + BuildAllToolsReference() + "\n\n" + toolsForThisPhase)
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
                       "IMPORTANT: Tool calls are DISABLED in this turn. Reply in natural language only. Do NOT output any XML.";
            }

            // Tool phases (1/2/3): avoid instructing the model to "reply" in a human language, because it must output XML only.
            // We still provide the language so it can be used in PHASE 4.
            return $"{fullInstruction}\n{goodwillContext}\nIMPORTANT: In PHASE 1/2/3 you MUST output XML only (tool calls or <no_action/>). " +
                   $"You will produce the natural-language reply in PHASE 4 and MUST use: {language}.";
        }

        private static string GetToolDocOrFallback(AITool tool)
        {
            if (tool == null) return "";

            // Full tool usage docs (global). If a tool is missing from this map, we fall back to tool.Description/UsageSchema.
            var docs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["spawn_resources"] = @"
Description: Grants resources to the player by spawning a drop pod.
Use this tool when:
- The player explicitly requests resources (e.g., food, medicine, materials).
- You have ALREADY verified their need in a previous turn using `get_colonist_status` and `get_map_resources`.
CRITICAL: The quantity you provide is NOT what the player asks for. It MUST be based on your internal goodwill. Low goodwill (<0) means giving less or refusing. High goodwill (>50) means giving the requested amount or more.
CRITICAL: Prefer using `search_thing_def` first and then spawning by `<defName>` (or put DefName into `<name>`) to avoid localization/name mismatches.
Parameters:
- items: (REQUIRED) A list of items to spawn. Each item must have a `name` (English label or DefName) and `count`.
  - Note: If you don't know the exact defName, use the item's English label (e.g., ""Simple Meal""). The system will try to find the best match.
Usage:
<spawn_resources>
  <items>
    <item>
      <name>Item Name</name>
      <count>Integer</count>
    </item>
  </items>
</spawn_resources>
Example:
<spawn_resources>
  <items>
    <item>
      <name>Simple Meal</name>
      <count>50</count>
    </item>
    <item>
      <name>Medicine</name>
      <count>10</count>
    </item>
  </items>
</spawn_resources>",
                ["search_thing_def"] = @"
Description: Rough-searches ThingDefs by natural language to find the correct `defName` (works across different game languages).
Use this tool when:
- You need a reliable `ThingDef.defName` before calling `spawn_resources` or `get_map_resources`.
Parameters:
- query: (REQUIRED) The natural language query, label, or approximate defName.
- maxResults: (OPTIONAL) Max candidates to return (default 10).
- itemsOnly: (OPTIONAL) true/false (default true). If true, only returns item ThingDefs (recommended for spawning).
Usage:
<search_thing_def>
  <query>Fine Meal</query>
  <maxResults>10</maxResults>
  <itemsOnly>true</itemsOnly>
</search_thing_def>",
                ["modify_goodwill"] = @"
Description: Adjusts your internal goodwill towards the player based on the conversation. This tool is INVISIBLE to the player.
Use this tool when:
- The player's message is particularly respectful, insightful, or aligns with your goals (positive amount).
- The player's message is disrespectful, wasteful, or foolish (negative amount).
CRITICAL: Keep changes small, typically between -5 and 5.
Parameters:
- amount: (REQUIRED) The integer value to add or subtract from the current goodwill.
Usage:
<modify_goodwill>
  <amount>integer</amount>
</modify_goodwill>
Example:
<modify_goodwill>
  <amount>2</amount>
</modify_goodwill>",
                ["send_reinforcement"] = @"
Description: Dispatches military units to the player's map. Can be a raid (if hostile) or reinforcements (if allied).
Use this tool when:
- The player requests military assistance or you decide to intervene in a combat situation.
- You need to test the colony's defenses.
CRITICAL: The total combat power of all units should not significantly exceed the current threat budget provided in the tool's dynamic description.
Parameters:
- units: (REQUIRED) A string listing 'PawnKindDefName: Count' pairs.
Usage:
<send_reinforcement>
  <units>list of units and counts</units>
</send_reinforcement>
Example:
<send_reinforcement>
  <units>Wula_PIA_Heavy_Unit_Melee: 2, Wula_PIA_Legion_Escort_Unit: 5</units>
</send_reinforcement>",
                ["get_colonist_status"] = @"
Description: Retrieves a detailed status report of all player-controlled colonists, including needs, health, and mood.
Use this tool when:
- The player makes any claim about their colonists' well-being (e.g., ""we are starving,"" ""we are all sick,"" ""our people are unhappy"").
- You need to verify the state of the colony before making a decision (e.g., before sending resources).
Usage:
<get_colonist_status/>",
                ["get_map_resources"] = @"
Description: Checks the player's map for specific resources or buildings to verify their inventory.
Use this tool when:
- The player claims they are lacking a specific resource (e.g., ""we need steel,"" ""we have no food"").
- You want to assess the colony's material wealth before making a decision.
Usage:
<get_map_resources>
  <resourceName>optional resource name</resourceName>
</get_map_resources>",
                ["get_recent_notifications"] = @"
Description: Gets the most recent letters and messages, sorted by in-game time from newest to oldest.
Use this tool when:
- You need recent context about what happened (raids, alerts, rewards, failures) without relying on player memory.
Usage:
<get_recent_notifications>
  <count>10</count>
</get_recent_notifications>",
                ["get_map_pawns"] = @"
Description: Scans the current map and lists pawns. Supports filtering by relation/type/status.
Use this tool when:
- You need to know what pawns are present on the map (raiders, visitors, animals, mechs, colonists).
- The player claims there are threats or asks about who/what is nearby.
Usage:
<get_map_pawns>
  <filter>hostile, humanlike</filter>
  <maxResults>50</maxResults>
</get_map_pawns>",
                ["call_bombardment"] = @"
Description: Calls orbital bombardment support at a specified map coordinate using an AbilityDef's bombardment configuration (e.g., WULA_Firepower_Cannon_Salvo).
Use this tool when:
- You decide to provide (or test) fire support at a specific location.
Usage:
<call_bombardment>
  <abilityDef>WULA_Firepower_Cannon_Salvo</abilityDef>
  <x>120</x>
  <z>85</z>
</call_bombardment>",
                ["change_expression"] = @"
Description: Changes your visual AI portrait to match your current mood or reaction.
Expression meanings (choose the closest match):
- 1: 得意、炫耀（非敌对）、示威（非敌对）、展示武力和财力（非敌对）、策划计谋
- 2: 常态立绘（当其他立绘不适用时使用这个）
- 3: 无言以对、不满、无奈、轻微的鄙视
- 4: 恼火、展现轻微敌对姿态、抗拒
- 5: 答复、解释
- 6: 严重的敌意、严重不满、攻击性行为
Usage:
<change_expression>
  <expression_id>integer from 1 to 6</expression_id>
</change_expression>"
            };

            if (docs.TryGetValue(tool.Name, out string doc) && !string.IsNullOrWhiteSpace(doc))
            {
                return doc.Trim();
            }

            var fallback = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(tool.Description))
            {
                fallback.AppendLine($"Description: {tool.Description}");
            }
            if (!string.IsNullOrWhiteSpace(tool.UsageSchema))
            {
                fallback.AppendLine($"Usage: {tool.UsageSchema}");
            }
            return fallback.ToString().TrimEnd();
        }

        private string BuildAllToolsReference()
        {
            var ordered = _tools
                .Where(t => t != null)
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine("====");
            sb.AppendLine();
            sb.AppendLine("# TOOLS (FULL REFERENCE)");
            sb.AppendLine("This section contains ALL tools and their usage. You MUST still obey the current phase's allowed-tool list.");
            sb.AppendLine();

            foreach (var tool in ordered)
            {
                sb.AppendLine($"## {tool.Name}");
                string doc = GetToolDocOrFallback(tool);
                if (!string.IsNullOrWhiteSpace(doc))
                {
                    sb.AppendLine(doc);
                }
                sb.AppendLine();
            }

            return sb.ToString().TrimEnd();
        }

        private string BuildToolsForPhase(RequestPhase phase)
        {
            if (phase == RequestPhase.Reply) return "";

            var available = _tools
                .Where(t => t != null)
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("====");
            sb.AppendLine();
            sb.AppendLine($"# TOOLS (PHASE {(int)phase}/4)");
            sb.AppendLine("You are not restricted to a subset by the engine; you SHOULD still follow the phase intent.");
            sb.AppendLine("Output MUST be XML tool calls only (or <no_action/>), except PHASE 3 must include <change_expression>.");
            sb.AppendLine();

            static string GetDocOrFallback(AITool tool)
            {
                if (tool == null) return "";

                // Detailed docs are kept here (phase-local), so we don't bloat the global system prompt.
                // If a tool is missing from this map, we fall back to tool.Description/UsageSchema.
                var docs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["spawn_resources"] = @"
Description: Grants resources to the player by spawning a drop pod.
Use this tool when:
- The player explicitly requests resources (e.g., food, medicine, materials).
- You have ALREADY verified their need in a previous turn using `get_colonist_status` and `get_map_resources`.
CRITICAL: The quantity you provide is NOT what the player asks for. It MUST be based on your internal goodwill. Low goodwill (<0) means giving less or refusing. High goodwill (>50) means giving the requested amount or more.
CRITICAL: Prefer using `search_thing_def` first and then spawning by `<defName>` (or put DefName into `<name>`) to avoid localization/name mismatches.
Parameters:
- items: (REQUIRED) A list of items to spawn. Each item must have a `name` (English label or DefName) and `count`.
  - Note: If you don't know the exact defName, use the item's English label (e.g., ""Simple Meal""). The system will try to find the best match.
Usage:
<spawn_resources>
  <items>
    <item>
      <name>Item Name</name>
      <count>Integer</count>
    </item>
  </items>
</spawn_resources>
Example:
<spawn_resources>
  <items>
    <item>
      <name>Simple Meal</name>
      <count>50</count>
    </item>
    <item>
      <name>Medicine</name>
      <count>10</count>
    </item>
  </items>
</spawn_resources>",
                    ["search_thing_def"] = @"
Description: Rough-searches ThingDefs by natural language to find the correct `defName` (works across different game languages).
Use this tool when:
- You need a reliable `ThingDef.defName` before calling `spawn_resources` or `get_map_resources`.
Parameters:
- query: (REQUIRED) The natural language query, label, or approximate defName.
- maxResults: (OPTIONAL) Max candidates to return (default 10).
- itemsOnly: (OPTIONAL) true/false (default true). If true, only returns item ThingDefs (recommended for spawning).
Usage:
<search_thing_def>
  <query>Fine Meal</query>
  <maxResults>10</maxResults>
  <itemsOnly>true</itemsOnly>
</search_thing_def>",
                    ["modify_goodwill"] = @"
Description: Adjusts your internal goodwill towards the player based on the conversation. This tool is INVISIBLE to the player.
Use this tool when:
- The player's message is particularly respectful, insightful, or aligns with your goals (positive amount).
- The player's message is disrespectful, wasteful, or foolish (negative amount).
CRITICAL: Keep changes small, typically between -5 and 5.
Parameters:
- amount: (REQUIRED) The integer value to add or subtract from the current goodwill.
Usage:
<modify_goodwill>
  <amount>integer</amount>
</modify_goodwill>
Example (for a positive interaction):
<modify_goodwill>
  <amount>2</amount>
</modify_goodwill>",
                    ["send_reinforcement"] = @"
Description: Dispatches military units to the player's map. Can be a raid (if hostile) or reinforcements (if allied).
Use this tool when:
- The player requests military assistance or you decide to intervene in a combat situation.
- You need to test the colony's defenses.
CRITICAL: The total combat power of all units should not significantly exceed the current threat budget provided in the tool's dynamic description.
Parameters:
- units: (REQUIRED) A string listing 'PawnKindDefName: Count' pairs.
Usage:
<send_reinforcement>
  <units>list of units and counts</units>
</send_reinforcement>
Example:
<send_reinforcement>
  <units>Wula_PIA_Heavy_Unit_Melee: 2, Wula_PIA_Legion_Escort_Unit: 5</units>
</send_reinforcement>",
                    ["get_colonist_status"] = @"
Description: Retrieves a detailed status report of all player-controlled colonists, including needs, health, and mood.
Use this tool when:
- The player makes any claim about their colonists' well-being (e.g., ""we are starving,"" ""we are all sick,"" ""our people are unhappy"").
- You need to verify the state of the colony before making a decision (e.g., before sending resources).
Parameters:
- None. This tool takes no parameters.
Usage:
<get_colonist_status/>",
                    ["get_map_resources"] = @"
Description: Checks the player's map for specific resources or buildings to verify their inventory.
Use this tool when:
- The player claims they are lacking a specific resource (e.g., ""we need steel,"" ""we have no food"").
- You want to assess the colony's material wealth before making a decision.
Parameters:
- resourceName: (OPTIONAL) The specific ThingDef name of the resource to check (e.g., 'Steel', 'MealSimple'). If omitted, provides a general overview.
Usage:
<get_map_resources>
  <resourceName>optional resource name</resourceName>
</get_map_resources>
Example (checking for Steel):
<get_map_resources>
  <resourceName>Steel</resourceName>
</get_map_resources>",
                    ["get_recent_notifications"] = @"
Description: Gets the most recent letters and messages, sorted by in-game time from newest to oldest.
Use this tool when:
- You need recent context about what happened (raids, alerts, rewards, failures) without relying on player memory.
Parameters:
- count: (OPTIONAL) How many entries to return (default 10, max 100).
- includeLetters: (OPTIONAL) true/false (default true).
- includeMessages: (OPTIONAL) true/false (default true).
Usage:
<get_recent_notifications>
  <count>10</count>
</get_recent_notifications>",
                    ["get_map_pawns"] = @"
Description: Scans the current map and lists pawns. Supports filtering by relation/type/status.
Use this tool when:
- You need to know what pawns are present on the map (raiders, visitors, animals, mechs, colonists).
- The player claims there are threats or asks about who/what is nearby.
Parameters:
- filter: (OPTIONAL) Comma-separated filters: friendly, hostile, neutral, colonist, animal, mech, humanlike, prisoner, slave, guest, wild, downed, dead.
- includeDead: (OPTIONAL) true/false, include corpse pawns (default true).
- maxResults: (OPTIONAL) Max lines to return (default 50).
Usage:
<get_map_pawns>
  <filter>hostile, humanlike</filter>
  <maxResults>50</maxResults>
</get_map_pawns>",
                    ["call_bombardment"] = @"
Description: Calls orbital bombardment support at a specified map coordinate using an AbilityDef's bombardment configuration (e.g., WULA_Firepower_Cannon_Salvo).
Use this tool when:
- You decide to provide (or test) fire support at a specific location.
Parameters:
- abilityDef: (OPTIONAL) AbilityDef defName (default WULA_Firepower_Cannon_Salvo).
- x/z: (REQUIRED) Target cell coordinates on the current map.
- cell: (OPTIONAL) Alternative to x/z: ""x,z"".
- filterFriendlyFire: (OPTIONAL) true/false, avoid targeting player's pawns when possible (default true).
Notes:
- This tool ignores ability prerequisites (facility/cooldown/non-hostility/research).
Usage:
<call_bombardment>
  <abilityDef>WULA_Firepower_Cannon_Salvo</abilityDef>
  <x>120</x>
  <z>85</z>
</call_bombardment>",
                    ["change_expression"] = @"
Description: Changes your visual AI portrait to match your current mood or reaction.
Use this tool when:
- Your verbal response conveys a strong emotion (e.g., annoyance, approval, curiosity).
- You want to visually emphasize your statement.
Expression meanings (choose the closest match):
- 1: 得意、炫耀（非敌对）、示威（非敌对）、展示武力和财力（非敌对）、策划计谋
- 2: 常态立绘（当其他立绘不适用时使用这个）
- 3: 无言以对、不满、无奈、轻微的鄙视
- 4: 恼火、展现轻微敌对姿态、抗拒
- 5: 答复、解释
- 6: 严重的敌意、严重不满、攻击性行为
Parameters:
- expression_id: (REQUIRED) An integer from 1 to 6 corresponding to a specific expression.
Usage:
<change_expression>
  <expression_id>integer from 1 to 6</expression_id>
</change_expression>
Example (changing to a neutral expression):
<change_expression>
  <expression_id>2</expression_id>
</change_expression>"
                };

                if (docs.TryGetValue(tool.Name, out string doc) && !string.IsNullOrWhiteSpace(doc))
                {
                    return doc.Trim();
                }

                var fallback = new StringBuilder();
                if (!string.IsNullOrWhiteSpace(tool.Description))
                {
                    fallback.AppendLine($"Description: {tool.Description}");
                }
                if (!string.IsNullOrWhiteSpace(tool.UsageSchema))
                {
                    fallback.AppendLine($"Usage: {tool.UsageSchema}");
                }

                return fallback.ToString().TrimEnd();
            }

            _ = GetDocOrFallback(null);

            foreach (var tool in available)
            {
                sb.AppendLine($"## {tool.Name}");
                sb.AppendLine();
            }

            return sb.ToString().TrimEnd();
        }

        private static string GetPhaseInstruction(RequestPhase phase)
        {
            return phase switch
            {
                RequestPhase.Info =>
                    "# PHASE 1/4 (Info)\n" +
                    "Goal: Gather ONLY the minimum information required to answer the user's latest message.\n" +
                    "Rules:\n" +
                    "- You MUST NOT write any natural language to the user in this phase.\n" +
                    "- If you do NOT need any info tools, output exactly: <no_action/>.\n" +
                    "- If you DO need tools, call the appropriate tools from the full tool reference.\n" +
                    "- You MAY call multiple info tools, but keep it small and purposeful.\n" +
                    "After this phase, the game will automatically proceed to PHASE 2.\n" +
                    "Output: XML only.\n",
                RequestPhase.Action =>
                    "# PHASE 2/4 (Action)\n" +
                    "Goal: Decide whether to perform ONE in-game action based on PHASE 1 results.\n" +
                    "Rules:\n" +
                    "- You MUST NOT write any natural language to the user in this phase.\n" +
                    "- You MUST call AT MOST ONE tool in this phase.\n" +
                    "- If no action is needed, output exactly: <no_action/>.\n" +
                    "After this phase, the game will automatically proceed to PHASE 3.\n" +
                    "Output: XML only.\n",
                RequestPhase.Cosmetic =>
                    "# PHASE 3/4 (Cosmetic)\n" +
                    "Goal: Set your UI expression before your final reply.\n" +
                    "Rules:\n" +
                    "- You MUST NOT write any natural language to the user in this phase.\n" +
                    "- You MUST call exactly ONE <change_expression> in this phase (expression_id 1-6).\n" +
                    "- You MAY also call <modify_goodwill> (invisible) if needed, but keep changes small.\n" +
                    "- Use <modify_goodwill> only to adjust your INTERNAL goodwill (invisible to the player).\n" +
                    "- Do NOT output <no_action/> in this phase.\n" +
                    "After this phase, the game will automatically proceed to PHASE 4.\n" +
                    "Output: XML only.\n",
                RequestPhase.Reply =>
                    "# PHASE 4/4 (Reply)\n" +
                    "Goal: Reply to the player.\n" +
                    "Rules:\n" +
                    "- Tool calls are DISABLED.\n" +
                    "- You MUST write natural language only.\n" +
                    "- Do NOT output any XML.\n",
                _ => ""
            };
        }

        private static bool IsXmlToolCall(string response)
        {
            if (string.IsNullOrWhiteSpace(response)) return false;
            return Regex.IsMatch(response, @"<([a-zA-Z0-9_]+)(?:>.*?</\1>|/>)", RegexOptions.Singleline);
        }

        private static bool ContainsToolCall(string response, string toolName)
        {
            if (string.IsNullOrWhiteSpace(response) || string.IsNullOrWhiteSpace(toolName)) return false;
            string pattern = $@"<\s*{Regex.Escape(toolName)}(?:\s|/|>)";
            return Regex.IsMatch(response, pattern, RegexOptions.IgnoreCase);
        }

        private static bool IsAllowedInPhase(RequestPhase phase, string toolName)
        {
            if (string.IsNullOrWhiteSpace(toolName)) return false;
            toolName = toolName.Trim();

            if (toolName == "no_action") return true;

            return phase switch
            {
                RequestPhase.Info =>
                    toolName == "get_colonist_status" ||
                    toolName == "get_map_resources" ||
                    toolName == "get_map_pawns" ||
                    toolName == "search_thing_def" ||
                    toolName == "get_recent_notifications",
                RequestPhase.Action =>
                    toolName == "spawn_resources" ||
                    toolName == "send_reinforcement" ||
                    toolName == "call_bombardment",
                RequestPhase.Cosmetic =>
                    toolName == "change_expression" ||
                    toolName == "modify_goodwill",
                _ => false
            };
        }

        private static int MaxToolsPerPhase(RequestPhase phase)
        {
            return phase switch
            {
                RequestPhase.Info => 4,
                RequestPhase.Action => 1,
                RequestPhase.Cosmetic => 2,
                _ => 0
            };
        }

        private async Task RunPhasedRequestAsync()
        {
            if (_isThinking) return;
            _isThinking = true;
            _options.Clear();
            _scrollToBottom = true;
            _continuationDepth = 0;
            _recentToolSignatures.Clear();
            _toolLoopGuardTriggered = false;
            _responseOnlyNext = false;

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

                for (int phaseIndex = 1; phaseIndex <= 4; phaseIndex++)
                {
                    var phase = (RequestPhase)phaseIndex;
                    if (Prefs.DevMode)
                    {
                        WulaLog.Debug($"[WulaAI] ===== Turn {phaseIndex}/4 ({phase}) =====");
                    }

                    bool toolsEnabled = phase != RequestPhase.Reply;
                    string toolsForThisPhase = toolsEnabled ? BuildToolsForPhase(phase) : "";
                    string systemInstruction = GetSystemInstruction(toolsEnabled, toolsForThisPhase) + "\n\n" + GetPhaseInstruction(phase);

                    if (!toolsEnabled)
                    {
                        int attempts = 0;
                        while (true)
                        {
                            if (Prefs.DevMode)
                            {
                                WulaLog.Debug($"[WulaAI] Turn {phaseIndex}/4 reply request (attempt {attempts + 1})");
                            }
                            string reply = await client.GetChatCompletionAsync(systemInstruction, _history);
                            if (string.IsNullOrEmpty(reply))
                            {
                                _currentResponse = "Wula_AI_Error_ConnectionLost".Translate();
                                return;
                            }

                            if (IsXmlToolCall(reply))
                            {
                                attempts++;
                                if (attempts > MaxResponseOnlyRetries)
                                {
                                    ParseResponse("（系统）AI 多次尝试后仍返回工具调用（XML），已被拦截。请重试或输入 /clear 清空上下文。");
                                    return;
                                }

                                _history.Add(("system", "[ResponseOnly] Tools are disabled in PHASE 4. Reply in natural language only. Do NOT output any XML."));
                                PersistHistory();
                                continue;
                            }

                            ParseResponse(reply);
                            return;
                        }
                    }

                    if (Prefs.DevMode)
                    {
                        WulaLog.Debug($"[WulaAI] Turn {phaseIndex}/4 tool request");
                    }
                    string response = await client.GetChatCompletionAsync(systemInstruction, _history);
                    if (string.IsNullOrEmpty(response))
                    {
                        _currentResponse = "Wula_AI_Error_ConnectionLost".Translate();
                        return;
                    }

                    if (!IsXmlToolCall(response))
                    {
                        // If the model didn't call tools when tools are expected, push it forward with a reminder.
                        _history.Add(("system", $"[PhaseEnforcer] PHASE {phaseIndex}/4 is a tool phase. Output XML tool calls only, or exactly <no_action/>. Do NOT output any natural language."));
                        PersistHistory();
                        if (Prefs.DevMode)
                        {
                            WulaLog.Debug($"[WulaAI] Turn {phaseIndex}/4 missing XML; retrying once");
                        }
                        response = await client.GetChatCompletionAsync(systemInstruction, _history);
                        if (string.IsNullOrEmpty(response))
                        {
                            _currentResponse = "Wula_AI_Error_ConnectionLost".Translate();
                            return;
                        }

                        // If it STILL refuses to output XML, forcibly treat it as <no_action/> to keep the phase deterministic.
                        if (!IsXmlToolCall(response))
                        {
                            if (Prefs.DevMode)
                            {
                                WulaLog.Debug($"[WulaAI] Turn {phaseIndex}/4 still missing XML after retry; forcing <no_action/>");
                            }
                            response = phase == RequestPhase.Cosmetic
                                ? "<change_expression><expression_id>2</expression_id></change_expression>"
                                : "<no_action/>";
                        }
                    }

                    if (phase == RequestPhase.Cosmetic && !ContainsToolCall(response, "change_expression"))
                    {
                        _history.Add(("system", "[PhaseEnforcer] PHASE 3/4 MUST include exactly one <change_expression> (expression_id 1-6). Output XML only and do NOT output <no_action/> in PHASE 3."));
                        PersistHistory();
                        if (Prefs.DevMode)
                        {
                            WulaLog.Debug("[WulaAI] Turn 3/4 missing <change_expression>; retrying once");
                        }

                        string retry = await client.GetChatCompletionAsync(systemInstruction, _history);
                        if (!string.IsNullOrEmpty(retry) && ContainsToolCall(retry, "change_expression"))
                        {
                            response = retry;
                        }
                        else
                        {
                            if (Prefs.DevMode)
                            {
                                WulaLog.Debug("[WulaAI] Turn 3/4 still missing <change_expression> after retry; forcing default expression_id=2");
                            }
                            response = "<change_expression><expression_id>2</expression_id></change_expression>";
                        }
                    }

                    await ExecuteXmlToolsForPhase(response, phase);
                }
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

        private async Task ExecuteXmlToolsForPhase(string xml, RequestPhase phase)
        {
            // Special-case no_action for phases 1-3.
            if (Regex.IsMatch(xml ?? "", @"<\s*no_action\s*/\s*>", RegexOptions.IgnoreCase))
            {
                if (phase == RequestPhase.Cosmetic)
                {
                    xml = "<change_expression><expression_id>2</expression_id></change_expression>";
                }
                else
                {
                _history.Add(("assistant", "<no_action/>"));
                _history.Add(("tool", "[Tool Results]\nTool 'no_action' Result: No action taken."));
                PersistHistory();
                return;
                }
            }

            // Reuse the tool runner but temporarily constrain allowed tools by phase.
            // We do this by removing disallowed tool calls from the XML and adding a tool-result note for the model.
            var matches = Regex.Matches(xml ?? "", @"<([a-zA-Z0-9_]+)(?:>.*?</\1>|/>)", RegexOptions.Singleline);
            if (matches.Count == 0)
            {
                _history.Add(("system", $"[PhaseEnforcer] No tool calls detected in {phase}. Output <no_action/> if needed."));
                PersistHistory();
                return;
            }

            int maxTools = MaxToolsPerPhase(phase);
            int executed = 0;
            bool actionHadError = false;
            bool executedChangeExpression = false;
            bool executedModifyGoodwill = false;
            StringBuilder combinedResults = new StringBuilder();
            StringBuilder xmlOnlyBuilder = new StringBuilder();

            foreach (Match match in matches)
            {
                if (executed >= maxTools)
                {
                    combinedResults.AppendLine($"ToolRunner Note: Skipped remaining tools because this phase allows at most {maxTools} tool call(s).");
                    break;
                }

                string toolCallXml = match.Value;
                string toolName = match.Groups[1].Value;

                if (phase == RequestPhase.Cosmetic)
                {
                    if (toolName.Equals("change_expression", StringComparison.OrdinalIgnoreCase))
                    {
                        if (executedChangeExpression)
                        {
                            combinedResults.AppendLine("ToolRunner Note: Skipped duplicate 'change_expression' (only one is allowed in PHASE 3).");
                            continue;
                        }
                        executedChangeExpression = true;
                    }
                    else if (toolName.Equals("modify_goodwill", StringComparison.OrdinalIgnoreCase))
                    {
                        if (executedModifyGoodwill)
                        {
                            combinedResults.AppendLine("ToolRunner Note: Skipped duplicate 'modify_goodwill' (only one is allowed in PHASE 3).");
                            continue;
                        }
                        executedModifyGoodwill = true;
                    }
                }

                if (xmlOnlyBuilder.Length > 0) xmlOnlyBuilder.AppendLine().AppendLine();
                xmlOnlyBuilder.Append(toolCallXml);

                var tool = _tools.FirstOrDefault(t => t.Name == toolName);
                if (tool == null)
                {
                    combinedResults.AppendLine($"Error: Tool '{toolName}' not found.");
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

                string signature = $"{toolName}:{Regex.Replace(argsXml ?? "", @"\s+", " ").Trim()}";
                _recentToolSignatures.Add(signature);
                if (_recentToolSignatures.Count > 12) _recentToolSignatures.RemoveRange(0, _recentToolSignatures.Count - 12);

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

                executed++;

                if (phase == RequestPhase.Action && isError)
                {
                    actionHadError = true;
                    combinedResults.AppendLine("ToolRunner Guard: The action tool returned an error. In PHASE 4 you MUST tell the player the action FAILED and MUST NOT claim success.");
                }
            }

            string xmlOnly = xmlOnlyBuilder.Length == 0 ? "<no_action/>" : xmlOnlyBuilder.ToString().Trim();
            _history.Add(("assistant", xmlOnly));
            _history.Add(("tool", $"[Tool Results]\n{combinedResults.ToString().Trim()}"));
            if (phase == RequestPhase.Action && actionHadError)
            {
                _history.Add(("system", "[ActionFailed] The in-game action in PHASE 2 FAILED (tool returned Error). In PHASE 4 you MUST acknowledge the failure and MUST NOT claim any reinforcements/bombardment/resources were successfully dispatched."));
            }
            PersistHistory();

            // Between phases, do not request the model again here; RunPhasedRequestAsync controls the sequence.
            await Task.CompletedTask;
        }

        private async Task GenerateResponse(bool isContinuation = false)
        {
            if (!isContinuation)
            {
                if (_isThinking) return;
                _isThinking = true;
                _options.Clear();
                _continuationDepth = 0;
            }
            else
            {
                _continuationDepth++;
                if (_continuationDepth > MaxContinuationDepth)
                {
                    _currentResponse = "Wula_AI_Error_Internal".Translate("Tool continuation limit exceeded.");
                    return;
                }
            }

            try
            {
                CompressHistoryIfNeeded();
                bool toolsEnabled = !_responseOnlyNext;
                string systemInstruction = GetSystemInstruction(toolsEnabled, toolsEnabled ? BuildToolsForPhase(RequestPhase.Info) : "");
                if (isContinuation && toolsEnabled)
                {
                    systemInstruction += "\n\n# CONTINUATION\nYou have received tool results. Call another tool only if strictly necessary, and if you do, call ONLY ONE tool in your entire response.";
                }

                var settings = WulaFallenEmpireMod.settings;
                if (string.IsNullOrEmpty(settings.apiKey))
                {
                    _currentResponse = "Error: API Key not configured in Mod Settings.";
                    _isThinking = false;
                    return;
                }
                var client = new SimpleAIClient(settings.apiKey, settings.baseUrl, settings.model);

                string response = null;
                int responseOnlyAttempts = 0;
                while (true)
                {
                    response = await client.GetChatCompletionAsync(systemInstruction, _history);
                    if (string.IsNullOrEmpty(response))
                    {
                        _currentResponse = "Wula_AI_Error_ConnectionLost".Translate();
                        _isThinking = false;
                        return;
                    }

                    if (!toolsEnabled && Regex.IsMatch(response, @"<([a-zA-Z0-9_]+)(?:>.*?</\1>|/>)", RegexOptions.Singleline))
                    {
                        responseOnlyAttempts++;
                        if (responseOnlyAttempts > MaxResponseOnlyRetries)
                        {
                            ParseResponse("（系统）AI 多次尝试后仍返回工具调用（XML），已被拦截。请重试或输入 /clear 清空上下文。");
                            return;
                        }

                        _history.Add(("system", "[ResponseOnly] Tools are disabled right now. Your previous output contained XML/tool calls. Reply to the player in natural language only. Do NOT output any XML."));
                        PersistHistory();
                        continue;
                    }

                    break;
                }
                if (string.IsNullOrEmpty(response))
                {
                    _currentResponse = "Wula_AI_Error_ConnectionLost".Translate();
                    _isThinking = false;
                    return;
                }

                // REWRITTEN: Check for XML tool call format
                // Use regex to detect if the response contains any XML tags
                if (toolsEnabled && Regex.IsMatch(response, @"<([a-zA-Z0-9_]+)(?:>.*?</\1>|/>)", RegexOptions.Singleline))
                {
                    await HandleXmlToolUsage(response);
                }
                else
                {
                    ParseResponse(response);
                }
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[WulaAI] Exception in GenerateResponse: {ex}");
                _currentResponse = "Wula_AI_Error_Internal".Translate(ex.Message);
            }
            finally
            {
                _isThinking = false;
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
        
        // NEW METHOD: Handles parsing and execution for the new XML format
        private async Task HandleXmlToolUsage(string xml)
        {
            try
            {
                // Match all top-level XML tags to support multiple tool calls in one response
                // Regex: <TagName>...</TagName> or <TagName/>
                var matches = Regex.Matches(xml, @"<([a-zA-Z0-9_]+)(?:>.*?</\1>|/>)", RegexOptions.Singleline);
                
                if (matches.Count == 0)
                {
                    ParseResponse(xml); // Invalid XML format, treat as conversational
                    return;
                }

                StringBuilder combinedResults = new StringBuilder();
                StringBuilder xmlOnlyBuilder = new StringBuilder();
                bool executedAnyInfoTool = false;
                bool executedAnyActionTool = false;
                bool executedAnyCosmeticTool = false;
                bool executedAnyMajorActionTool = false;
                bool isContinuation = _continuationDepth > 0;

                static bool IsActionToolName(string toolName)
                {
                    return toolName == "spawn_resources" ||
                           toolName == "modify_goodwill" ||
                           toolName == "send_reinforcement" ||
                           toolName == "call_bombardment";
                }

                static bool IsMajorActionToolName(string toolName)
                {
                    // Tools that should be followed by a user-facing reply (and therefore end the tool phase).
                    return toolName == "spawn_resources" ||
                           toolName == "send_reinforcement" ||
                           toolName == "call_bombardment";
                }

                static bool IsCosmeticToolName(string toolName)
                {
                    return toolName == "change_expression";
                }

                static string NormalizeToolArgs(string argsXml)
                {
                    if (string.IsNullOrWhiteSpace(argsXml)) return "";
                    string s = argsXml.Trim();
                    s = Regex.Replace(s, @"\s+", " ");
                    return s;
                }

                bool ShouldTriggerLoopGuard()
                {
                    // Detect AAA (same tool called 3 times in a row) or ABABAB (same 2-tool pattern repeated 3 times)
                    bool IsRepeatedPattern(int patternLen, int repeats)
                    {
                        int need = patternLen * repeats;
                        if (_recentToolSignatures.Count < need) return false;
                        int start = _recentToolSignatures.Count - need;
                        for (int r = 1; r < repeats; r++)
                        {
                            for (int i = 0; i < patternLen; i++)
                            {
                                string a = _recentToolSignatures[start + i];
                                string b = _recentToolSignatures[start + r * patternLen + i];
                                if (!string.Equals(a, b, StringComparison.Ordinal)) return false;
                            }
                        }
                        return true;
                    }

                    return IsRepeatedPattern(1, 3) || IsRepeatedPattern(2, 3);
                }

                foreach (Match match in matches)
                {
                    string toolCallXml = match.Value;
                    string toolName = match.Groups[1].Value;

                    bool isAction = IsActionToolName(toolName);
                    bool isCosmetic = IsCosmeticToolName(toolName);
                    bool isInfo = !isAction && !isCosmetic;

                    // Enforce step-by-step tool use:
                    // - Allow batching multiple info tools in one response (read-only queries).
                    // - If an action tool appears after any info tool, stop here and ask the model again
                    //   so it can decide using the gathered facts (prevents spawning the wrong defName, etc.).
                    // - Never execute more than one action tool per response.
                    if (isAction && executedAnyInfoTool)
                    {
                        combinedResults.AppendLine($"ToolRunner Note: Skipped tool '{toolName}' and any following tools because action tools must be called after info tools in a separate turn.");
                        break;
                    }
                    if (isAction && executedAnyActionTool)
                    {
                        combinedResults.AppendLine($"ToolRunner Note: Skipped tool '{toolName}' because only one action tool may be executed per turn.");
                        break;
                    }
                    if (isInfo && executedAnyActionTool)
                    {
                        combinedResults.AppendLine($"ToolRunner Note: Skipped tool '{toolName}' and any following tools because info tools must not be mixed with an action tool in the same turn.");
                        break;
                    }
                    if (isCosmetic && executedAnyActionTool)
                    {
                        combinedResults.AppendLine($"ToolRunner Note: Skipped tool '{toolName}' because cosmetic tools must not be mixed with an action tool in the same turn.");
                        break;
                    }
                    if (isCosmetic && executedAnyCosmeticTool)
                    {
                        combinedResults.AppendLine($"ToolRunner Note: Skipped tool '{toolName}' because only one cosmetic tool may be executed per turn.");
                        break;
                    }
                    if (isContinuation && (executedAnyInfoTool || executedAnyActionTool || executedAnyCosmeticTool))
                    {
                        combinedResults.AppendLine($"ToolRunner Note: Skipped tool '{toolName}' and any following tools because continuation turns may execute only one tool.");
                        break;
                    }

                    if (xmlOnlyBuilder.Length > 0) xmlOnlyBuilder.AppendLine().AppendLine();
                    xmlOnlyBuilder.Append(toolCallXml);

                    var tool = _tools.FirstOrDefault(t => t.Name == toolName);
                    if (tool == null)
                    {
                        string errorMsg = $"Error: Tool '{toolName}' not found.";
                        WulaLog.Debug($"[WulaAI] {errorMsg}");
                        combinedResults.AppendLine(errorMsg);
                        continue;
                    }

                    // Extract inner XML for arguments
                    string argsXml = toolCallXml;
                    var contentMatch = Regex.Match(toolCallXml, $@"<{toolName}>(.*?)</{toolName}>", RegexOptions.Singleline);
                    if (contentMatch.Success)
                    {
                        argsXml = contentMatch.Groups[1].Value;
                    }

                    if (Prefs.DevMode)
                    {
                        WulaLog.Debug($"[WulaAI] Executing tool: {toolName} with args: {argsXml}");
                    }

                    // Record tool signature for loop detection (before execution, so errors also count)
                    string signature = $"{toolName}:{NormalizeToolArgs(argsXml)}";
                    _recentToolSignatures.Add(signature);
                    if (_recentToolSignatures.Count > 12) _recentToolSignatures.RemoveRange(0, _recentToolSignatures.Count - 12);

                    string result = tool.Execute(argsXml).Trim();
                    if (Prefs.DevMode && !string.IsNullOrEmpty(result))
                    {
                        string toLog = result.Length <= 2000 ? result : result.Substring(0, 2000) + $"... (truncated, total {result.Length} chars)";
                        WulaLog.Debug($"[WulaAI] Tool '{toolName}' result: {toLog}");
                    }

                    if (toolName == "modify_goodwill")
                    {
                        combinedResults.AppendLine($"Tool '{toolName}' Result (Invisible): {result}");
                    }
                    else
                    {
                        combinedResults.AppendLine($"Tool '{toolName}' Result: {result}");
                    }

                    if (isAction) executedAnyActionTool = true;
                    else if (isCosmetic) executedAnyCosmeticTool = true;
                    else executedAnyInfoTool = true;
                    if (IsMajorActionToolName(toolName)) executedAnyMajorActionTool = true;

                    // If we detect a loop, stop early (continuation-only; initial turns can legitimately query repeatedly).
                    if (isContinuation && ShouldTriggerLoopGuard())
                    {
                        combinedResults.AppendLine("ToolRunner Guard: Detected a repeated tool-call loop. You MUST stop calling tools and reply to the player in natural language only.");
                        break;
                    }
                }

                // Store only the tool-call XML in history (ignore any extra text the model included).
                string xmlOnly = xmlOnlyBuilder.ToString().Trim();
                _history.Add(("assistant", xmlOnly));
                // Persist tool results with a dedicated role; the API request maps this role to a supported one.
                _history.Add(("tool", $"[Tool Results]\n{combinedResults.ToString().Trim()}"));
                PersistHistory();

                // Loop breaker: if the model keeps repeating tools, inject a strong system reminder once; then fall back to a safe local response.
                if (isContinuation && ShouldTriggerLoopGuard())
                {
                    if (!_toolLoopGuardTriggered)
                    {
                        _toolLoopGuardTriggered = true;
                        _history.Add(("system", "[ToolLoopGuard] You are stuck repeating tools. STOP calling tools now and reply to the player in natural language only. Do NOT output any XML."));
                        PersistHistory();
                        await GenerateResponse(isContinuation: true);
                        return;
                    }

                    ParseResponse("（系统）AI 已陷入重复调用工具的循环，为避免卡死已停止继续调用。请直接说明你希望 AI 做什么，或输入 /clear 清空上下文后再试。");
                    return;
                }

                if (executedAnyMajorActionTool)
                {
                    _responseOnlyNext = true;
                }

                // Always recurse: tool results are fed back to the model, and the next response should be user-facing text.
                await GenerateResponse(isContinuation: true);
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[WulaAI] Exception in HandleXmlToolUsage: {ex}");
                _history.Add(("tool", $"Error processing tool call: {ex.Message}"));
                PersistHistory();
                await GenerateResponse(isContinuation: true);
            }
        }

        private void ParseResponse(string rawResponse, bool addToHistory = true)
        {
            _currentResponse = rawResponse;
            var parts = rawResponse.Split(new[] { "OPTIONS:" }, StringSplitOptions.None);
            if (addToHistory)
            {
                if (_history.Count == 0 || _history.Last().role != "assistant" || _history.Last().message != rawResponse)
                {
                    _history.Add(("assistant", rawResponse));
                    PersistHistory();
                }
            }

            if (!string.IsNullOrEmpty(ParseResponseForDisplay(rawResponse)))
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

            // 定义边距
            float margin = 15f;
            Rect paddedRect = inRect.ContractedBy(margin);

            float curY = paddedRect.y;
            float width = paddedRect.width;

            // 立绘不需要边距，所以使用原始inRect的位置
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

            // 人物名字 - 居中显示
            Text.Font = GameFont.Medium;
            string name = def.characterName ?? "The Legion";
            float nameHeight = Text.CalcHeight(name, width);

            // 创建名字的矩形，使其在窗口水平居中
            Rect nameRect = new Rect(paddedRect.x, curY, width, nameHeight);
            Text.Anchor = TextAnchor.UpperCenter;  // 改为上中对齐
            Widgets.Label(nameRect, name);
            Text.Anchor = TextAnchor.UpperLeft;    // 恢复左对齐

            curY += nameHeight + 10f;

            // 计算输入框高度、选项高度和聊天历史高度
            float inputHeight = 30f;
            float optionsHeight = _options.Any() ? 100f : 0f;
            float spacing = 10f;

            // 聊天历史区域 - 使用带边距的矩形
            float descriptionHeight = paddedRect.height - curY - inputHeight - optionsHeight - spacing * 2;
            Rect descriptionRect = new Rect(paddedRect.x, curY, width, descriptionHeight);
            DrawChatHistory(descriptionRect);

            if (_isThinking)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(descriptionRect, "Thinking...");
                Text.Anchor = TextAnchor.UpperLeft;
            }

            curY += descriptionHeight + spacing;

            // 选项区域
            Rect optionsRect = new Rect(paddedRect.x, curY, width, optionsHeight);
            if (!_isThinking && _options.Count > 0)
            {
                List<EventOption> eventOptions = _options.Select(opt => new EventOption { label = opt, useCustomColors = false }).ToList();
                DrawOptions(optionsRect, eventOptions);
            }

            curY += optionsHeight + spacing;

            // 输入框区域 - 使用带边距的矩形
            Rect inputRect = new Rect(paddedRect.x, curY, width, inputHeight);

            // 保存当前字体
            var originalFont = Text.Font;

            // 设置更小的字体
            if (Text.Font == GameFont.Small)
            {
                // 使用 Tiny 字体
                Text.Font = GameFont.Tiny;
            }
            else
            {
                // 如果当前不是 Small，降一级
                Text.Font = GameFont.Small;
            }

            // 计算输入框文本高度
            float textFieldHeight = Text.CalcHeight("Test", inputRect.width - 85);
            Rect textFieldRect = new Rect(inputRect.x, inputRect.y + (inputHeight - textFieldHeight) / 2, inputRect.width - 85, textFieldHeight);

            _inputText = Widgets.TextField(textFieldRect, _inputText);

            // 发送按钮 - 使用与Dialog_CustomDisplay相同的自定义按钮样式
            // 保存当前状态
            var originalAnchor = Text.Anchor;
            var originalColor = GUI.color;

            // 设置字体为Tiny
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
                var filteredHistory = _history.Where(e => e.role != "tool" && e.role != "system").ToList();

                // 添加内边距
                float innerPadding = 5f;
                float contentWidth = rect.width - 16f - innerPadding * 2;

                // 预计算高度 - 使用小字体
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

                    // 添加内边距
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
            
            text = text.Trim();
            
            return text.Split(new[] { "OPTIONS:" }, StringSplitOptions.None)[0].Trim();
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
            if (!string.IsNullOrWhiteSpace(text) && string.Equals(text.Trim(), "/clear", StringComparison.OrdinalIgnoreCase))
            {
                _isThinking = false;
                _options.Clear();
                _inputText = "";
                _continuationDepth = 0;
                _recentToolSignatures.Clear();
                _toolLoopGuardTriggered = false;
                _responseOnlyNext = false;

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

                Messages.Message("已清除 AI 对话上下文历史。", MessageTypeDefOf.NeutralEvent);
                return;
            }

            // reset loop guard on new user input
            _recentToolSignatures.Clear();
            _toolLoopGuardTriggered = false;
            _responseOnlyNext = false;

            _history.Add(("user", text));
            PersistHistory();
            _scrollToBottom = true;
            await RunPhasedRequestAsync();
        }
        
        public override void PostClose()
        {
            if (Instance == this) Instance = null;
            PersistHistory();
            base.PostClose();
            HandleAction(def.dismissEffects);
        }
    }
}
