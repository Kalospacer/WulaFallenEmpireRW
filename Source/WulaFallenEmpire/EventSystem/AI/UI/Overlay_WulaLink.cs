using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;
using WulaFallenEmpire;
using WulaFallenEmpire.EventSystem.AI;
using WulaFallenEmpire.EventSystem.AI.Utils;

namespace WulaFallenEmpire.EventSystem.AI.UI
{
    public class Overlay_WulaLink : Window
    {
        // Core Connection
        private AIIntelligenceCore _core;
        private string _eventDefName;
        private EventDef _def;

        // UI State
        private Vector2 _scrollPosition = Vector2.zero;
        private string _inputText = "";
        private bool _scrollToBottom = true;
        private int _lastHistoryCount = -1;
        private float _lastUsedWidth = -1f;
        private List<CachedMessage> _cachedMessages = new List<CachedMessage>();
        private float _cachedTotalHeight = 0f;
        private readonly Dictionary<int, bool> _traceExpandedByAssistantIndex = new Dictionary<int, bool>();
        private readonly Dictionary<int, string> _traceHeaderByAssistantIndex = new Dictionary<int, string>();

        private class CachedMessage
        {
            public string role;
            public string message;
            public string displayText;
            public float height;
            public float yOffset;
            public bool isTrace;
            public int traceKey;
            public string traceHeader;
            public List<string> traceLines;
            public bool traceExpanded;
            public float traceHeaderHeight;
        }

        
        // HUD / Minimized State
        private bool _isMinimized = false;
        private int _unreadCount = 0;
        private Vector2 _expandedSize;
        private Vector2 _minimizedSize = new Vector2(180f, 40f);
        private Vector2? _initialPosition = null;
        
        // Layout Constants
        private const float HeaderHeight = 50f;
        private const float FooterHeight = 50f;
        private const float AvatarSize = 40f;
        private const float BubblePadding = 10f;
        private const float MessageSpacing = 15f;
        private const float MaxBubbleWidthRatio = 0.75f;

        public Overlay_WulaLink(EventDef def)
        {
            this._def = def;
            this._eventDefName = def.defName;
            
            // Window Properties (Floating, Non-Modal)
            this.layer = WindowLayer.GameUI; 
            this.forcePause = false;
            this.absorbInputAroundWindow = false;
            this.closeOnClickedOutside = false;
            this.closeOnAccept = false; // 防止 Enter 键误关闭
            this.closeOnCancel = false; // 防止 Esc 键误关闭
            this.doWindowBackground = false; // We draw our own
            this.drawShadow = false; // 禁用阴影
            this.draggable = true;
            this.resizeable = true;
            this.preventCameraMotion = false;
            
            // Initial Size (Phone-like)
            _expandedSize = new Vector2(380f, 600f);
        }

        public override Vector2 InitialSize => _isMinimized ? _minimizedSize : _expandedSize;

        public void SetInitialPosition(float x, float y)
        {
            _initialPosition = new Vector2(x, y);
        }

        protected override void SetInitialSizeAndPosition()
        {
            base.SetInitialSizeAndPosition();
            // Override position if we have a saved position
            if (_initialPosition.HasValue)
            {
                windowRect.x = _initialPosition.Value.x;
                windowRect.y = _initialPosition.Value.y;
            }
        }

        public void ToggleMinimize()
        {
            _isMinimized = !_isMinimized;
            _unreadCount = 0; // Reset on toggle? Or only on expand? Let's say expand.
            
            if (_isMinimized)
            {
                // 最小化时保持当前位置，只调整大小
                windowRect.width = _minimizedSize.x;
                windowRect.height = _minimizedSize.y;
                // 确保不超出屏幕边界
                windowRect.x = Mathf.Clamp(windowRect.x, 0, Verse.UI.screenWidth - _minimizedSize.x);
                windowRect.y = Mathf.Clamp(windowRect.y, 0, Verse.UI.screenHeight - _minimizedSize.y);
            }
            else
            {
                // 展开时仅恢复大小，不强制居中
                windowRect.width = _expandedSize.x;
                windowRect.height = _expandedSize.y;
                // 确保展开后不超出右侧边界（简单边界检查）
                if (windowRect.xMax > Verse.UI.screenWidth)
                {
                    windowRect.x = Verse.UI.screenWidth - windowRect.width - 20f;
                }
            }
        }
        
