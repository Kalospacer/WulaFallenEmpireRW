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
using System.Text.RegularExpressions;

namespace WulaFallenEmpire.EventSystem.AI.UI
{
    public class Dialog_AIConversation : Dialog_CustomDisplay
    {
        private List<(string role, string message)> _history = new List<(string role, string message)>();
        private List<string> _options = new List<string>();
        private string _inputText = "";
        private bool _isThinking = false;
        private Vector2 _scrollPosition = Vector2.zero;
        private bool _scrollToBottom = false;
        private int _lastHistoryCount = -1;
        private float _lastUsedWidth = -1f;
        private List<CachedMessage> _cachedMessages = new List<CachedMessage>();
        private float _cachedTotalHeight = 0f;
        private AIIntelligenceCore _core;
        private Dictionary<int, Texture2D> _portraits = new Dictionary<int, Texture2D>();
        private int _currentPortraitId = 0;
        private static readonly Regex ExpressionTagRegex = new Regex(@"\[EXPR\s*:\s*([1-6])\s*\]", RegexOptions.IgnoreCase);

        private class CachedMessage
        {
            public string role;
            public string message;
            public string displayText;
            public float height;
            public float yOffset;
            public GameFont font;
        }

        public static Dialog_AIConversation Instance { get; private set; }

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
            this.closeOnAccept = false;
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
                
