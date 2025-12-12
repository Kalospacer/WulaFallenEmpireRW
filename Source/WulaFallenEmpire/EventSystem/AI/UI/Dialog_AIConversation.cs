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
    public class Dialog_AIConversation : Window
    {
        private EventDef _def;
        private List<(string role, string message)> _history = new List<(string role, string message)>();
        private string _currentResponse = "Thinking...";
        private List<string> _options = new List<string>();
        private string _inputText = "";
        private bool _isThinking = false;
        private Vector2 _scrollPosition = Vector2.zero;
        private List<AITool> _tools = new List<AITool>();

        private const int MaxHistoryTokens = 100000; // Approximate token limit
        private const int CharsPerToken = 4; // Rough estimation

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

        public Dialog_AIConversation(EventDef def)
        {
            _def = def;
            this.doCloseX = true;
            this.forcePause = true;
            this.absorbInputAroundWindow = true;
            this.closeOnClickedOutside = false;

            _tools.Add(new Tool_SpawnResources());
            _tools.Add(new Tool_ModifyGoodwill());
            _tools.Add(new Tool_SendReinforcement());
            _tools.Add(new Tool_GetColonistStatus());
            _tools.Add(new Tool_GetMapResources());
        }

        public override Vector2 InitialSize => _def.windowSize != Vector2.zero ? _def.windowSize : new Vector2(600, 500);

        public override void PostOpen()
        {
            base.PostOpen();
            StartConversation();
        }

        public override void PostClose()
        {
            base.PostClose();
            // Save history on close
            var historyManager = Find.World.GetComponent<WulaFallenEmpire.EventSystem.AI.AIHistoryManager>();
            historyManager.SaveHistory(_def.defName, _history);
        }

        private async void StartConversation()
        {
            _isThinking = true;
            
            // Load history
            var historyManager = Find.World.GetComponent<WulaFallenEmpire.EventSystem.AI.AIHistoryManager>();
            _history = historyManager.GetHistory(_def.defName);

            if (_history.Count == 0)
            {
                _history.Add(("User", "Hello")); // Initial trigger
                await GenerateResponse();
            }
            else
            {
                // Restore last state
                var lastAIResponse = _history.LastOrDefault(x => x.role == "AI");
                if (lastAIResponse.message != null)
                {
                    ParseResponse(lastAIResponse.message);
                }
                else
                {
                    // Should not happen if history is valid, but fallback
                    await GenerateResponse();
                }
                _isThinking = false;
            }
        }

        private string GetSystemInstruction()
        {
            string baseInstruction = !string.IsNullOrEmpty(_def.aiSystemInstruction) ? _def.aiSystemInstruction : DefaultSystemInstruction;
            string language = LanguageDatabase.activeLanguage.FriendlyNameNative;
            
            // Get Goodwill
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
            _currentResponse = "Thinking...";
            _options.Clear();

            CompressHistoryIfNeeded();

            string response = await WulaFallenEmpire.EventSystem.AI.RimTalkBridge.GetChatCompletion(GetSystemInstruction() + GetToolDescriptions(), _history);

            if (string.IsNullOrEmpty(response))
            {
                _currentResponse = "Error: Could not connect to AI.";
                _isThinking = false;
                return;
            }

            // Check for tool usage (Array or Single Object)
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

            _isThinking = false;
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
            // Normalize to list of objects
            List<string> toolCalls = new List<string>();
            if (json.Trim().StartsWith("{"))
            {
                toolCalls.Add(json);
            }
            else
            {
                // Simple array parsing: split by "}, {"
                // This is fragile but works for simple cases without nested objects in args
                // A better way is to use a proper JSON parser if available, or regex
                // Let's try a simple regex to extract objects
                // Assuming objects are { ... }
                int depth = 0;
                int start = 0;
                for (int i = 0; i < json.Length; i++)
                {
                    if (json[i] == '{')
                    {
                        if (depth == 0) start = i;
                        depth++;
                    }
                    else if (json[i] == '}')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            toolCalls.Add(json.Substring(start, i - start + 1));
                        }
                    }
                }
            }

            StringBuilder combinedResults = new StringBuilder();
            bool hasVisibleResult = false;

            foreach (var callJson in toolCalls)
            {
                string toolName = "";
                string args = "";

                try
                {
                    // Extract tool name and args
                    int toolIndex = callJson.IndexOf("\"tool\"");
                    int argsIndex = callJson.IndexOf("\"args\"");
                    
                    if (toolIndex != -1 && argsIndex != -1)
                    {
                        int toolValueStart = callJson.IndexOf(":", toolIndex) + 1;
                        int toolValueEnd = callJson.IndexOf(",", toolValueStart);
                        if (toolValueEnd == -1) toolValueEnd = callJson.IndexOf("}", toolValueStart); // Handle case where tool is last
                        
                        toolName = callJson.Substring(toolValueStart, toolValueEnd - toolValueStart).Trim().Trim('"');

                        int argsValueStart = callJson.IndexOf(":", argsIndex) + 1;
                        int argsValueEnd = callJson.LastIndexOf("}");
                        args = callJson.Substring(argsValueStart, argsValueEnd - argsValueStart).Trim();
                    }
                }
                catch
                {
                    combinedResults.AppendLine("Error parsing tool request.");
                    continue;
                }

                var tool = _tools.FirstOrDefault(t => t.Name == toolName);
                if (tool != null)
                {
                    string result = tool.Execute(args);
                    
                    if (toolName == "modify_goodwill")
                    {
                        combinedResults.AppendLine($"Tool '{toolName}' Result (Invisible): {result}");
                    }
                    else
                    {
                        combinedResults.AppendLine($"Tool '{toolName}' Result: {result}");
                        hasVisibleResult = true;
                    }
                }
                else
                {
                    combinedResults.AppendLine($"Error: Tool '{toolName}' not found.");
                }
            }

            _history.Add(("AI", json)); // Log the full tool call
            _history.Add(("System", combinedResults.ToString()));
            
            await GenerateResponse();
        }

        private void ParseResponse(string rawResponse)
        {
            var parts = rawResponse.Split(new[] { "OPTIONS:" }, StringSplitOptions.None);
            _currentResponse = parts[0].Trim();
            
            // Only add to history if it's a new response (not restoring from history)
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
                    if (dotIndex != -1 && dotIndex < 4)
                    {
                        opt = opt.Substring(dotIndex + 1).Trim();
                    }
                    _options.Add(opt);
                }
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0, 0, inRect.width, 30), _def.characterName ?? "The Legion");
            Text.Font = GameFont.Small;

            float y = 40;

            Rect chatRect = new Rect(0, y, inRect.width, 300);
            Widgets.DrawMenuSection(chatRect);
            
            string display = _currentResponse;
            if (_isThinking) display = "Thinking...";

            Widgets.Label(chatRect.ContractedBy(10), display);
            y += 310;

            if (!_isThinking && _options.Count > 0)
            {
                foreach (var option in _options)
                {
                    if (Widgets.ButtonText(new Rect(0, y, inRect.width, 30), option))
                    {
                        SelectOption(option);
                    }
                    y += 35;
                }
            }

            y += 10;
            _inputText = Widgets.TextField(new Rect(0, y, inRect.width - 110, 30), _inputText);
            if (Widgets.ButtonText(new Rect(inRect.width - 100, y, 100, 30), "Send"))
            {
                if (!string.IsNullOrEmpty(_inputText))
                {
                    SelectOption(_inputText);
                    _inputText = "";
                }
            }
            
            // Close button
            if (Widgets.ButtonText(new Rect(inRect.width - 120, inRect.height - 40, 120, 30), "CloseButton".Translate()))
            {
                Close();
            }
        }

        private async void SelectOption(string text)
        {
            _history.Add(("User", text));
            await GenerateResponse();
        }
    }
}