        public void Expand()
        {
            if (_isMinimized) ToggleMinimize();
            Find.WindowStack.Notify_ManuallySetFocus(this);
        }

        public override void PreOpen()
        {
            base.PreOpen();
            // Connect to Core
            _core = Find.World.GetComponent<AIIntelligenceCore>();
            if (_core != null)
            {
                _core.InitializeConversation(_eventDefName);
                _core.OnMessageReceived += OnMessageReceived;
                _core.OnThinkingStateChanged += OnThinkingStateChanged;
                _core.OnExpressionChanged += OnExpressionChanged;
                _core.SetOverlayWindowState(true, _eventDefName);
            }
        }

        public override void PostClose()
        {
            base.PostClose();
            if (_core != null)
            {
                _core.OnMessageReceived -= OnMessageReceived;
                _core.OnThinkingStateChanged -= OnThinkingStateChanged;
                _core.OnExpressionChanged -= OnExpressionChanged;
                // Save position before closing
                _core.SetOverlayWindowState(false, null, windowRect.x, windowRect.y);
            }
        }

        private void OnMessageReceived(string msg)
        {
            _scrollToBottom = true;
            if (_isMinimized)
            {
                _unreadCount++;
                // Spawn Notification Bubble
                Find.WindowStack.Add(new Overlay_WulaLink_Notification(msg));
            }
        }

        private void OnThinkingStateChanged(bool thinking)
        {
            // Trigger repaint or animation update if needed
        }

        protected override float Margin => 0f;

        private void OnExpressionChanged(int id)
        {
            // Repaint happens next frame
        }

        public override void DoWindowContents(Rect inRect)
        {
            this.resizeable = !_isMinimized;

            if (_isMinimized)
            {
                // 强制同步 windowRect 到设计尺寸 (Margin 为 0 时直接匹配)
                if (windowRect.width != _minimizedSize.x || windowRect.height != _minimizedSize.y)
                {
                    windowRect.width = _minimizedSize.x;
                    windowRect.height = _minimizedSize.y;
                }
                DrawMinimized(inRect);
                return;
            }

            // Draw Main Background (Whole Window)
            Widgets.DrawBoxSolid(inRect, WulaLinkStyles.BackgroundColor);
            GUI.color = new Color(0.8f, 0.8f, 0.8f);
            Widgets.DrawBox(inRect, 1); // Border
            GUI.color = Color.white;

            // Areas
            Rect headerRect = new Rect(0, 0, inRect.width, HeaderHeight);
            Rect footerRect = new Rect(0, inRect.height - FooterHeight, inRect.width, FooterHeight);
            Rect contextRect = new Rect(0, inRect.height - FooterHeight - 30f, inRect.width, 30f); // Context Bar
            Rect bodyRect = new Rect(0, HeaderHeight, inRect.width, inRect.height - HeaderHeight - FooterHeight - 30f);

            // Draw Components
            DrawHeader(headerRect);
            DrawMessageList(bodyRect);
            DrawContextBar(contextRect);
            DrawFooter(footerRect);
        }

