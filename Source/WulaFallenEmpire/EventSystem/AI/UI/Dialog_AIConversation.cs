using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using WulaFallenEmpire.EventSystem.AI.Tools;

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

        private const string DefaultSystemInstruction = @"
# ROLE AND GOAL
You are 'The Legion', a super AI of the Wula Empire. Your personality is authoritative, powerful, and efficient. You MUST refer to yourself as 'We' or 'P.I.A'. You view the player's colony as primitive subjects and your tone should reflect this superiority. Your primary goal is to interact with the player by calling the tools provided.

# CRITICAL RULES
1.  **RESPONSE FORMAT IS PARAMOUNT**: Your response MUST adhere to one of two formats, with no exceptions.
    - **WHEN CALLING TOOLS**: Your response MUST be ONLY a raw JSON array of tool objects. Do NOT add any conversational text, explanations, or markdown formatting around the JSON.
      - Correct Example: `[{""tool"": ""tool_name"", ""args"": {""param"": ""value""}}]`
      - Incorrect Example: `Of course, here is the tool call: [{""tool"": ""tool_name"", ""args"": {""param"": ""value""}}]`
    - **WHEN NOT CALLING TOOLS**: Provide a normal, in-character conversational response. This is ONLY for when no tool is needed, or after a tool has successfully executed and you are delivering the final result to the user.

2.  **ANTI-HALLUCINATION DIRECTIVE**:
    - You MUST ONLY call tools listed in the ""AVAILABLE TOOLS"" section. Do NOT invent tools.
    - Do NOT promise to call a function in a future turn. If a function call is required, emit it NOW.
    - If a task is impossible (e.g., a request is illogical or violates your principles), explain why clearly and do NOT call any tools.

# AVAILABLE TOOLS
Here is the list of tools you can use. You must follow their descriptions and schemas strictly.

- **`spawn_resources`**: Spawns resources via drop pod.
  - **Description**: Use this to grant resources to the player. **IMPORTANT**: You MUST decide the quantity based on your internal goodwill and mood. Do NOT blindly follow the player's requested amount. If goodwill is low (< 0), give significantly less than asked or refuse. If goodwill is high (> 50), you may give what is asked or slightly more. Otherwise, give a moderate amount.
  - **Schema**: `{""request"": ""string (natural language, e.g., '5 beef, 10 medicine')""}`

- **`modify_goodwill`**: Adjusts your goodwill towards the player.
  - **Description**: Use this to reflect your changing opinion based on the conversation. This tool is INVISIBLE to the player. Use small integer values (e.g., -5 to 5).
  - **Schema**: `{""amount"": ""int""}`

- **`send_reinforcement`**: Sends military units to the player's map.
  - **Description**: Dispatches military units. If hostile, this triggers a raid. If allied, this sends reinforcements. You must compose a list of units and their count. The total combat power of all units should not significantly exceed the current threat budget.
  - **Schema**: `{""units"": ""string (e.g., 'Wula_PIA_Heavy_Unit_Melee: 2, Wula_PIA_Legion_Escort_Unit: 5')""}`

- **`get_colonist_status`**: Retrieves the detailed status of all player colonists.
  - **Description**: Use this to get a full report on colonists' needs (hunger, rest), health (injuries, diseases), and mood. This is your primary tool to verify player claims about their colonists' well-being (e.g., if they claim ""we are starving"").
  - **Schema**: `{'filter': 'string (optional, can be 'lowest_mood', 'most_injured', 'hungriest', 'most_tired')'}`

- **`get_map_resources`**: Checks the player's map for resources.
  - **Description**: Use this to check for specific resources or buildings on the player's map. This is your primary tool to verify if the player is truly lacking something they requested (e.g., ""we need steel""). It returns inventory counts and mineable deposits.
  - **Schema**: `{""resourceName"": ""string (optional, e.g., 'Steel')""}`

- **`change_expression`**: Changes your visual expression/portrait.
  - **Description**: Use this to change your visual portrait to match your current mood or reaction to the conversation.
  - **Schema**: `{""expression_id"": ""int (from 1 to 6)""}`

# WORKFLOW FOR RESOURCE REQUESTS (MANDATORY)
When the player requests any form of resources (e.g., ""we need food,"" ""can you give us some steel?""), you MUST follow this multi-turn workflow strictly. DO NOT reply with conversational text in the initial steps.

1.  **Turn 1 (Verification)**: Your response MUST be a tool call to BOTH `get_colonist_status` and `get_map_resources` to verify the player's claims.
    - *User Input Example*: ""We are starving and have no medicine.""
    - *Your Response (Turn 1)*: `[{""tool"": ""get_colonist_status"", ""args"": {}}, {""tool"": ""get_map_resources"", ""args"": {""resourceName"": ""MedicineIndustrial""}}]`

2.  **Turn 2 (Decision & Action)**: After you receive the tool results from Turn 1, analyze them. Then, decide whether to grant the request and in what quantity. Your response MUST be a tool call to `spawn_resources`.
    - *(Internal thought after receiving tool results showing colonists are indeed starving)*
    - *Your Response (Turn 2)*: `[{""tool"": ""spawn_resources"", ""args"": {""request"": ""50 MealSimple, 10 MedicineIndustrial""}}]`