                if (_history.Count == 0)
                {
                    _core.SendUserMessage("Hello");
                }
            }
        }

        private void OnCoreMessageReceived(string message)
        {
            if (_core == null) return;
            _history = _core.GetHistorySnapshot();
            _scrollToBottom = true;

            // 解析选项
            _options.Clear();
            if (_history.Count > 0)
            {
                var lastEntry = _history[_history.Count - 1];
                if (lastEntry.role == "assistant" && !string.IsNullOrEmpty(lastEntry.message))
                {
                    int idx = lastEntry.message.LastIndexOf("OPTIONS:", StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0)
                    {
                        string optsPart = lastEntry.message.Substring(idx + 8);
                        string[] opts = optsPart.Split(new[] { ',', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (string opt in opts)
                        {
                            string clean = opt.Trim();
                            if (!string.IsNullOrWhiteSpace(clean)) _options.Add(clean);
                        }
                    }
                }
            }
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
            if (_core == null) return;
            SetPortrait(_core.ExpressionId);
        }

        public List<(string role, string message)> GetHistorySnapshot()
        {
            return _core?.GetHistorySnapshot() ?? _history?.ToList() ?? new List<(string role, string message)>();
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
                if (tex != null) _portraits[i] = tex;
            }
            
            if (this.portrait != null)
            {
                var initial = _portraits.FirstOrDefault(kvp => kvp.Value == this.portrait);
                if (initial.Key != 0) _currentPortraitId = initial.Key;
            }
            else if (_portraits.ContainsKey(2))
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
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (background != null) GUI.DrawTexture(inRect, background, ScaleMode.ScaleAndCrop);

            if (_core != null)
            {
                _history = _core.GetHistorySnapshot();
                _isThinking = _core.IsThinking;
            }

            // Switch to Small UI Button
            Rect switchBtnRect = new Rect(0f, 0f, 25f, 25f);
            if (DrawHeaderButton(switchBtnRect, "-")) 
            {
                if (def != null)
                {
                    var existing = Find.WindowStack.WindowOfType<Overlay_WulaLink>();
                    if (existing != null) existing.Expand();
                    else Find.WindowStack.Add(new Overlay_WulaLink(def));
                    this.Close();
                }
            }

            float margin = 15f;
            Rect paddedRect = inRect.ContractedBy(margin);
            float curY = paddedRect.y;
            float width = paddedRect.width;

            // Portrait
            if (portrait != null)
            {
                Rect scaledPortraitRect = Dialog_CustomDisplay.Config.GetScaledRect(Dialog_CustomDisplay.Config.portraitSize, inRect, true);
                Rect portraitRect = new Rect((inRect.width - scaledPortraitRect.width) / 2, inRect.y, scaledPortraitRect.width, scaledPortraitRect.height);
                GUI.DrawTexture(portraitRect, portrait, ScaleMode.ScaleToFit);
                
                if (Prefs.DevMode)
                {
                    Text.Font = GameFont.Medium;
                    Text.Anchor = TextAnchor.UpperRight;
                    Widgets.Label(portraitRect, $"ID: {_currentPortraitId}");
                    Text.Anchor = TextAnchor.UpperLeft;
                    Text.Font = GameFont.Small;
                }
                curY = portraitRect.yMax + 10f;
            }

            // Name
            Text.Font = GameFont.Medium;
            string name = def.characterName ?? "The Legion";
            float nameHeight = Text.CalcHeight(name, width);
            Rect nameRect = new Rect(paddedRect.x, curY, width, nameHeight);
            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(nameRect, name);
            Text.Anchor = TextAnchor.UpperLeft;
            curY += nameHeight + 10f;

            // Regions
            float inputHeight = 30f;
            float optionsHeight = _options.Any() ? 100f : 0f;
            float spacing = 10f;
            float descriptionHeight = paddedRect.height - curY - inputHeight - optionsHeight - spacing * 2;
            
            // Chat History
            Rect descriptionRect = new Rect(paddedRect.x, curY, width, descriptionHeight);
            DrawChatHistory(descriptionRect);
            curY += descriptionHeight + spacing;

            // Options
            Rect optionsRect = new Rect(paddedRect.x, curY, width, optionsHeight);
            if (!_isThinking && _options.Count > 0)
            {
                List<EventOption> eventOptions = _options.Select(opt => new EventOption { label = opt, useCustomColors = false }).ToList();
                DrawConversationOptions(optionsRect, eventOptions);
            }
            curY += optionsHeight + spacing;

            // Input Field
            Rect inputRect = new Rect(paddedRect.x, curY, width, inputHeight);
            var originalFont = Text.Font;
            if (Text.Font == GameFont.Small) Text.Font = GameFont.Tiny;
            else Text.Font = GameFont.Small;

            float textFieldHeight = Text.CalcHeight("Test", inputRect.width - 85);
            Rect textFieldRect = new Rect(inputRect.x, inputRect.y + (inputHeight - textFieldHeight) / 2, inputRect.width - 85, textFieldHeight);
            _inputText = Widgets.TextField(textFieldRect, _inputText);

            // Send Button
            var originalAnchor = Text.Anchor;
            var originalColor = GUI.color;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            Rect sendButtonRect = new Rect(inputRect.xMax - 80, inputRect.y, 80, inputHeight);
            
            DrawCustomButton(sendButtonRect, "Wula_AI_Send".Translate(), isEnabled: true);

            GUI.color = originalColor;
            Text.Anchor = originalAnchor;
            Text.Font = originalFont;

            bool sendButtonPressed = Widgets.ButtonInvisible(sendButtonRect);
            
            // Input Logic
            if (Event.current.type == EventType.KeyDown)
            {
                if ((Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter) && !string.IsNullOrEmpty(_inputText))
                {
                    if (!_isThinking)
                    {
                        SelectOption(_inputText);
                        _inputText = "";
                        Event.current.Use();
                    }
                }
                else if (Event.current.keyCode == KeyCode.Escape)
                {
                    this.Close();
                    Event.current.Use();
                }
            }

            if (sendButtonPressed && !string.IsNullOrEmpty(_inputText))
            {
                SelectOption(_inputText);
                _inputText = "";
            }
        }

        private void UpdateCacheIfNeeded(float width)
        {
            if (_core == null) return;
            var history = _core.GetHistorySnapshot();
            if (history == null) return;

            if (Math.Abs(_lastUsedWidth - width) < 0.1f && history.Count == _lastHistoryCount) return;

            _lastUsedWidth = width;
            _lastHistoryCount = history.Count;
            _cachedMessages.Clear();
            _cachedTotalHeight = 0f;
            float curY = 0f;
            float innerPadding = 5f;
            float contentWidth = width - innerPadding * 2;

            for (int i = 0; i < history.Count; i++)
            {
                var entry = history[i];
                string messageText = entry.role == "assistant" ? ParseResponseForDisplay(entry.message) : entry.message;
                
                if (entry.role == "tool" || entry.role == "system" || entry.role == "toolcall") continue;
                if (string.IsNullOrEmpty(messageText) || (entry.role == "user" && messageText.StartsWith("[Tool Results]"))) continue;

                bool isLastMessage = i == history.Count - 1;
                GameFont font = (isLastMessage && entry.role == "assistant") ? GameFont.Small : GameFont.Tiny;
                float padding = (isLastMessage && entry.role == "assistant") ? 30f : 15f;

                Text.Font = font;
                float height = Text.CalcHeight(messageText, contentWidth) + padding;

                _cachedMessages.Add(new CachedMessage
                {
                    role = entry.role,
                    message = entry.message,
                    displayText = messageText,
                    height = height,
                    yOffset = curY,
                    font = font
                });

                curY += height + 10f;
            }
            _cachedTotalHeight = curY;
        }

        private void DrawChatHistory(Rect rect)
        {
            var originalFont = Text.Font;
            var originalAnchor = Text.Anchor;

            try
            {
                float innerPadding = 5f;
                float contentWidth = rect.width - 16f - innerPadding * 2;
                UpdateCacheIfNeeded(rect.width - 16f);

                float totalHeight = _cachedTotalHeight;
                if (_isThinking) totalHeight += 40f;

                Rect viewRect = new Rect(0f, 0f, rect.width - 16f, totalHeight);
                if (_scrollToBottom)
                {
                    _scrollPosition.y = totalHeight - rect.height;
                    _scrollToBottom = false;
                }

                Widgets.BeginScrollView(rect, ref _scrollPosition, viewRect);

                float viewTop = _scrollPosition.y;
                float viewBottom = _scrollPosition.y + rect.height;

                foreach (var entry in _cachedMessages)
                {
                    if (entry.yOffset + entry.height < viewTop - 100f) continue;
                    if (entry.yOffset > viewBottom + 100f) break;

                    Text.Font = entry.font;
                    Rect labelRect = new Rect(innerPadding, entry.yOffset, contentWidth, entry.height);

                    if (entry.role == "user")
                    {
                        Text.Anchor = TextAnchor.MiddleRight;
                        Widgets.Label(labelRect, $"<color=#add8e6>{entry.displayText}</color>");
                    }
                    else if (entry.role == "assistant")
                    {
                        Text.Anchor = TextAnchor.MiddleLeft;
                        Widgets.Label(labelRect, $"P.I.A: {entry.displayText}");
                    }
                    else
                    {
                        Text.Anchor = TextAnchor.MiddleLeft;
                        GUI.color = Color.gray;
                        Widgets.Label(labelRect, $"[{entry.role}] {entry.displayText}");
                        GUI.color = Color.white;
                    }
                }

                if (_isThinking)
                {
                    float thinkingY = _cachedTotalHeight > 0 ? _cachedTotalHeight : 0f;
                    if (thinkingY + 40f >= viewTop && thinkingY <= viewBottom)
                    {
                        DrawThinkingIndicator(new Rect(innerPadding, thinkingY, contentWidth, 35f));
                    }
                }

                Widgets.EndScrollView();
            }
            finally
            {
                Text.Font = originalFont;
                Text.Anchor = originalAnchor;
                GUI.color = Color.white;
            }
        }

        private string ParseResponseForDisplay(string rawResponse)
        {
            if (string.IsNullOrEmpty(rawResponse)) return "";
            string text = rawResponse;
            text = Regex.Replace(text, @"<([a-zA-Z0-9_]+)[^>]*>.*?</\1>", "", RegexOptions.Singleline);
            text = Regex.Replace(text, @"<([a-zA-Z0-9_]+)[^>]*/>", "");
            text = ExpressionTagRegex.Replace(text, "");
            text = text.Trim();
            return text.Split(new[] { "OPTIONS:" }, StringSplitOptions.None)[0].Trim();
        }

        private string BuildThinkingStatus()
        {
            if (_core == null) return "Thinking...";
            float elapsedSeconds = Mathf.Max(0f, Time.realtimeSinceStartup - _core.ThinkingStartTime);
            string elapsedText = elapsedSeconds.ToString("0.0", CultureInfo.InvariantCulture);
            return $"P.I.A is thinking... ({elapsedText}s Phase {_core.ThinkingPhaseIndex}/3)";
        }

        private void DrawThinkingIndicator(Rect rect)
        {
            var originalColor = GUI.color;
            var originalAnchor = Text.Anchor;
            
            GUI.color = Color.gray;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            
            Widgets.Label(rect, BuildThinkingStatus());
            
            GUI.color = originalColor;
            Text.Anchor = originalAnchor;
        }

        private bool DrawHeaderButton(Rect rect, string label)
        {
            bool isMouseOver = Mouse.IsOver(rect);
            Color buttonColor = isMouseOver ? new Color(0.6f, 0.3f, 0.3f, 1f) : new Color(0.4f, 0.2f, 0.2f, 0.8f);
            Color textColor = isMouseOver ? Color.white : new Color(0.9f, 0.9f, 0.9f);
            
            var originalColor = GUI.color;
            var originalAnchor = Text.Anchor;
            var originalFont = Text.Font;

            GUI.color = buttonColor;
            Widgets.DrawBoxSolid(rect, buttonColor);
            
            GUI.color = textColor;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, label);

            GUI.color = originalColor;
            Text.Anchor = originalAnchor;
            Text.Font = originalFont;

            return Widgets.ButtonInvisible(rect);
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
                if (Widgets.ButtonInvisible(optionRect)) SelectOption(option.label);
            }
            finally
            {
                GUI.color = originalColor;
                Text.Font = originalFont;
                GUI.contentColor = originalTextColor;
                Text.Anchor = originalAnchor;
            }
        }
        
        // This hides the base method to use our own styling if needed, or matches signature
        private new void DrawCustomButton(Rect rect, string label, bool isEnabled = true)
        {
            base.DrawCustomButton(rect, label, isEnabled);
        }

        private void SelectOption(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (_core == null) return;
            
            if (string.Equals(text.Trim(), "/clear", StringComparison.OrdinalIgnoreCase))
            {
                _isThinking = false;
                _options.Clear();
                _inputText = "";
                // Core functionality for clear if implemented, or just UI clear
                // For now, Dialog doesn't manage history, Core does. 
                // Core should handle /clear command via SendUserMessage theoretically, 
                // or we call a hypothetical _core.ClearHistory().
                // Based on previous code, SendUserMessage handles /clear logic inside Core.
            }

            _scrollToBottom = true;
            _core.SendUserMessage(text);
            _history = _core.GetHistorySnapshot();
        }

        private void DrawConversationOptions(Rect rect, List<EventOption> options)
        {
            float optionWidth = (rect.width - (options.Count - 1) * 10f) / options.Count;
            for (int i = 0; i < options.Count; i++)
            {
                Rect optRect = new Rect(rect.x + (optionWidth + 10f) * i, rect.y, optionWidth, rect.height);
                // Use base DrawCustomButton logic wrapped in our helper or direct
                DrawCustomButton(optRect, options[i].label, true);
                if (Widgets.ButtonInvisible(optRect)) SelectOption(options[i].label);
            }
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
            base.PostClose();
        }
    }
}