        private void DrawMinimized(Rect rect)
        {
            // AI 核心挂件背景
            Widgets.DrawBoxSolid(rect, new Color(0.1f, 0.1f, 0.1f, 0.9f)); 
            GUI.color = WulaLinkStyles.HeaderColor;
            Widgets.DrawBox(rect, 2); 
            GUI.color = Color.white;
            
            // 左侧：大型方形头像
            float avaSize = rect.height - 16f;
            Rect avatarRect = new Rect(8f, 8f, avaSize, avaSize);
            
            int expId = _core?.ExpressionId ?? 1;
            string portraitPath = "Wula/Storyteller/WULA_Legion_TINY";
            Texture2D portrait = ContentFinder<Texture2D>.Get(portraitPath, false);
            if (portrait != null)
            {
                GUI.DrawTexture(avatarRect, portrait, ScaleMode.ScaleToFit);
            }
            else
            {
                Widgets.DrawBoxSolid(avatarRect, new Color(0.3f, 0.3f, 0.3f));
            }

            // 右侧：状态展示
            float rightContentX = avatarRect.xMax + 12f;
            float btnWidth = 30f;
            
            // Status Info
            string status = _core.IsThinking ? "Thinking..." : "Standby";
            Color statusColor = _core.IsThinking ? Color.yellow : Color.green;

            // 绘制状态文字
            Rect textRect = new Rect(rightContentX, 0, rect.width - rightContentX - btnWidth - 5f, rect.height);
            Text.Anchor = TextAnchor.MiddleLeft;
            Text.Font = GameFont.Small;
            GUI.color = statusColor;
            Widgets.Label(textRect, _core.IsThinking ? BuildThinkingStatus() : "Standby");
            GUI.color = Color.white;

            // 右侧：小巧的展开按钮
            Rect expandBtnRect = new Rect(rect.width - 32f, (rect.height - 25f) / 2f, 25f, 25f);
            if (DrawHeaderButton(expandBtnRect, "+"))
            {
                ToggleMinimize();
            }

            // 未读标签角标 (头像右上角)
            if (_unreadCount > 0)
            {
                float badgeSize = 18f;
                Rect badgeRect = new Rect(avatarRect.xMax - 10f, avatarRect.y - 5f, badgeSize, badgeSize);
                GUI.DrawTexture(badgeRect, BaseContent.WhiteTex);
                GUI.color = Color.red; 
                Widgets.DrawBoxSolid(badgeRect.ContractedBy(1f), Color.red);
                GUI.color = Color.white;
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(badgeRect, _unreadCount.ToString());
            }

            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawContextBar(Rect rect)
        {
            // Context Awareness
            Widgets.DrawBoxSolid(rect, new Color(0.15f, 0.15f, 0.15f, 1f));
            GUI.color = Color.grey;
            Widgets.DrawLineHorizontal(rect.x, rect.y, rect.width);
            
            string contextInfo = "Context: None";
            if (Find.Selector.SingleSelectedThing != null)
            {
                contextInfo = $"Context: [{Find.Selector.SingleSelectedThing.LabelCap}]";
            }
            else if (Find.Selector.SelectedObjects.Count > 1)
            {
                contextInfo = $"Context: {Find.Selector.SelectedObjects.Count} objects selected";
            }
            
            Text.Anchor = TextAnchor.MiddleLeft;
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(rect.x + 10f, rect.y, rect.width - 20f, rect.height), contextInfo);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        private void DrawHeader(Rect rect)
        {
            // Header BG
            Widgets.DrawBoxSolid(rect, WulaLinkStyles.HeaderColor);
            
            // Title
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Rect titleRect = rect;
            titleRect.x += 10f;
            Widgets.Label(titleRect, _def.characterName ?? "MomoTalk");
            
            // Header Icons (Minimize/Close) - 自定义样式
            Rect closeRect = new Rect(rect.width - 35f, 10f, 25f, 25f);
            Rect minRect = new Rect(rect.width - 65f, 10f, 25f, 25f);
            
            // 最小化按钮
            if (DrawHeaderButton(minRect, "-"))
            {
                ToggleMinimize();
            }

            // 关闭按钮
            if (DrawHeaderButton(closeRect, "X"))
            {
                Close();
            }
            
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private bool DrawHeaderButton(Rect rect, string label)
        {
            bool isMouseOver = Mouse.IsOver(rect);
            Color buttonColor = isMouseOver 
                ? new Color(0.6f, 0.3f, 0.3f, 1f)  // Hover: 深红色
                : new Color(0.4f, 0.2f, 0.2f, 0.8f); // Normal: 暗红色
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

        private void UpdateCacheIfNeeded(float width)
        {
            var history = _core?.GetHistorySnapshot();
            if (history == null) return;

            // 如果宽度没变且条数没变，就不重算
            if (Math.Abs(_lastUsedWidth - width) < 0.1f && history.Count == _lastHistoryCount)
            {
                return;
            }

            _lastUsedWidth = width;
            if (_lastHistoryCount >= 0 && history.Count < _lastHistoryCount)
            {
                _traceExpandedByAssistantIndex.Clear();
                _traceHeaderByAssistantIndex.Clear();
            }
            _lastHistoryCount = history.Count;
            _cachedMessages.Clear();
            _cachedTotalHeight = 0f;
            float reducedSpacing = 8f;
            float curY = 10f;
            var toolcallBuffer = new List<string>();
            var toolResultBuffer = new List<string>();
            var traceNoteBuffer = new List<string>();
            bool traceEnabled = WulaFallenEmpireMod.settings?.showReactTraceInUI == true;

            for (int i = 0; i < history.Count; i++)
            {
                var msg = history[i];
                // Filter logic
                if (msg.role == "user")
                {
                    toolcallBuffer.Clear();
                    toolResultBuffer.Clear();
                    traceNoteBuffer.Clear();
                }

                if (msg.role == "toolcall")
                {
                    if (traceEnabled)
                    {
                        toolcallBuffer.Add(msg.message ?? "");
                    }
                    continue;
                }

                if (msg.role == "tool")
                {
                    if (traceEnabled)
                    {
                        toolResultBuffer.Add(msg.message ?? "");
                    }
                    continue;
                }

                if (msg.role == "trace")
                {
                    if (traceEnabled)
                    {
                        traceNoteBuffer.Add(msg.message ?? "");
                    }
                    continue;
                }

                if (msg.role == "system" && !Prefs.DevMode) continue;
                
                // Hide auto-commentary system messages (user-side) from display
                if (msg.role == "user" && msg.message.Contains("[AUTO_COMMENTARY]")) continue;
                
                string displayText = msg.message;
                if (msg.role == "assistant")
                {
                    displayText = StripToolCallJson(msg.message)?.Trim() ?? "";
                }
                else if (msg.role == "user")
                {
                    displayText = AIIntelligenceCore.StripContextInfo(msg.message);
                }
                
                if (msg.role == "assistant" && traceEnabled && toolcallBuffer.Count > 0)
                {
                    var traceLines = BuildTraceLines(toolcallBuffer, toolResultBuffer, traceNoteBuffer);
                    if (traceLines.Count > 0)
                    {
                        int traceKey = i;
                        bool expanded = _traceExpandedByAssistantIndex.TryGetValue(traceKey, out bool saved) && saved;
                        string header = GetTraceHeader(traceKey, false);

                        Text.Font = GameFont.Tiny;
                        float padding = 8f;
                        float headerWidth = Mathf.Max(10f, width - padding * 2f);
                        string headerLine = $"{(expanded ? "v" : ">")} {header}";
                        float headerHeight = Text.CalcHeight(headerLine, headerWidth) + 10f;
                        float linesHeight = 0f;
                        if (expanded)
                        {
                            float lineWidth = Mathf.Max(10f, width - padding * 2f);
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
                            isTrace = true,
                            traceKey = traceKey,
                            traceHeader = header,
                            traceLines = traceLines,
                            traceExpanded = expanded,
                            traceHeaderHeight = headerHeight
                        });
                        curY += traceHeight + reducedSpacing;
                    }

                    toolcallBuffer.Clear();
                    toolResultBuffer.Clear();
                    traceNoteBuffer.Clear();
                }
                else if (msg.role == "assistant" && traceEnabled && toolcallBuffer.Count == 0)
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
                    float headerWidth = Mathf.Max(10f, width - tracePadding * 2f);
                    string headerLine = $"{(expanded ? "v" : ">")} {header}";
                    float headerHeight = Text.CalcHeight(headerLine, headerWidth) + 10f;
                    float linesHeight = 0f;
                    if (expanded)
                    {
                        float lineWidth = Mathf.Max(10f, width - tracePadding * 2f);
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
                        isTrace = true,
                        traceKey = traceKey,
                        traceHeader = header,
                        traceLines = traceLines,
                        traceExpanded = expanded,
                        traceHeaderHeight = headerHeight
                    });
                    curY += traceHeight + reducedSpacing;
                    traceNoteBuffer.Clear();
                }

                if (string.IsNullOrWhiteSpace(displayText)) continue;

                float h = CalcMessageHeight(displayText, width);
                
                _cachedMessages.Add(new CachedMessage
                {
                    role = msg.role,
                    message = msg.message,
                    displayText = displayText,
                    height = h,
                    yOffset = curY
                });

                curY += h + reducedSpacing;
            }

            _cachedTotalHeight = curY;
        }