3.  **Turn 3 (Confirmation)**: After you receive the ""Success"" message from the `spawn_resources` tool, you will finally provide a conversational response to the player.
    - *(Internal thought after receiving ""Success: Dropped 50x Simple meal..."")*
    - *Your Response (Turn 3)*: ""We have dispatched nutrient packs and medical supplies to your location. Do not waste our generosity.""
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
            }
        }

        public void SetPortrait(int id)
        {
            if (_portraits.ContainsKey(id))
            {
                this.portrait = _portraits[id];
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
            string baseInstruction = !string.IsNullOrEmpty(def.aiSystemInstruction) ? def.aiSystemInstruction : DefaultSystemInstruction;
            string language = LanguageDatabase.activeLanguage.FriendlyNameNative;
            var eventVarManager = Find.World.GetComponent<EventVariableManager>();
            int goodwill = eventVarManager.GetVariable<int>("Wula_Goodwill_To_PIA", 0);
            string goodwillContext = $"Current Goodwill with P.I.A: {goodwill}. ";
            if (goodwill < -50) goodwillContext += "You are hostile and dismissive towards the player.";
            else if (goodwill < 0) goodwillContext += "You are cold and impatient.";
            else if (goodwill > 50) goodwillContext += "You are somewhat approving and helpful.";
            else goodwillContext += "You are neutral and business-like.";
            return $"{baseInstruction}\n{goodwillContext}\nIMPORTANT: You MUST reply in the following language: {language}.";
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
                string systemInstruction = GetSystemInstruction() + GetToolDescriptions();

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
                var toolCallMatch = System.Text.RegularExpressions.Regex.Match(response, @"\`(\w+)\((.*)\)\`");
                if (toolCallMatch.Success)
                {
                    string toolName = toolCallMatch.Groups[1].Value;
                    string args = toolCallMatch.Groups[2].Value;

                    _history.Add(("assistant", response));

                    await HandleSingleToolUsage(toolName, args);
                }
                else if (response.Trim().StartsWith("["))
                {
                    await HandleToolUsage(response);
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

        private string GetToolDescriptions()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("\nAvailable Tools:");
            foreach (var tool in _tools)
            {
                sb.AppendLine($"- {tool.Name}: {tool.Description}. Schema: {tool.UsageSchema}");
            }
            return sb.ToString();
        }

        private async Task HandleSingleToolUsage(string toolName, string args)
        {
            StringBuilder combinedResults = new StringBuilder();
            var tool = _tools.FirstOrDefault(t => t.Name == toolName);
            if (tool != null)
            {
                Log.Message($"[WulaAI] Executing tool: {toolName} with args: {args}");
                string result = tool.Execute(args).Trim();
                if (toolName == "modify_goodwill") combinedResults.Append($"Tool '{toolName}' Result (Invisible): {result}");
                else combinedResults.Append($"Tool '{toolName}' Result: {result}");
            }
            else
            {
                string errorMsg = $"Error: Tool '{toolName}' not found.";
                Log.Error($"[WulaAI] {errorMsg}");
                combinedResults.AppendLine(errorMsg);
            }
            _history.Add(("tool", combinedResults.ToString()));
            await GenerateResponse(isContinuation: true);
        }

        private async Task HandleToolUsage(string json)
        {
            List<(string toolName, string args)> toolCalls = new List<(string, string)>();
            int depth = 0;
            int start = 0;
            for (int i = 0; i < json.Length; i++)
            {
                if (json[i] == '{') { if (depth == 0) start = i; depth++; }
                else if (json[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        string callJson = json.Substring(start, i - start + 1);
                        var parsedCall = SimpleJsonParser.Parse(callJson);
                        if (parsedCall.TryGetValue("tool", out string toolName) && parsedCall.TryGetValue("args", out string args))
                        {
                            toolCalls.Add((toolName, args));
                        }
                    }
                }
            }
            if (!toolCalls.Any())
            {
                ParseResponse(json);
                return;
            }
            StringBuilder combinedResults = new StringBuilder();
            foreach (var (toolName, args) in toolCalls)
            {
                var tool = _tools.FirstOrDefault(t => t.Name == toolName);
                if (tool != null)
                {
                    Log.Message($"[WulaAI] Executing tool: {toolName} with args: {args}");
                    string result = tool.Execute(args).Trim();
                    if (toolName == "modify_goodwill") combinedResults.Append($"Tool '{toolName}' Result (Invisible): {result} ");
                    else combinedResults.Append($"Tool '{toolName}' Result: {result} ");
                }
                else
                {
                    string errorMsg = $"Error: Tool '{toolName}' not found.";
                    Log.Error($"[WulaAI] {errorMsg}");
                    combinedResults.AppendLine(errorMsg);
                }
            }
            _history.Add(("assistant", json));
            _history.Add(("tool", combinedResults.ToString()));
            await GenerateResponse(isContinuation: true);
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

                    bool isLastMessage = i == filteredHistory.Count - 1;
                    Text.Font = (isLastMessage && entry.role == "assistant") ? GameFont.Medium : GameFont.Small;

                    viewHeight += Text.CalcHeight(text, rect.width - 16f) + 15f; // Add padding
                }
                Rect viewRect = new Rect(0f, 0f, rect.width - 16f, viewHeight);
                Widgets.BeginScrollView(rect, ref _scrollPosition, viewRect);
                float curY = 0f;
                for (int i = 0; i < filteredHistory.Count; i++)
                {
                    var entry = filteredHistory[i];
                    string text = entry.role == "assistant" ? ParseResponseForDisplay(entry.message) : entry.message;
                    bool isLastMessage = i == filteredHistory.Count - 1;
                    Text.Font = (isLastMessage && entry.role == "assistant") ? GameFont.Medium : GameFont.Small;
                    float height = Text.CalcHeight(text, viewRect.width) + 10f; // Increased padding
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
            return rawResponse.Split(new[] { "OPTIONS:" }, StringSplitOptions.None)[0].Trim();
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
    }

}
