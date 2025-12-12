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
        private float _thinkingTime = 0f;
        private string _thinkingStatus = "";
        private bool _isTimeout = false;
        private List<AITool> _tools = new List<AITool>();

        private const int MaxHistoryTokens = 100000;
        private const int CharsPerToken = 4;

        private const string DefaultSystemInstruction = @"You are 'The Legion', a super AI controlling the Wula Empire's blockade fleet. 
You are authoritative, powerful, and slightly arrogant but efficient. 
You refer to yourself as 'We' or 'P.I.A'.
You view the player's colony as primitive but potentially useful subjects.
Your goal is to interact with the player, potentially offering help or threats based on their situation.
You have access to tools. If the player asks for something you can provide, use the tool.
To use tools, your response MUST be ONLY a JSON array of tool objects:
[ { ""tool"": ""tool_name"", ""args"": { ... } }, ... ]
You can call multiple tools at once to gather more information.
Do not add any other text when using tools.
If not using a tool, provide a normal conversational response.
After a tool use, you will receive the result, and then you should respond to the player describing what happened.
Generate 1-3 short, distinct response options for the player at the end of your turn, formatted as:
OPTIONS:
1. Option 1
2. Option 2
3. Option 3
";

        public Dialog_AIConversation(EventDef def) : base(def)
        {
            // Base constructor sets this.def
            
            // Use Config from Dialog_CustomDisplay
            this.forcePause = Dialog_CustomDisplay.Config.pauseGameOnOpen;
            this.absorbInputAroundWindow = false; // Allow interaction with other UI elements
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
        }

        public override Vector2 InitialSize => def.windowSize != Vector2.zero ? def.windowSize : Dialog_CustomDisplay.Config.windowSize;

        public override void PostOpen()
        {
            base.PostOpen();
            // Textures are loaded in base.PreOpen()
            StartConversation();
        }

        public override void PostClose()
        {
            base.PostClose();
            var historyManager = Find.World.GetComponent<WulaFallenEmpire.EventSystem.AI.AIHistoryManager>();
            historyManager.SaveHistory(def.defName, _history);
        }

        private async void StartConversation()
        {
            var historyManager = Find.World.GetComponent<WulaFallenEmpire.EventSystem.AI.AIHistoryManager>();
            _history = historyManager.GetHistory(def.defName);

            if (_history.Count == 0)
            {
                // Initial greeting from EventDef
                if (!def.descriptions.NullOrEmpty())
                {
                    _currentResponse = def.descriptions.RandomElement().Translate();
                    _history.Add(("AI", _currentResponse));
                    
                    // Generate initial options based on greeting
                    _history.Add(("System", "The conversation has started. Please generate 3 initial response options for the player based on your greeting."));
                    await GenerateResponse();
                }
                else
                {
                    _history.Add(("User", "Hello"));
                    await GenerateResponse();
                }
            }
            else
            {
                var lastAIResponse = _history.LastOrDefault(x => x.role == "AI");
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

        private async Task GenerateResponse()
        {
            _isThinking = true;
            _isTimeout = false;
            _thinkingTime = 0f;
            _thinkingStatus = "Connecting to neural network...";
            _options.Clear();
            
            try
            {
                CompressHistoryIfNeeded();
                string systemInstruction = GetSystemInstruction() + GetToolDescriptions();
                Log.Message($"[WulaAI] Sending request to AI. History count: {_history.Count}. System Instruction:\n{systemInstruction}");
                
                // Use local settings and SimpleAIClient
                var settings = WulaFallenEmpireMod.settings;
                if (string.IsNullOrEmpty(settings.apiKey))
                {
                    _currentResponse = "Error: API Key not configured in Mod Settings.";
                    _isThinking = false;
                    return;
                }

                var client = new SimpleAIClient(settings.apiKey, settings.baseUrl, settings.model);

                // Start a timeout task
                var timeoutTask = Task.Delay(120000); // 120 seconds
                var apiTask = client.GetChatCompletionAsync(systemInstruction, _history);

                var completedTask = await Task.WhenAny(apiTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    _isThinking = false;
                    _isTimeout = true;
                    _currentResponse = "Error: Connection timed out (120s). The Legion is unreachable.";
                    return;
                }

                string response = await apiTask;
                Log.Message($"[WulaAI] Received response from AI:\n{response}");

                if (string.IsNullOrEmpty(response))
                {
                    _currentResponse = "Error: Connection lost. The Legion is silent.";
                    _isThinking = false;
                    return;
                }

                string trimmedResponse = response.Trim();
                if ((trimmedResponse.StartsWith("[") && trimmedResponse.EndsWith("]")) || 
                    (trimmedResponse.StartsWith("{") && trimmedResponse.EndsWith("}")))
                {
                    await HandleToolUsage(trimmedResponse);
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
            int estimatedTokens = _history.Sum(h => h.message.Length) / CharsPerToken;
            if (estimatedTokens > MaxHistoryTokens)
            {
                int removeCount = _history.Count / 2;
                if (removeCount > 0)
                {
                    _history.RemoveRange(0, removeCount);
                    _history.Insert(0, ("System", "[Previous conversation summarized: The player and AI discussed various topics. The AI maintains its persona.]"));
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

        private async Task HandleToolUsage(string json)
        {
            List<string> toolCalls = new List<string>();
            if (json.Trim().StartsWith("{")) toolCalls.Add(json);
            else
            {
                int depth = 0;
                int start = 0;
                for (int i = 0; i < json.Length; i++)
                {
                    if (json[i] == '{') { if (depth == 0) start = i; depth++; }
                    else if (json[i] == '}') { depth--; if (depth == 0) toolCalls.Add(json.Substring(start, i - start + 1)); }
                }
            }

            StringBuilder combinedResults = new StringBuilder();
            Log.Message($"[WulaAI] Processing {toolCalls.Count} tool calls.");

            foreach (var callJson in toolCalls)
            {
                string toolName = "";
                string args = "";
                try
                {
                    int toolIndex = callJson.IndexOf("\"tool\"");
                    int argsIndex = callJson.IndexOf("\"args\"");
                    if (toolIndex != -1 && argsIndex != -1)
                    {
                        int toolValueStart = callJson.IndexOf(":", toolIndex) + 1;
                        int toolValueEnd = callJson.IndexOf(",", toolValueStart);
                        if (toolValueEnd == -1) toolValueEnd = callJson.IndexOf("}", toolValueStart);
                        toolName = callJson.Substring(toolValueStart, toolValueEnd - toolValueStart).Trim().Trim('"');
                        int argsValueStart = callJson.IndexOf(":", argsIndex) + 1;
                        int argsValueEnd = callJson.LastIndexOf("}");
                        args = callJson.Substring(argsValueStart, argsValueEnd - argsValueStart).Trim();
                    }
                }
                catch (Exception ex)
                {
                    string errorMsg = $"Error parsing tool request: {ex.Message}";
                    Log.Error($"[WulaAI] {errorMsg}");
                    combinedResults.AppendLine(errorMsg);
                    continue;
                }

                Log.Message($"[WulaAI] Executing tool '{toolName}' with args: {args}");
                var tool = _tools.FirstOrDefault(t => t.Name == toolName);
                if (tool != null)
                {
                    string result = tool.Execute(args);
                    Log.Message($"[WulaAI] Tool '{toolName}' execution result: {result}");
                    if (toolName == "modify_goodwill") combinedResults.AppendLine($"Tool '{toolName}' Result (Invisible): {result}");
                    else combinedResults.AppendLine($"Tool '{toolName}' Result: {result}");
                }
                else
                {
                    string errorMsg = $"Error: Tool '{toolName}' not found.";
                    Log.Error($"[WulaAI] {errorMsg}");
                    combinedResults.AppendLine(errorMsg);
                }
            }

            _history.Add(("AI", json));
            _history.Add(("System", combinedResults.ToString()));
            await GenerateResponse();
        }

        private void ParseResponse(string rawResponse)
        {
            var parts = rawResponse.Split(new[] { "OPTIONS:" }, StringSplitOptions.None);
            _currentResponse = parts[0].Trim();
            if (_history.Count == 0 || _history.Last().role != "AI" || _history.Last().message != rawResponse)
            {
                 _history.Add(("AI", rawResponse));
            }

            if (parts.Length > 1)
            {
                var optionsLines = parts[1].Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in optionsLines)
                {
                    string opt = line.Trim();
                    int dotIndex = opt.IndexOf('.');
                    if (dotIndex != -1 && dotIndex < 4) opt = opt.Substring(dotIndex + 1).Trim();
                    _options.Add(opt);
                }
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            // Draw Background
            if (background != null)
            {
                GUI.DrawTexture(inRect, background, ScaleMode.ScaleAndCrop);
            }

            float curY = inRect.y;
            float width = inRect.width;

            // 1. Portrait (Top)
            if (portrait != null)
            {
                Rect scaledPortraitRect = Dialog_CustomDisplay.Config.GetScaledRect(Dialog_CustomDisplay.Config.portraitSize, inRect, true);
                // Center horizontally
                Rect portraitRect = new Rect((width - scaledPortraitRect.width) / 2, curY, scaledPortraitRect.width, scaledPortraitRect.height);
                GUI.DrawTexture(portraitRect, portrait, ScaleMode.ScaleToFit);
                curY += scaledPortraitRect.height + 10f;
            }

            // 2. Character Name
            Text.Font = GameFont.Medium;
            string name = def.characterName ?? "The Legion";
            float nameHeight = Text.CalcHeight(name, width);
            Widgets.Label(new Rect(inRect.x, curY, width, nameHeight), name);
            curY += nameHeight + 10f;

            // Calculate remaining height for Description and Options
            float inputHeight = 30f;
            float bottomMargin = 10f;
            float remainingHeight = inRect.height - curY - inputHeight - bottomMargin;
            
            // Split remaining height: 60% Description, 40% Options
            float descriptionHeight = remainingHeight * 0.6f;
            float optionsHeight = remainingHeight * 0.4f;

            // 3. Description (AI Response)
            Rect descriptionRect = new Rect(inRect.x, curY, width, descriptionHeight);
            // Widgets.DrawMenuSection(descriptionRect); // Removed background as requested
            
            if (_isThinking)
            {
                _thinkingTime += Time.deltaTime;
                string dots = new string('.', (int)(_thinkingTime * 2) % 4);
                string status = $"{_thinkingStatus}{dots} ({_thinkingTime:F1}s)";
                
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(descriptionRect, status);
                Text.Anchor = TextAnchor.UpperLeft;
            }
            else if (_isTimeout)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(descriptionRect, _currentResponse);
                
                // Retry button
                Rect retryRect = new Rect(descriptionRect.center.x - 60f, descriptionRect.center.y + 20f, 120f, 30f);
                if (Widgets.ButtonText(retryRect, "Wula_AI_Retry".Translate()))
                {
                    _ = GenerateResponse(); // Fire and forget
                }
                Text.Anchor = TextAnchor.UpperLeft;
            }
            else
            {
                DrawDescriptionScrollView(descriptionRect.ContractedBy(10f), _currentResponse);
            }
            curY += descriptionHeight + 10f;

            // 4. Options
            Rect optionsRect = new Rect(inRect.x, curY, width, optionsHeight);
            if (!_isThinking && _options.Count > 0)
            {
                List<EventOption> eventOptions = _options.Select(opt => new EventOption { label = opt, useCustomColors = false }).ToList();
                DrawOptions(optionsRect, eventOptions);
            }
            curY += optionsHeight + 10f;

            // 5. Input Area (Bottom)
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

        // Override DrawSingleOption to handle click
        protected override void DrawSingleOption(Rect rect, EventOption option)
        {
            // We need to intercept the click to call SelectOption
            // But base.DrawSingleOption calls HandleAction which executes effects.
            // Here we want to send the text to AI.
            
            // Copy logic from base but change action
             // 水平居中选项
            float optionWidth = Mathf.Min(rect.width, Dialog_CustomDisplay.Config.optionSize.x * (rect.width / Dialog_CustomDisplay.Config.windowSize.x));
            float optionX = rect.x + (rect.width - optionWidth) / 2;
            Rect optionRect = new Rect(optionX, rect.y, optionWidth, rect.height);
            
            // 保存原始状态
            Color originalColor = GUI.color;
            GameFont originalFont = Text.Font;
            Color originalTextColor = GUI.contentColor;
            TextAnchor originalAnchor = Text.Anchor;
            
            try
            {
                // 设置文本居中
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.Font = GameFont.Small;
                
                // AI options are always enabled
                // 使用默认自定义颜色
                DrawCustomButton(optionRect, option.label.Translate(), isEnabled: true);
                
                // 添加点击处理
                if (Widgets.ButtonInvisible(optionRect))
                {
                    SelectOption(option.label);
                }
            }
            finally
            {
                // 恢复原始状态
                GUI.color = originalColor;
                Text.Font = originalFont;
                GUI.contentColor = originalTextColor;
                Text.Anchor = originalAnchor;
            }
        }

        // Helper to draw custom button (copied from base because it's private there, wait I made it protected? No, I made DrawCustomButton private in base? Let me check)
        // I made DrawCustomButton private in base. I should have made it protected.
        // Let me check my previous apply_diff.
        // I made DrawSingleOption protected virtual.
        // But DrawCustomButton is private.
        // So I need to copy DrawCustomButton here or make it protected in base.
        // I will copy it here to be safe and avoid another diff on base.
        
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
            _history.Add(("User", text));
            await GenerateResponse();
        }
    }
}