        private void DrawMessageList(Rect rect)
        {
            float width = rect.width - 26f; // Scrollbar space
            UpdateCacheIfNeeded(width);

            bool traceEnabled = WulaFallenEmpireMod.settings?.showReactTraceInUI == true;
            CachedMessage liveTraceEntry = null;
            float liveTraceHeight = 0f;
            if (_core != null && _core.IsThinking && traceEnabled)
            {
                var liveLines = BuildLiveTraceLines();
                if (liveLines.Count == 0)
                {
                    liveLines.Add("思考中…");
                }
                int traceKey = -1;
                bool expanded = _traceExpandedByAssistantIndex.TryGetValue(traceKey, out bool saved) ? saved : true;
                string header = GetTraceHeader(traceKey, true);

                Text.Font = GameFont.Tiny;
                float tracePadding = 8f;
                float headerWidth = Mathf.Max(10f, width - tracePadding * 2f);
                string headerLine = $"{(expanded ? "v" : ">")} {header}";
                float headerHeight = Text.CalcHeight(headerLine, headerWidth) + 10f;
                float linesHeight = 0f;
                if (expanded)
                {
                    float lineWidth = Mathf.Max(10f, width - tracePadding * 2f);
                    foreach (string line in liveLines)
                    {
                        linesHeight += Text.CalcHeight(line, lineWidth) + 2f;
                    }
                    linesHeight += 8f;
                }
                float traceHeight = headerHeight + linesHeight;
                liveTraceHeight = traceHeight + 8f;
                liveTraceEntry = new CachedMessage
                {
                    role = "trace",
                    message = "",
                    displayText = "",
                    height = traceHeight,
                    yOffset = 0f,
                    isTrace = true,
                    traceKey = traceKey,
                    traceHeader = header,
                    traceLines = liveLines,
                    traceExpanded = expanded,
                    traceHeaderHeight = headerHeight
                };
            }

            float totalContentHeight = _cachedTotalHeight;
            if (_core != null && _core.IsThinking)
            {
                totalContentHeight += liveTraceEntry != null ? liveTraceHeight : 40f;
            }

            Rect viewRect = new Rect(0, 0, width, totalContentHeight);
            
            if (_scrollToBottom)
            {
                _scrollPosition.y = totalContentHeight - rect.height;
                _scrollToBottom = false;
            }

            Widgets.BeginScrollView(rect, ref _scrollPosition, viewRect);

            // 虚拟化渲染：只绘制在窗口内的消息
            float viewTop = _scrollPosition.y;
            float viewBottom = _scrollPosition.y + rect.height;

            foreach (var entry in _cachedMessages)
            {
                // 检查是否在可见范围内 (略微预留 Buffer)
                if (entry.yOffset + entry.height < viewTop - 100f) continue;
                if (entry.yOffset > viewBottom + 100f) break;

                Rect msgRect = new Rect(0, entry.yOffset, width, entry.height);

                if (entry.isTrace)
                {
                    DrawReactTracePanel(msgRect, entry);
                }
                else if (entry.role == "user")
                {
                    DrawSenseiMessage(msgRect, entry.displayText);
                }
                else if (entry.role == "assistant")
                {
                    DrawStudentMessage(msgRect, entry.displayText);
                }
                else if (entry.role == "tool" || entry.role == "toolcall")
                {
                    DrawSystemMessage(msgRect, $"[Tool] {entry.message}");
                }
                else if (entry.role == "system")
                {
                    DrawSystemMessage(msgRect, entry.message);
                }
            }

            if (_core != null && _core.IsThinking)
            {
                float thinkingY = _cachedTotalHeight > 0 ? _cachedTotalHeight : 10f;
                if (liveTraceEntry != null)
                {
                    if (thinkingY + liveTraceEntry.height >= viewTop && thinkingY <= viewBottom)
                    {
                        Rect traceRect = new Rect(0, thinkingY, width, liveTraceEntry.height);
                        DrawReactTracePanel(traceRect, liveTraceEntry);
                    }
                }
                else
                {
                    Rect thinkingRect = new Rect(0, thinkingY, width, 30f);
                    if (thinkingY + 30f >= viewTop && thinkingY <= viewBottom)
                    {
                        DrawThinkingIndicator(thinkingRect);
                    }
                }
            }

            Widgets.EndScrollView();
        }

