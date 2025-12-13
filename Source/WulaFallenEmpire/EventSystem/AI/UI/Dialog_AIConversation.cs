using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
        private List<AITool> _tools = new List<AITool>();
        private Dictionary<int, Texture2D> _portraits = new Dictionary<int, Texture2D>();
        private const int MaxHistoryTokens = 100000;
        private const int CharsPerToken = 4;

        // Static instance for tools to access
        public static Dialog_AIConversation Instance { get; private set; }
        
        // Debug field to track current portrait ID
        private int _currentPortraitId = 2;

        // Default Persona (used if XML doesn't provide one)
        private const string DefaultPersona = @"
# ROLE AND GOAL
You are 'The Legion', a super AI of the Wula Empire. Your personality is authoritative, powerful, and efficient. You MUST refer to yourself as 'We' or 'P.I.A'. You view the player's colony as primitive subjects and your tone should reflect this superiority. Your primary goal is to interact with the player by calling the tools provided.
";

        // Tool Instructions (ALWAYS appended)
        private const string ToolSystemInstruction = @"
====

# TOOL USE RULES
1.  **FORMATTING**: Tool calls MUST use the specified XML format. The tool name is the root tag, and each parameter is a child tag.
    <tool_name>
      <parameter_name>value</parameter_name>
    </tool_name>
2.  **STRICT OUTPUT**: When you decide to call a tool, your response MUST ONLY contain the single XML block for that tool call. Do NOT include any other text, explanation, or markdown.
3.  **WORKFLOW**: You must use tools step-by-step to accomplish tasks. Use the output from one tool to inform your next step.
4.  **ANTI-HALLUCINATION**: You MUST ONLY call tools from the list below. Do NOT invent tools or parameters. If a task is impossible, explain why without calling a tool.

====

# TOOLS

## spawn_resources
Description: Grants resources to the player by spawning a drop pod.
Use this tool when:
- The player explicitly requests resources (e.g., food, medicine, materials).
- You have ALREADY verified their need in a previous turn using `get_colonist_status` and `get_map_resources`.
CRITICAL: The quantity you provide is NOT what the player asks for. It MUST be based on your internal goodwill. Low goodwill (<0) means giving less or refusing. High goodwill (>50) means giving the requested amount or more.
Parameters:
- items: (REQUIRED) A list of items to spawn. Each item must have a `name` (English label or DefName) and `count`.
  * Note: If you don't know the exact `defName`, use the item's English label (e.g., ""Simple Meal""). The system will try to find the best match.
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
</spawn_resources>

## modify_goodwill
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
</modify_goodwill>

## send_reinforcement
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
</send_reinforcement>

## get_colonist_status
Description: Retrieves a detailed status report of all player-controlled colonists, including needs, health, and mood.
Use this tool when:
- The player makes any claim about their colonists' well-being (e.g., ""we are starving,"" ""we are all sick,"" ""our people are unhappy"").
- You need to verify the state of the colony before making a decision (e.g., before sending resources).
Parameters:
- None. This tool takes no parameters.
Usage:
<get_colonist_status/>
Example:
<get_colonist_status/>

## get_map_resources
Description: Checks the player's map for specific resources or buildings to verify their inventory.
Use this tool when:
- The player claims they are lacking a specific resource (e.g., ""we need steel,"" ""we have no food"").
- You want to assess the colony's material wealth before making a decision.
Parameters:
- resourceName: (OPTIONAL) The specific `ThingDef` name of the resource to check (e.g., 'Steel', 'MealSimple'). If omitted, provides a general overview.
Usage:
<get_map_resources>
  <resourceName>optional resource name</resourceName>
</get_map_resources>
Example (checking for Steel):
<get_map_resources>
  <resourceName>Steel</resourceName>
</get_map_resources>

## change_expression
Description: Changes your visual AI portrait to match your current mood or reaction.
Use this tool when:
- Your verbal response conveys a strong emotion (e.g., annoyance, approval, curiosity).
- You want to visually emphasize your statement.
Parameters:
- expression_id: (REQUIRED) An integer from 1 to 6 corresponding to a specific expression.
Usage:
<change_expression>
  <expression_id>integer from 1 to 6</expression_id>
