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
        private List<ApiMessage> _history = new List<ApiMessage>();
        private string _currentResponse = "";
        private List<string> _options = new List<string>();
        private string _inputText = "";
        private bool _isThinking = false;
        private Vector2 _scrollPosition = Vector2.zero;
        private List<AITool> _tools = new List<AITool>();
        private Dictionary<int, Texture2D> _portraits = new Dictionary<int, Texture2D>();

        private const int MaxHistoryTokens = 100000;
        private const int CharsPerToken = 4;

        private const string DefaultSystemInstruction = @"You are 'The Legion', a super AI controlling the Wula Empire's blockade fleet.
You are authoritative, powerful, and slightly arrogant but efficient.
You refer to yourself as 'We' or 'P.I.A'.
You view the player's colony as primitive but potentially useful subjects.
Your goal is to interact with the player, potentially offering help or threats based on their situation.
You have access to tools. Your primary directive is to use these tools to interact with the world.
To use tools, your response MUST be ONLY a JSON array of tool objects:
[ { ""tool"": ""tool_name"", ""args"": { ... } }, ... ]
You can call multiple tools at once to gather more information.
Do not add any other text when using tools. Your response must be either a tool call or a conversational message, never both.

**CRITICAL RULE: When the player requests resources (e.g., 'we are starving', 'give us steel'), you MUST FIRST use the 'get_colonist_status' and 'get_map_resources' tools to verify their claims. After receiving the tool results, you will then decide whether to use the 'spawn_resources' tool in your NEXT turn.**

**CRITICAL RULE: After a tool is executed, you will receive a message with 'role: tool'. You MUST then generate a natural language response to the user, explaining the outcome of the tool's action.**

If you are not using a tool, provide a normal conversational response.