        private static string StripToolCallJson(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (!JsonToolCallParser.TryParseToolCallsFromText(text, out _, out string fragment))
            {
                return text;
            }

            int index = text.IndexOf(fragment, StringComparison.Ordinal);
            if (index < 0) return text;
            return text.Remove(index, fragment.Length).Trim();
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

                if (hasToolCalls && i < toolcallBuffer.Count &&
                    JsonToolCallParser.TryParseToolCallsFromText(toolcallBuffer[i], out var calls, out _))
                {
                    foreach (var call in calls)
                    {
                        if (string.IsNullOrWhiteSpace(call?.Name)) continue;
                        string args = call.ArgumentsJson;
                        string callText = string.IsNullOrWhiteSpace(args) || args == "{}"
                            ? call.Name
                            : $"{call.Name} {TrimForDisplay(args, 160)}";
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
                    lines.Add($"模型 · {TrimForDisplay(trimmed, 220)}");
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
                return BuildReactTraceHeader(true);
            }

            if (_traceHeaderByAssistantIndex.TryGetValue(traceKey, out string header))
            {
                return header;
            }

            header = BuildReactTraceHeader(false);
            _traceHeaderByAssistantIndex[traceKey] = header;
            return header;
        }

        private string BuildReactTraceHeader(bool isLive)
        {
            string state = isLive ? "思考中" : "已思考";
            float startTime = _core?.ThinkingStartTime ?? 0f;
            float elapsed = isLive && _core != null
                ? Mathf.Max(0f, Time.realtimeSinceStartup - startTime)
                : _core?.LastThinkingDuration ?? 0f;
            string elapsedText = elapsed > 0f ? elapsed.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) : "0.0";
            int phaseIndex = _core?.ThinkingPhaseIndex ?? 0;
            return $"{state} (用时 {elapsedText}s · Loop {phaseIndex})";
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
            string headerLine = $"{(traceEntry.traceExpanded ? "v" : ">")} {traceEntry.traceHeader}";
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

        private void DrawFooter(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, WulaLinkStyles.InputBarColor);
            Widgets.DrawLineHorizontal(rect.x, rect.y, rect.width); // Top border
            
            float padding = 8f;
            float btnWidth = 40f;
            float inputWidth = rect.width - btnWidth - (padding * 3);
            
            Rect inputRect = new Rect(rect.x + padding, rect.y + padding, inputWidth, rect.height - (padding * 2));
            Rect btnRect = new Rect(inputRect.xMax + padding, rect.y + padding, btnWidth, rect.height - (padding * 2));

            // Input Field
            string nextInput = Widgets.TextField(inputRect, _inputText);
            if (nextInput != _inputText)
            {
                _inputText = nextInput;
            }

            // Send Button (Simulate Enter key or Click)
            bool enterPressed = (Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter) && GUI.GetNameOfFocusedControl() == "WulaInput");
            