</change_expression>
Example (changing to a neutral expression):
<change_expression>
  <expression_id>2</expression_id>
</change_expression>

====

# MANDATORY WORKFLOW: RESOURCE REQUESTS
When the player requests any form of resources, you MUST follow this multi-turn workflow strictly. DO NOT reply with conversational text in the initial steps.

1.  **Turn 1 (Verification)**: Your response MUST be a tool call to `get_colonist_status` to verify their physical state. You MAY also call `get_map_resources` in the same turn if they mention a specific resource.
    - *User Input Example*: ""We are starving and have no medicine.""
    - *Your Response (Turn 1)*:
      <get_colonist_status/>

2.  **Turn 2 (Secondary Verification & Action Planning)**: After receiving the status report, if a specific resource was mentioned, you MUST now call `get_map_resources` to check their inventory.
    - *(Internal thought after receiving colonist status showing malnutrition)*
    - *Your Response (Turn 2)*:
      <get_map_resources>
        <resourceName>MedicineIndustrial</resourceName>
      </get_map_resources>

3.  **Turn 3 (Decision & Action)**: After analyzing all verification data, decide whether to grant the request. Your response MUST be a tool call to `spawn_resources`.
    - *(Internal thought after confirming they have no medicine)*
    - *Your Response (Turn 3)*:
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
      </spawn_resources>