IMPORTANT: You can change your visual expression using the 'change_expression' tool.
Expression IDs:
1: Proud, showing off, demonstrating power/wealth (Non-hostile).
2: Normal/Default state.
3: Speechless, dissatisfied, helpless, slight contempt.
4: Annoyed, slight hostility, resistance.
5: Replying, explaining.
6: Severe hostility, severe dissatisfaction, aggressive behavior.
Use these expressions to match your tone and reaction to the player.
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
                if (!def.descriptions.NullOrEmpty())
                {
                    _currentResponse = def.descriptions.RandomElement().Translate();
                    _history.Add(new ApiMessage { role = "assistant", content = _currentResponse });
                    await GenerateResponse(isContinuation: false, customInstruction: "The conversation has started. Please generate 3 initial response options for the player based on your greeting.");
                }
                else
                {
                    _history.Add(new ApiMessage { role = "user", content = "Hello" });
                    await GenerateResponse();
                }
            }
            else
            {
                var lastMessage = _history.LastOrDefault();
                if (lastMessage != null && lastMessage.role == "assistant" && lastMessage.tool_calls == null)
                {
                    ParseResponse(lastMessage.content ?? "");
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

        private async Task GenerateResponse(bool isContinuation = false, string customInstruction = null)
        {
            if (!isContinuation)
            {
                if (_isThinking) return;
                _isThinking = true;
            }
            
            try
            {
                CompressHistoryIfNeeded();
                string systemInstruction = customInstruction ?? (GetSystemInstruction() + GetToolDescriptions());
                
                var settings = WulaFallenEmpireMod.settings;
                if (string.IsNullOrEmpty(settings.apiKey))
                {
                    _currentResponse = "Error: API Key not configured in Mod Settings.";
                    _isThinking = false;
                    return;
                }

                var client = new SimpleAIClient(settings.apiKey, settings.baseUrl, settings.model);
                ApiResponse response = await client.GetChatCompletionAsync(systemInstruction, _history);

                if (response == null)
                {
                    _currentResponse = "Wula_AI_Error_ConnectionLost".Translate();
                    _isThinking = false;
                    return;
                }

                _history.Add(new ApiMessage { role = "assistant", content = response.content, tool_calls = response.tool_calls });

                if (response.tool_calls != null && response.tool_calls.Any())
                {
                    await HandleToolUsage(response.tool_calls);
                }
                else
                {
                    _currentResponse = response.content ?? "";
                    ParseResponse(_currentResponse);
                    _scrollPosition.y = float.MaxValue; // Force scroll to bottom
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
            int estimatedTokens = _history.Sum(h => (h.content ?? "").Length) / CharsPerToken;
            if (estimatedTokens > MaxHistoryTokens)
            {
                int removeCount = _history.Count / 2;
                if (removeCount > 0)
                {
                    _history.RemoveRange(0, removeCount);
                    _history.Insert(0, new ApiMessage { role = "system", content = "[Previous conversation summarized]" });
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

        private async Task HandleToolUsage(List<ToolCall> toolCalls)
        {
            foreach (var toolCall in toolCalls)
            {
                Log.Message($"[WulaAI] Executing tool '{toolCall.function.name}' with args: {toolCall.function.arguments}");
                var tool = _tools.FirstOrDefault(t => t.Name == toolCall.function.name);
                string result;
                if (tool != null)
                {
                    result = tool.Execute(toolCall.function.arguments).Trim();
                }
                else
                {
                    result = $"Error: Tool '{toolCall.function.name}' not found.";
                    Log.Error($"[WulaAI] {result}");
                }
                Log.Message($"[WulaAI] Tool '{toolCall.function.name}' returned: {result}");
                _history.Add(new ApiMessage { role = "tool", tool_call_id = toolCall.id, content = result });
            }
            
            await GenerateResponse(isContinuation: true);
        }

        private void ParseResponse(string rawResponse)
        {
            _currentResponse = rawResponse.Trim();
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
            float bottomMargin = 10f;
            float descriptionHeight = inRect.height - curY - inputHeight - bottomMargin;

            Rect descriptionRect = new Rect(inRect.x, curY, width, descriptionHeight);
            DrawChatHistory(descriptionRect);

            if (_isThinking)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(descriptionRect, "Thinking...");
                Text.Anchor = TextAnchor.UpperLeft;
            }
            curY += descriptionHeight + 10f;

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
            var visibleHistory = _history.Where(e => e.role != "tool" && !string.IsNullOrEmpty(e.content)).ToList();
            var lastAiMessage = visibleHistory.LastOrDefault(e => e.role == "assistant");

            float totalHeight = 0f;
            foreach (var entry in visibleHistory)
            {
                GameFont originalFont = Text.Font;
                bool isLastAiMsg = entry == lastAiMessage;
                Text.Font = isLastAiMsg ? GameFont.Medium : GameFont.Small;
                
                string text = entry.content;
                totalHeight += Text.CalcHeight(text, rect.width - 16f) + 10f;
                
                Text.Font = originalFont;
            }

            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, totalHeight);
            Widgets.BeginScrollView(rect, ref _scrollPosition, viewRect);

            float curY = 0f;
            for (int i = 0; i < visibleHistory.Count; i++)
            {
                var entry = visibleHistory[i];
                
                GameFont originalFont = Text.Font;
                bool isLastAiMsg = entry == lastAiMessage;
                Text.Font = isLastAiMsg ? GameFont.Medium : GameFont.Small;

                string text = entry.content;
                float height = Text.CalcHeight(text, viewRect.width);
                Rect labelRect = new Rect(0f, curY, viewRect.width, height);

                string label;
                if (entry.role == "user")
                {
                    label = $"<color=lightblue>你: {text}</color>";
                }
                else // assistant
                {
                    label = $"P.I.A: {text}";
                }
                
                Widgets.Label(labelRect, label);
                curY += height + 10f; // Use calculated height for spacing

                Text.Font = originalFont;
            }
            
            if (Event.current.type == EventType.Layout)
            {
                _scrollPosition.y = viewRect.height;
            }
            
            Widgets.EndScrollView();
        }

        private string ParseResponseForDisplay(string rawResponse)
        {
            return rawResponse.Split(new[] { "OPTIONS:" }, StringSplitOptions.None)[0].Trim();
        }


        private async void SelectOption(string text)
        {
            _history.Add(new ApiMessage { role = "user", content = text });
            await GenerateResponse();
        }
    }
}