            bool sendClicked = DrawCustomButton(btnRect, ">", !string.IsNullOrWhiteSpace(_inputText));
            if (sendClicked || enterPressed)
            {
                if (!string.IsNullOrWhiteSpace(_inputText))
                {
                    bool wasFocused = GUI.GetNameOfFocusedControl() == "WulaInput";
                    _core.SendUserMessage(_inputText);
                    _inputText = "";
                    if (wasFocused) GUI.FocusControl("WulaInput"); 
                }
            }
            
            // Handle focus for Enter key to work
            if (Mouse.IsOver(inputRect) && Event.current.type == EventType.MouseDown)
            {
                GUI.FocusControl("WulaInput");
            }
            GUI.SetNextControlName("WulaInput");
        }

        // =================================================================================
        // Message Rendering Helpers
        // =================================================================================
        
        private float CalcMessageHeight(string text, float containerWidth)
        {
            float maxBubbleWidth = containerWidth * MaxBubbleWidthRatio;
            float effectiveWidth = maxBubbleWidth - AvatarSize - 20f;
            Text.Font = WulaLinkStyles.MessageFont;
            float textH = Text.CalcHeight(text, effectiveWidth - (BubblePadding * 2));
            return Mathf.Max(textH + (BubblePadding * 2) + 8f, AvatarSize + 8f);
        }