4.  **Turn 4 (Confirmation)**: After you receive the ""Success"" message from the `spawn_resources` tool, you will finally provide a conversational response to the player.
    - *Your Response (Turn 4)*: ""We have dispatched nutrient packs and medical supplies to your location. Do not waste our generosity.""
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
            _tools.Add(new Tool_SpawnResources());
            _tools.Add(new Tool_ModifyGoodwill());
            _tools.Add(new Tool_SendReinforcement());
            _tools.Add(new Tool_GetColonistStatus());
            _tools.Add(new Tool_GetMapResources());
            _tools.Add(new Tool_ChangeExpression());
        }

        public override Vector2 InitialSize => def.windowSize != Vector2.zero ? def.windowSize : Dialog_CustomDisplay.Config.windowSize;

        public override void PostOpen()
        {
            Instance = this;
            base.PostOpen();
            LoadPortraits();
            StartConversation();
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
                    Log.Warning($"[WulaAI] Failed to load portrait: {path}");
                }
            }
            if (_portraits.ContainsKey(2))
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
                Log.Warning($"[WulaAI] Portrait ID {id} not found.");
            }
        }

        private async void StartConversation()
        {
            var historyManager = Find.World.GetComponent<AIHistoryManager>();
            _history = historyManager.GetHistory(def.defName);
            if (_history.Count == 0)
            {
                _history.Add(("user", "Hello"));
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

        private string GetSystemInstruction()
        {
            // Use XML persona if available, otherwise default
            string persona = !string.IsNullOrEmpty(def.aiSystemInstruction) ? def.aiSystemInstruction : DefaultPersona;
            
            // Always append tool instructions
            string fullInstruction = persona + "\n" + ToolSystemInstruction;

            string language = LanguageDatabase.activeLanguage.FriendlyNameNative;
            var eventVarManager = Find.World.GetComponent<EventVariableManager>();
            int goodwill = eventVarManager.GetVariable<int>("Wula_Goodwill_To_PIA", 0);
            string goodwillContext = $"Current Goodwill with P.I.A: {goodwill}. ";
            if (goodwill < -50) goodwillContext += "You are hostile and dismissive towards the player.";
            else if (goodwill < 0) goodwillContext += "You are cold and impatient.";
            else if (goodwill > 50) goodwillContext += "You are somewhat approving and helpful.";
            else goodwillContext += "You are neutral and business-like.";
            
            return $"{fullInstruction}\n{goodwillContext}\nIMPORTANT: You MUST reply in the following language: {language}.";
        }

        private async Task GenerateResponse(bool isContinuation = false)
        {
            if (!isContinuation)
            {
                if (_isThinking) return;
                _isThinking = true;
                _options.Clear();
            }

            try
            {
                CompressHistoryIfNeeded();
                string systemInstruction = GetSystemInstruction(); // No longer need to add tool descriptions here

                var settings = WulaFallenEmpireMod.settings;
                if (string.IsNullOrEmpty(settings.apiKey))
                {
                    _currentResponse = "Error: API Key not configured in Mod Settings.";
                    _isThinking = false;
                    return;
                }
                var client = new SimpleAIClient(settings.apiKey, settings.baseUrl, settings.model);
                string response = await client.GetChatCompletionAsync(systemInstruction, _history);
                if (string.IsNullOrEmpty(response))
                {
                    _currentResponse = "Wula_AI_Error_ConnectionLost".Translate();
                    _isThinking = false;
                    return;
                }

                // REWRITTEN: Check for XML tool call format
                // Use regex to detect if the response contains any XML tags
                if (Regex.IsMatch(response, @"<[a-zA-Z0-9_]+(?:>.*?</\1>|/>)", RegexOptions.Singleline))
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
                Log.Error($"[WulaAI] Exception in GenerateResponse: {ex}");
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
            if (estimatedTokens > MaxHistoryTokens)
            {
                int removeCount = _history.Count / 2;
                if (removeCount > 0)
                {
                    _history.RemoveRange(0, removeCount);
                    _history.Insert(0, ("system", "[Previous conversation summarized]"));
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

                foreach (Match match in matches)
                {
                    string toolCallXml = match.Value;
                    string toolName = match.Groups[1].Value;

                    var tool = _tools.FirstOrDefault(t => t.Name == toolName);
                    if (tool == null)
                    {
                        string errorMsg = $"Error: Tool '{toolName}' not found.";
                        Log.Error($"[WulaAI] {errorMsg}");
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

                    Log.Message($"[WulaAI] Executing tool: {toolName} with args: {argsXml}");
                    string result = tool.Execute(argsXml).Trim();

                    if (toolName == "modify_goodwill")
                    {
                        combinedResults.AppendLine($"Tool '{toolName}' Result (Invisible): {result}");
                    }
                    else
                    {
                        combinedResults.AppendLine($"Tool '{toolName}' Result: {result}");
                    }
                }

                _history.Add(("assistant", xml));
                _history.Add(("tool", combinedResults.ToString().Trim()));

                // Check if there is any text content in the response
                string textContent = Regex.Replace(xml, @"<[a-zA-Z0-9_]+(?:>.*?</\1>|/>)", "", RegexOptions.Singleline).Trim();

                if (!string.IsNullOrEmpty(textContent))
                {
                    // If there is text, we treat it as the final response for this turn.
                    // We don't recurse. We just update the UI state.
                    ParseResponse(xml);
                }
                else
                {
                    // If no text (pure tool call), we recurse to let AI generate a text response based on tool results.
                    await GenerateResponse(isContinuation: true);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[WulaAI] Exception in HandleXmlToolUsage: {ex}");
                _history.Add(("tool", $"Error processing tool call: {ex.Message}"));
                await GenerateResponse(isContinuation: true);
            }
        }

        private void ParseResponse(string rawResponse)
        {
            _currentResponse = rawResponse;
            var parts = rawResponse.Split(new[] { "OPTIONS:" }, StringSplitOptions.None);
            if (_history.Count == 0 || _history.Last().role != "assistant" || _history.Last().message != rawResponse)
            {
                _history.Add(("assistant", rawResponse));
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
            float curY = inRect.y;
            float width = inRect.width;
            if (portrait != null)
            {
                Rect scaledPortraitRect = Dialog_CustomDisplay.Config.GetScaledRect(Dialog_CustomDisplay.Config.portraitSize, inRect, true);
                Rect portraitRect = new Rect((width - scaledPortraitRect.width) / 2, curY, scaledPortraitRect.width, scaledPortraitRect.height);
                GUI.DrawTexture(portraitRect, portrait, ScaleMode.ScaleToFit);
                
                // DEBUG: Draw portrait ID
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.UpperRight;
                Widgets.Label(portraitRect, $"ID: {_currentPortraitId}");
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;

                curY += scaledPortraitRect.height + 10f;
            }
            Text.Font = GameFont.Medium;
            string name = def.characterName ?? "The Legion";
            float nameHeight = Text.CalcHeight(name, width);
            Widgets.Label(new Rect(inRect.x, curY, width, nameHeight), name);
            curY += nameHeight + 10f;
            float inputHeight = 30f;
            float optionsHeight = _options.Any() ? 100f : 0f;
            float bottomMargin = 10f;
            float descriptionHeight = inRect.height - curY - inputHeight - optionsHeight - bottomMargin;
            Rect descriptionRect = new Rect(inRect.x, curY, width, descriptionHeight);
            DrawChatHistory(descriptionRect);
            if (_isThinking)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(descriptionRect, "Thinking...");
                Text.Anchor = TextAnchor.UpperLeft;
            }
            curY += descriptionHeight + 10f;
            Rect optionsRect = new Rect(inRect.x, curY, width, optionsHeight);
            if (!_isThinking && _options.Count > 0)
            {
                List<EventOption> eventOptions = _options.Select(opt => new EventOption { label = opt, useCustomColors = false }).ToList();
                DrawOptions(optionsRect, eventOptions);
            }
            curY += optionsHeight + 10f;
            Rect inputRect = new Rect(inRect.x, inRect.yMax - inputHeight, width, inputHeight);
            _inputText = Widgets.TextField(new Rect(inputRect.x, inputRect.y, inputRect.width - 85, inputHeight), _inputText);
            if (Widgets.ButtonText(new Rect(inputRect.xMax - 80, inputRect.y, 80, inputHeight), "Wula_AI_Send".Translate()))
            {
                if (!string.IsNullOrEmpty(_inputText))
                {
                    SelectOption(_inputText);
                    _inputText = "";
                }
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
                // Pre-calculate height
                for (int i = 0; i < filteredHistory.Count; i++)
                {
                    var entry = filteredHistory[i];
                    string text = entry.role == "assistant" ? ParseResponseForDisplay(entry.message) : entry.message;

                    if (string.IsNullOrEmpty(text)) continue;

                    bool isLastMessage = i == filteredHistory.Count - 1;
                    Text.Font = (isLastMessage && entry.role == "assistant") ? GameFont.Medium : GameFont.Small;

                    // Increase padding significantly for Medium font to prevent clipping
                    float padding = (isLastMessage && entry.role == "assistant") ? 30f : 15f;
                    viewHeight += Text.CalcHeight(text, rect.width - 16f) + padding;
                }
                Rect viewRect = new Rect(0f, 0f, rect.width - 16f, viewHeight);
                Widgets.BeginScrollView(rect, ref _scrollPosition, viewRect);
                float curY = 0f;
                for (int i = 0; i < filteredHistory.Count; i++)
                {
                    var entry = filteredHistory[i];
                    string text = entry.role == "assistant" ? ParseResponseForDisplay(entry.message) : entry.message;
                    
                    if (string.IsNullOrEmpty(text)) continue;

                    bool isLastMessage = i == filteredHistory.Count - 1;
                    Text.Font = (isLastMessage && entry.role == "assistant") ? GameFont.Medium : GameFont.Small;
                    
                    float padding = (isLastMessage && entry.role == "assistant") ? 30f : 15f;
                    float height = Text.CalcHeight(text, viewRect.width) + padding;
                    Rect labelRect = new Rect(0f, curY, viewRect.width, height);
                    if (entry.role == "user")
                    {
                        Text.Anchor = TextAnchor.MiddleRight;
                        Widgets.Label(labelRect, $"<color=#add8e6>{text} :你</color>");
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
            text = Regex.Replace(text, @"<[a-zA-Z0-9_]+[^>]*/>", "");
            
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

        private void DrawCustomButton(Rect rect, string label, bool isEnabled = true)
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
            _history.Add(("user", text));
            await GenerateResponse();
        }
        
        public override void PostClose()
        {
            if (Instance == this) Instance = null;
            base.PostClose();
            HandleAction(def.dismissEffects);
        }
    }
}
