using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RimWorld;
using UnityEngine;
using Verse;
using WulaFallenEmpire;
using WulaFallenEmpire.EventSystem.AI;
using WulaFallenEmpire.EventSystem.AI.Utils;
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
        private readonly Dictionary<int, bool> _traceExpandedByAssistantIndex = new Dictionary<int, bool>();

        private class CachedMessage
        {
            public string role;
            public string message;
            public string displayText;
            public float height;
            public float yOffset;
            public GameFont font;
            public bool isTrace;
            public int traceKey;
            public string traceHeader;
            public List<string> traceLines;
            public bool traceExpanded;
            public float traceHeaderHeight;
            public bool isImage;
            public Texture2D imageTex;
            public string imageFile;
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
            var previousHistory = _history;
            _history = _core.GetHistorySnapshot();
            _scrollToBottom = true;

            // 流式更新是「原地替换最后一条 assistant 消息内容」，历史条目数不变。
            // 那种情况只重算最后一条并平移总高度（codex commit_tick 思路：增量提交而非整表重建）；
            // 结构性变化（新增/删除/重排）仍然全量重建缓存。
            bool streamingOnly = previousHistory != null
                && previousHistory.Count == _history.Count
                && _history.Count > 0
                && previousHistory.Take(_history.Count - 1).SequenceEqual(_history.Take(_history.Count - 1))
                && _history[_history.Count - 1].role == "assistant";

            if (streamingOnly)
            {
                UpdateStreamingTail();
            }
            else
            {
                _lastHistoryCount = -1;
                _lastUsedWidth = -1f;
            }

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

        /// <summary>
        /// Streaming path: recompute only the last assistant row's height and shift the total; every
        /// earlier cached row stays untouched. Falls back to a full rebuild when the tail row is not a
        /// cached assistant message.
        /// </summary>
        private void UpdateStreamingTail()
        {
            if (_cachedMessages.Count == 0)
            {
                _lastHistoryCount = -1;
                return;
            }
            var last = _cachedMessages[_cachedMessages.Count - 1];
            if (last.role != "assistant" || last.isTrace)
            {
                _lastHistoryCount = -1;
                return;
            }
            string newText = MarkdownRenderer.ToRichText(ParseResponseForDisplay(_history[_history.Count - 1].message));
            Text.Font = last.font;
            float newHeight = Text.CalcHeight(newText, _lastUsedWidth - 16f - 10f) + 30f;
            _cachedTotalHeight += newHeight - last.height;
            last.height = newHeight;
            last.displayText = newText;
            last.message = _history[_history.Count - 1].message;
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

            // Switch to Small UI Button (three-state: bar ⇄ small ⇄ dialog)
            Rect switchBtnRect = new Rect(0f, 0f, 25f, 25f);
            if (DrawHeaderButton(switchBtnRect, "-"))
            {
                if (def != null && Find.WindowStack != null)
                {
                    var existing = Find.WindowStack.WindowOfType<Overlay_WulaLink>();
                    if (existing != null)
                    {
                        existing.Expand();
                    }
                    else
                    {
                        // Restore the small window where it was before the round trip, if we have
                        // a position saved from the last overlay close.
                        var overlay = new Overlay_WulaLink(def);
                        if (_core != null && _core.TryGetSavedOverlayPosition(out float px, out float py))
                        {
                            overlay.SetInitialPosition(px, py);
                        }
                        Find.WindowStack.Add(overlay);
                    }
                    this.Close();
                }
            }

            // Personality Prompt Button
            Rect personalityBtnRect = new Rect(0f, 30f, 25f, 25f);
            if (DrawHeaderButton(personalityBtnRect, "P"))
            {
                Find.WindowStack?.Add(new Dialog_ExtraPersonalityPrompt());
            }

            bool aiEnabled = _core?.IsAIEnabled == true;
            Rect aiPowerBtnRect = new Rect(30f, 0f, 35f, 25f);
            if (DrawHeaderButton(aiPowerBtnRect, aiEnabled ? "ON" : "OFF"))
            {
                _core?.SetAIEnabled(!aiEnabled);
                aiEnabled = _core?.IsAIEnabled == true;
            }
            TooltipHandler.TipRegion(aiPowerBtnRect, "Wula_AISettings_SaveAIEnabledDesc".Translate());

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

            // Send Button — becomes a Stop button while the agent loop is running.
            var originalAnchor = Text.Anchor;
            var originalColor = GUI.color;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            Rect sendButtonRect = new Rect(inputRect.xMax - 80, inputRect.y, 80, inputHeight);
            bool buttonEnabled = _isThinking || aiEnabled;
            DrawCustomButton(sendButtonRect, _isThinking ? "Wula_AI_Stop".Translate() : "Wula_AI_Send".Translate(), isEnabled: buttonEnabled);

            GUI.color = originalColor;
            Text.Anchor = originalAnchor;
            Text.Font = originalFont;

            bool sendButtonPressed = buttonEnabled && Widgets.ButtonInvisible(sendButtonRect);
            if (sendButtonPressed && _isThinking)
            {
                _core?.CancelCurrentRequest();
                sendButtonPressed = false;
            }
            
            // Input Logic
            if (Event.current.type == EventType.KeyDown)
            {
                if ((Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter) && !string.IsNullOrEmpty(_inputText))
                {
                    if (aiEnabled && !_isThinking)
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
            if (_lastHistoryCount >= 0 && history.Count < _lastHistoryCount)
            {
                _traceExpandedByAssistantIndex.Clear();
            }
            _lastHistoryCount = history.Count;
            _cachedMessages.Clear();
            _cachedTotalHeight = 0f;
            float curY = 0f;
            float innerPadding = 5f;
            float contentWidth = width - innerPadding * 2;
            var toolcallBuffer = new List<string>();
            var toolResultBuffer = new List<string>();
            var traceNoteBuffer = new List<string>();
            bool showDirectReplyTrace = WulaFallenEmpireAIMod.settings?.showReactTraceInUI == true;
            bool traceEnabled = true;

            for (int i = 0; i < history.Count; i++)
            {
                var entry = history[i];
                if (entry.role == "user")
                {
                    toolcallBuffer.Clear();
                    toolResultBuffer.Clear();
                    traceNoteBuffer.Clear();
                }

                if (entry.role == "toolcall")
                {
                    if (traceEnabled)
                    {
                        toolcallBuffer.Add(entry.message ?? "");
                    }
                    continue;
                }

                if (entry.role == "tool")
                {
                    if (traceEnabled)
                    {
                        toolResultBuffer.Add(entry.message ?? "");
                    }
                    continue;
                }

                if (entry.role == "trace")
                {
                    if (traceEnabled)
                    {
                        traceNoteBuffer.Add(entry.message ?? "");
                    }
                    continue;
                }

                // 图片行：解码引用并渲染成缩略图，塞进对话流（不动工具 trace 缓冲）。
                if (entry.role == "image")
                {
                    if (AIImageStore.TryParseImageRef(entry.message, out var imgFile, out _, out _))
                    {
                        Texture2D tex = AIImageStore.LoadImageTexture(imgFile);
                        if (tex != null)
                        {
                            float maxW = Mathf.Min(contentWidth, 512f);
                            float maxH = 384f;
                            float scale = Mathf.Min(maxW / tex.width, maxH / tex.height, 1f);
                            float imgW = tex.width * scale;
                            float imgH = tex.height * scale;
                            _cachedMessages.Add(new CachedMessage
                            {
                                role = "image",
                                message = entry.message,
                                displayText = "",
                                height = imgH,
                                yOffset = curY,
                                font = GameFont.Tiny,
                                isImage = true,
                                imageTex = tex,
                                imageFile = imgFile
                            });
                            curY += imgH + 10f;
                        }
                    }
                    continue;
                }

                string messageText = entry.role == "assistant"
                    ? MarkdownRenderer.ToRichText(ParseResponseForDisplay(entry.message))
                    : AIIntelligenceCore.StripContextInfo(entry.message);

                if (entry.role == "system") continue;
                // Hide auto-commentary system messages (user-side) from display
                if (entry.role == "user" && entry.message.Contains("[AUTO_COMMENTARY]")) continue;
                if (entry.role == "assistant" && traceEnabled && toolcallBuffer.Count > 0)
                {
                    var traceLines = BuildTraceLines(toolcallBuffer, toolResultBuffer, traceNoteBuffer);
                    if (traceLines.Count > 0)
                    {
                        int traceKey = i;
                        bool expanded = _traceExpandedByAssistantIndex.TryGetValue(traceKey, out bool saved) && saved;
                        string header = GetTraceHeader(traceKey, false);

                        Text.Font = GameFont.Tiny;
                        float tracePadding = 8f;
                        float headerWidth = Mathf.Max(10f, contentWidth - tracePadding * 2f);
                        string headerLine = $"{(expanded ? "v" : ">")} {header}";
                        float headerHeight = Text.CalcHeight(headerLine, headerWidth) + 10f;
                        float linesHeight = 0f;
                        if (expanded)
                        {
                            float lineWidth = Mathf.Max(10f, contentWidth - tracePadding * 2f);
                            foreach (string line in traceLines)
                            {
                                linesHeight += Text.CalcHeight(line, lineWidth) + 2f;
                            }
                            linesHeight += 8f;
                        }
                        float traceHeight = headerHeight + linesHeight;

                        _cachedMessages.Add(new CachedMessage
                        {
                            role = "trace",
                            message = "",
                            displayText = "",
                            height = traceHeight,
                            yOffset = curY,
                            font = GameFont.Tiny,
                            isTrace = true,
                            traceKey = traceKey,
                            traceHeader = header,
                            traceLines = traceLines,
                            traceExpanded = expanded,
                            traceHeaderHeight = headerHeight
                        });
                        curY += traceHeight + 10f;
                    }

                    toolcallBuffer.Clear();
                    toolResultBuffer.Clear();
                    traceNoteBuffer.Clear();
                }
                else if (entry.role == "assistant" && showDirectReplyTrace && toolcallBuffer.Count == 0)
                {
                    var traceLines = BuildTraceLines(toolcallBuffer, toolResultBuffer, traceNoteBuffer);
                    if (traceLines.Count == 0)
                    {
                    traceLines.Add("无工具调用，直接回复");
                }
                int traceKey = i;
                bool expanded = _traceExpandedByAssistantIndex.TryGetValue(traceKey, out bool saved) && saved;
                string header = GetTraceHeader(traceKey, false);

                    Text.Font = GameFont.Tiny;
                    float tracePadding = 8f;
                    float headerWidth = Mathf.Max(10f, contentWidth - tracePadding * 2f);
                    string headerLine = $"{(expanded ? "v" : ">")} {header}";
                    float headerHeight = Text.CalcHeight(headerLine, headerWidth) + 10f;
                    float linesHeight = 0f;
                    if (expanded)
                    {
                        float lineWidth = Mathf.Max(10f, contentWidth - tracePadding * 2f);
                        foreach (string line in traceLines)
                        {
                            linesHeight += Text.CalcHeight(line, lineWidth) + 2f;
                        }
                        linesHeight += 8f;
                    }
                    float traceHeight = headerHeight + linesHeight;

                    _cachedMessages.Add(new CachedMessage
                    {
                        role = "trace",
                        message = "",
                        displayText = "",
                        height = traceHeight,
                        yOffset = curY,
                        font = GameFont.Tiny,
                        isTrace = true,
                        traceKey = traceKey,
                        traceHeader = header,
                        traceLines = traceLines,
                        traceExpanded = expanded,
                        traceHeaderHeight = headerHeight
                    });
                    curY += traceHeight + 10f;
                    traceNoteBuffer.Clear();
                }
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
                bool showDirectReplyTrace = WulaFallenEmpireAIMod.settings?.showReactTraceInUI == true;
                CachedMessage liveTraceEntry = null;
                float liveTraceHeight = 0f;
                var liveLines = _isThinking ? BuildLiveTraceLines() : new List<string>();
                if (_isThinking && (showDirectReplyTrace || liveLines.Count > 0))
                {
                    if (liveLines.Count == 0)
                    {
                        liveLines.Add("思考中…");
                    }
                    int traceKey = -1;
                    bool expanded = _traceExpandedByAssistantIndex.TryGetValue(traceKey, out bool saved) ? saved : true;
                    string header = GetTraceHeader(traceKey, true);

                    Text.Font = GameFont.Tiny;
                    float tracePadding = 8f;
                    float headerWidth = Mathf.Max(10f, contentWidth - tracePadding * 2f);
                    string headerLine = $"{(expanded ? "v" : ">")} {header}";
                    float headerHeight = Text.CalcHeight(headerLine, headerWidth) + 10f;
                    float linesHeight = 0f;
                    if (expanded)
                    {
                        float lineWidth = Mathf.Max(10f, contentWidth - tracePadding * 2f);
                        foreach (string line in liveLines)
                        {
                            linesHeight += Text.CalcHeight(line, lineWidth) + 2f;
                        }
                        linesHeight += 8f;
                    }
                    float traceHeight = headerHeight + linesHeight;
                    liveTraceHeight = traceHeight + 10f;
                    liveTraceEntry = new CachedMessage
                    {
                        role = "trace",
                        message = "",
                        displayText = "",
                        height = traceHeight,
                        yOffset = 0f,
                        font = GameFont.Tiny,
                        isTrace = true,
                        traceKey = traceKey,
                        traceHeader = header,
                        traceLines = liveLines,
                        traceExpanded = expanded,
                        traceHeaderHeight = headerHeight
                    };
                }

                float totalHeight = _cachedTotalHeight;
                if (_isThinking)
                {
                    totalHeight += liveTraceEntry != null ? liveTraceHeight : 40f;
                }

                // While streaming, the assistant draft is already the last cached message and is
                // counted into _cachedTotalHeight, so drawing the thinking indicator at
                // _cachedTotalHeight would put it *below* the reply. Pull it above the draft:
                // reserve the block at the draft's yOffset and push the draft below it.
                float reserveHeight = liveTraceEntry != null ? liveTraceHeight : 40f;
                CachedMessage streamingDraft = null;
                if (_isThinking && _cachedMessages.Count > 0)
                {
                    var last = _cachedMessages[_cachedMessages.Count - 1];
                    if (last.role == "assistant")
                    {
                        streamingDraft = last;
                    }
                }
                float thinkingY = streamingDraft != null ? streamingDraft.yOffset : _cachedTotalHeight;

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
                    // The streaming draft is drawn below the reserved thinking block (above it).
                    float entryY = ReferenceEquals(entry, streamingDraft) ? entry.yOffset + reserveHeight : entry.yOffset;
                    Rect labelRect = new Rect(innerPadding, entryY, contentWidth, entry.height);

                    if (entry.isTrace)
                    {
                        DrawReactTracePanel(labelRect, entry);
                    }
                    else if (entry.isImage)
                    {
                        float iw = Mathf.Min(contentWidth, entry.imageTex != null ? entry.imageTex.width : contentWidth);
                        Rect imgRect = new Rect(innerPadding, entry.yOffset, iw, entry.height);
                        GUI.color = new Color(1f, 1f, 1f, 1f);
                        Widgets.DrawBox(imgRect, 1);
                        if (entry.imageTex != null)
                        {
                            GUI.DrawTexture(imgRect.ContractedBy(1), entry.imageTex, ScaleMode.ScaleToFit);
                        }
                        GUI.color = Color.white;
                    }
                    else if (entry.role == "user")
                    {
                        Text.Anchor = TextAnchor.MiddleRight;
                        Widgets.Label(labelRect, $"<color=#add8e6>{entry.displayText}</color>");
                    }
                    else if (entry.role == "assistant")
                    {
                        Text.Anchor = TextAnchor.MiddleLeft;
                        GUI.contentColor = Color.white;
                        Widgets.Label(labelRect, $"<color=#e8e8e8>P.I.A:</color> {entry.displayText}");
                        GUI.contentColor = Color.white;
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
                    if (liveTraceEntry != null)
                    {
                        if (thinkingY + liveTraceEntry.height >= viewTop && thinkingY <= viewBottom)
                        {
                            Rect traceRect = new Rect(innerPadding, thinkingY, contentWidth, liveTraceEntry.height);
                            DrawReactTracePanel(traceRect, liveTraceEntry);
                        }
                    }
                    else
                    {
                        float indicatorHeight = (_core != null && !string.IsNullOrWhiteSpace(_core.LatestThought)) ? 55f : 35f;
                        if (thinkingY + indicatorHeight >= viewTop && thinkingY <= viewBottom)
                        {
                            DrawThinkingIndicator(new Rect(innerPadding, thinkingY, contentWidth, indicatorHeight));
                        }
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
            text = ExpressionTagRegex.Replace(text, "");
            text = text.Trim();
            return text.Split(new[] { "OPTIONS:" }, StringSplitOptions.None)[0].Trim();
        }

        private List<string> BuildTraceLines(List<string> toolcallBuffer, List<string> toolResultBuffer, List<string> traceNotes)
        {
            var lines = new List<string>();
            bool hasToolCalls = toolcallBuffer != null && toolcallBuffer.Count > 0;

            int stepIndex = 0;
            int maxSteps = Math.Max(toolcallBuffer?.Count ?? 0, toolResultBuffer?.Count ?? 0);
            for (int i = 0; i < maxSteps; i++)
            {
                bool anyStepContent = false;
                stepIndex++;

                if (hasToolCalls && i < toolcallBuffer.Count)
                {
                    string callText = TrimForDisplay(toolcallBuffer[i], 160);
                    if (!string.IsNullOrWhiteSpace(callText))
                    {
                        lines.Add($"步骤 {stepIndex} · 调用 {callText}");
                        anyStepContent = true;
                    }
                }

                if (toolResultBuffer != null && i < toolResultBuffer.Count)
                {
                    foreach (string resultLine in ExtractToolResultLines(toolResultBuffer[i], 4))
                    {
                        lines.Add($"步骤 {stepIndex} · 结果 {resultLine}");
                        anyStepContent = true;
                    }
                }

                if (!anyStepContent)
                {
                    stepIndex--;
                }
            }

            if (traceNotes != null && traceNotes.Count > 0)
            {
                foreach (string note in traceNotes)
                {
                    string trimmed = (note ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(trimmed)) continue;
                    lines.Add($"状态 · {TrimForDisplay(trimmed, 220)}");
                }
            }

            return lines;
        }

        private static List<string> ExtractToolResultLines(string toolMessage, int maxLines)
        {
            var lines = new List<string>();
            if (string.IsNullOrWhiteSpace(toolMessage)) return lines;

            string[] rawLines = toolMessage.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string raw in rawLines)
            {
                string line = raw.Trim();
                if (string.IsNullOrEmpty(line)) continue;
                if (line.StartsWith("[Tool Results]", StringComparison.OrdinalIgnoreCase)) continue;
                if (line.StartsWith("ToolRunner", StringComparison.OrdinalIgnoreCase)) continue;
                if (!line.StartsWith("Tool '", StringComparison.OrdinalIgnoreCase) &&
                    !line.StartsWith("Query Result:", StringComparison.OrdinalIgnoreCase) &&
                    !line.StartsWith("Error:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                lines.Add(TrimForDisplay(line, 200));
                if (lines.Count >= maxLines) break;
            }

            return lines;
        }

        private static string TrimForDisplay(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxChars) return text ?? "";
            return text.Substring(0, maxChars) + "...";
        }

        private List<string> BuildLiveTraceLines()
        {
            if (_core == null) return new List<string>();
            var history = _core.GetHistorySnapshot();
            if (history == null || history.Count == 0) return new List<string>();

            int lastUserIndex = -1;
            for (int i = history.Count - 1; i >= 0; i--)
            {
                if (history[i].role == "user")
                {
                    lastUserIndex = i;
                    break;
                }
            }
            if (lastUserIndex == -1) return new List<string>();

            var toolcallBuffer = new List<string>();
            var toolResultBuffer = new List<string>();
            var traceNoteBuffer = new List<string>();
            for (int i = lastUserIndex + 1; i < history.Count; i++)
            {
                var entry = history[i];
                if (entry.role == "toolcall")
                {
                    toolcallBuffer.Add(entry.message ?? "");
                }
                else if (entry.role == "tool")
                {
                    toolResultBuffer.Add(entry.message ?? "");
                }
                else if (entry.role == "trace")
                {
                    traceNoteBuffer.Add(entry.message ?? "");
                }
            }

            return BuildTraceLines(toolcallBuffer, toolResultBuffer, traceNoteBuffer);
        }

        private string GetTraceHeader(int traceKey, bool isLive)
        {
            if (isLive)
            {
                return BuildReactTraceHeader(true, 0f);
            }
            // Read the duration live rather than caching it: the stamp lands only after the assistant
            // row (and its header) is built, so a cached header would never pick up the real value.
            return BuildReactTraceHeader(false, GetThinkingDurationAt(traceKey));
        }

        private float GetThinkingDurationAt(int index)
        {
            return _core?.GetThinkingDurationAtIndex(index) ?? 0f;
        }

        private string BuildReactTraceHeader(bool isLive, float durationSeconds)
        {
            if (!isLive)
            {
                return $"已思考 (用时 {durationSeconds.ToString("0.0", CultureInfo.InvariantCulture)}s)";
            }
            float elapsed = _core != null ? (float)_core.ThinkingElapsedSeconds : 0f;
            string elapsedText = elapsed > 0f ? elapsed.ToString("0.0", CultureInfo.InvariantCulture) : "0.0";
            return $"思考中 (用时 {elapsedText}s)";
        }

        private void DrawReactTracePanel(Rect rect, CachedMessage traceEntry)
        {
            var originalColor = GUI.color;
            var originalFont = Text.Font;
            var originalAnchor = Text.Anchor;

            float padding = 8f;
            Rect headerRect = new Rect(rect.x, rect.y, rect.width, traceEntry.traceHeaderHeight);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            bool isLive = traceEntry.traceKey == -1;
            string headerLine = $"{(traceEntry.traceExpanded ? "v" : ">")} {GetTraceHeader(traceEntry.traceKey, isLive)}";
            Widgets.Label(headerRect.ContractedBy(padding, 4f), headerLine);

            if (Widgets.ButtonInvisible(headerRect))
            {
                traceEntry.traceExpanded = !traceEntry.traceExpanded;
                _traceExpandedByAssistantIndex[traceEntry.traceKey] = traceEntry.traceExpanded;
                _lastHistoryCount = -1;
                _lastUsedWidth = -1f;
            }

            if (traceEntry.traceExpanded && traceEntry.traceLines != null && traceEntry.traceLines.Count > 0)
            {
                float y = headerRect.yMax + 6f;
                foreach (string line in traceEntry.traceLines)
                {
                    float lineHeight = Text.CalcHeight(line, rect.width - padding * 2f) + 2f;
                    Rect lineRect = new Rect(rect.x + padding, y, rect.width - padding * 2f, lineHeight);
                    GUI.color = new Color(0.85f, 0.85f, 0.85f, 1f);
                    Widgets.Label(lineRect, line);
                    y += lineHeight;
                }
            }

            GUI.color = originalColor;
            Text.Font = originalFont;
            Text.Anchor = originalAnchor;
        }

        private string BuildThinkingStatus()
        {
            if (_core == null) return "Thinking...";
            float elapsedSeconds = (float)_core.ThinkingElapsedSeconds;
            string elapsedText = elapsedSeconds.ToString("0.0", CultureInfo.InvariantCulture);
            return $"P.I.A is thinking... ({elapsedText}s)";
        }

        private void DrawThinkingIndicator(Rect rect)
        {
            var originalColor = GUI.color;
            var originalAnchor = Text.Anchor;
            
            GUI.color = Color.gray;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            
            Rect statusRect = new Rect(rect.x, rect.y, rect.width, 22f);
            Widgets.Label(statusRect, BuildThinkingStatus());

            string usage = _core?.LastUsageSummary;
            if (!string.IsNullOrWhiteSpace(usage))
            {
                Text.Font = GameFont.Tiny;
                Rect usageRect = new Rect(rect.xMax - 180f, rect.y + 4f, 180f, 16f);
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(usageRect, usage);
                Text.Anchor = TextAnchor.MiddleLeft;
            }

            string thought = _core?.LatestThought;
            if (!string.IsNullOrWhiteSpace(thought))
            {
                Text.Font = GameFont.Tiny;
                Rect thoughtRect = new Rect(rect.x, statusRect.yMax + 2f, rect.width, 22f);
                // LatestThought carries trace lines and streamed reasoning; either way it reads as
                // "what the model is doing right now", not a status label.
                Widgets.Label(thoughtRect, thought);
            }
            
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
            _history = _core.GetHistorySnapshot() ?? new List<(string role, string message)>();
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

            AIImageStore.ClearCache();

            if (Instance == this) Instance = null;
            base.PostClose();
        }
    }
}