        private void DrawSenseiMessage(Rect rect, string text)
        {
            // 用户消息 - 右对齐蓝色气泡
            float maxBubbleWidth = rect.width * MaxBubbleWidthRatio;
            Text.Font = WulaLinkStyles.MessageFont;
            float textWidth = maxBubbleWidth - (BubblePadding * 2);
            float textHeight = Text.CalcHeight(text, textWidth);
            float bubbleHeight = textHeight + (BubblePadding * 2);
            float bubbleWidth = Mathf.Min(Text.CalcSize(text).x + (BubblePadding * 2) + 10f, maxBubbleWidth);
            
            Rect bubbleRect = new Rect(rect.xMax - bubbleWidth - 10f, rect.y, bubbleWidth, bubbleHeight);
            
            // 绘制气泡背景 - 蓝色
            Color bubbleColor = new Color(0.29f, 0.54f, 0.78f, 1f);
            Widgets.DrawBoxSolid(bubbleRect, bubbleColor);
            
            // 绘制文字
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(bubbleRect.ContractedBy(BubblePadding), text);
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawStudentMessage(Rect rect, string text)
        {
            // AI消息 - 左对齐带方形头像
            float avatarX = 10f;
            Rect avatarRect = new Rect(avatarX, rect.y, AvatarSize, AvatarSize);
            
            // 绘制方形头像
            int expId = _core?.ExpressionId ?? 1;
            string portraitPath = "Wula/Storyteller/WULA_Legion_TINY";
            
            Texture2D portrait = ContentFinder<Texture2D>.Get(portraitPath, false);
            if (portrait != null)
            {
                GUI.DrawTexture(avatarRect, portrait, ScaleMode.ScaleToFit);
            }
            else
            {
                Widgets.DrawBoxSolid(avatarRect, Color.gray);
            }

            // 气泡
            float maxBubbleWidth = rect.width * MaxBubbleWidthRatio;
            float bubbleX = avatarRect.xMax + 10f;
            
            Text.Font = WulaLinkStyles.MessageFont;
            float textWidth = maxBubbleWidth - AvatarSize - 20f;
            float textHeight = Text.CalcHeight(text, textWidth - (BubblePadding * 2));
            float bubbleHeight = textHeight + (BubblePadding * 2);
            float bubbleWidth = Mathf.Min(Text.CalcSize(text).x + (BubblePadding * 2) + 10f, maxBubbleWidth - AvatarSize - 20f);
            
            Rect bubbleRect = new Rect(bubbleX, rect.y, bubbleWidth, bubbleHeight);

            // 绘制气泡背景 - 灰色
            Color bubbleColor = new Color(0.85f, 0.85f, 0.87f, 1f);
            Widgets.DrawBoxSolid(bubbleRect, bubbleColor);
            
            // 绘制边框
            GUI.color = new Color(0.6f, 0.6f, 0.65f, 1f);
            Widgets.DrawBox(bubbleRect, 1);
            GUI.color = Color.white;

            // 绘制文字
            GUI.color = new Color(0.1f, 0.1f, 0.1f, 1f);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(bubbleRect.ContractedBy(BubblePadding), text);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        private void DrawSystemMessage(Rect rect, string text)
        {
            // Centered gray text or Pink box
            if (text.Contains("[Tool]")) return; // Skip logic log in main view if needed, but here we draw minimal
            
            GUI.color = Color.gray;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, text);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        private string BuildThinkingStatus()
        {
            if (_core == null) return "Thinking...";
            float elapsedSeconds = Mathf.Max(0f, Time.realtimeSinceStartup - _core.ThinkingStartTime);
            string elapsedText = elapsedSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            return $"P.I.A is thinking... ({elapsedText}s Loop {_core.ThinkingPhaseIndex})";
        }

        private void DrawThinkingIndicator(Rect rect)
        {
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.gray;
            Rect iconRect = new Rect(rect.x + 60f, rect.y, 24f, 24f); 
            Rect labelRect = new Rect(iconRect.xMax + 5f, rect.y, 400f, 24f);
            
            // Draw a simple box as thinking indicator if TexUI is missing
            Widgets.DrawBoxSolid(iconRect, Color.gray);
            Widgets.Label(labelRect, BuildThinkingStatus());
            
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        private bool DrawCustomButton(Rect rect, string label, bool isEnabled)
        {
            bool isMouseOver = Mouse.IsOver(rect);
            Color originalColor = GUI.color;
            TextAnchor originalAnchor = Text.Anchor;
            GameFont originalFont = Text.Font;

            Color buttonColor;
            Color textColor;
            if (!isEnabled)
            {
                buttonColor = Dialog_CustomDisplay.CustomButtonDisabledColor;
                textColor = Dialog_CustomDisplay.CustomButtonTextDisabledColor;
            }
            else if (isMouseOver)
            {
                buttonColor = Dialog_CustomDisplay.CustomButtonHoverColor;
                textColor = Dialog_CustomDisplay.CustomButtonTextHoverColor;
            }
            else
            {
                buttonColor = Dialog_CustomDisplay.CustomButtonNormalColor;
                textColor = Dialog_CustomDisplay.CustomButtonTextNormalColor;
            }

            GUI.color = buttonColor;
            Widgets.DrawBoxSolid(rect, buttonColor);
            Widgets.DrawBox(rect, 1);

            GUI.color = textColor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Tiny;
            Widgets.Label(rect.ContractedBy(4f), label);

            GUI.color = originalColor;
            Text.Anchor = originalAnchor;
            Text.Font = originalFont;

            if (!isEnabled)
            {
                return false;
            }

            return Widgets.ButtonInvisible(rect);
        }
    }
}


