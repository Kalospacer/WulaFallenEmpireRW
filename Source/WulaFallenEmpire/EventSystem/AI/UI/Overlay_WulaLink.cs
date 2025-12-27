using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;
using WulaFallenEmpire.EventSystem.AI;

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

        
        // HUD / Minimized State
        private bool _isMinimized = false;
        private int _unreadCount = 0;
        private Vector2 _expandedSize;
        private Vector2 _minimizedSize = new Vector2(220f, 80f);
        
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
            this.doWindowBackground = false; // We draw our own
            this.drawShadow = false; // 禁用阴影
            this.draggable = true;
            this.resizeable = true;
            this.preventCameraMotion = false;
            
            // Initial Size (Phone-like)
            _expandedSize = new Vector2(380f, 600f);
        }

        public override Vector2 InitialSize => _isMinimized ? _minimizedSize : _expandedSize;

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
                // 展开时居中到屏幕中心
                windowRect.width = _expandedSize.x;
                windowRect.height = _expandedSize.y;
                windowRect.x = (Verse.UI.screenWidth - _expandedSize.x) / 2f;
                windowRect.y = (Verse.UI.screenHeight - _expandedSize.y) / 2f;
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

        private void OnExpressionChanged(int id)
        {
            // Repaint happens next frame
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (_isMinimized)
            {
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
            // HUD Capsule Style
            Widgets.DrawBoxSolid(rect, new Color(0.1f, 0.1f, 0.1f, 0.85f)); // Semi-transparent black
            GUI.color = WulaLinkStyles.HeaderColor;
            Widgets.DrawBox(rect, 2); // Thicker colored border
            GUI.color = Color.white;
            
            // Layout
            Rect titleRect = new Rect(rect.x + 10f, rect.y + 5f, rect.width - 20f, 25f);
            Rect statusRect = new Rect(rect.x + 10f, rect.yMax - 30f, rect.width - 20f, 25f);
            
            // Title
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Small;
            Widgets.Label(titleRect, "WULA LINK");
            
            // Status
            string status = _core.IsThinking ? "Thinking..." : "Standby";
            Color statusColor = _core.IsThinking ? Color.yellow : Color.green;
            
            GUI.color = statusColor;
            Widgets.Label(statusRect, $"鈼?{status}");
            GUI.color = Color.white;

            // Unread Badge
            if (_unreadCount > 0)
            {
                float badgeSize = 24f;
                Rect badgeRect = new Rect(rect.xMax - badgeSize - 5f, rect.y - 10f, badgeSize, badgeSize);
                GUI.color = Color.red;
                GUI.DrawTexture(badgeRect, BaseContent.WhiteTex); // Circle ideally
                GUI.color = Color.white;
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(badgeRect, _unreadCount.ToString());
                Text.Font = GameFont.Small;
            }
            Text.Anchor = TextAnchor.UpperLeft;
            
            // Click to Expand
            if (Widgets.ButtonInvisible(rect))
            {
                ToggleMinimize();
            }
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

        private void DrawMessageList(Rect rect)
        {
            var history = _core?.GetHistorySnapshot();
            if (history == null) return;

            // Filter out tool messages, empty messages, and XML-only messages
            var displayHistory = new List<(string role, string message, string displayText)>();
            foreach (var msg in history)
            {
                // Skip tool/toolcall messages to avoid empty spacing
                if (msg.role == "tool" || msg.role == "toolcall") continue;
                if (msg.role == "system" && !Prefs.DevMode) continue;
                
                // For assistant messages, strip XML and check if empty
                string displayText = msg.message;
                if (msg.role == "assistant")
                {
                    displayText = StripXmlTags(msg.message)?.Trim() ?? "";
                }
                
                // Skip empty or whitespace-only messages
                if (string.IsNullOrWhiteSpace(displayText)) continue;
                
                displayHistory.Add((msg.role, msg.message, displayText));
            }

            // Setup ScrollView
            float contentHeight = 0f;
            float width = rect.width - 26f; // Scrollbar space
            float reducedSpacing = 8f;
            
            List<float> heights = new List<float>();
            foreach (var msg in displayHistory)
            {
                string textToMeasure = msg.role == "assistant" ? msg.displayText : msg.message;
                float h = CalcMessageHeight(textToMeasure, width);
                heights.Add(h);
                contentHeight += h + reducedSpacing;
            }
            
            if (_core.IsThinking)
            {
                contentHeight += 40f;
            }

            Rect viewRect = new Rect(0, 0, width, contentHeight);
            
            if (_scrollToBottom)
            {
                _scrollPosition.y = contentHeight - rect.height;
                _scrollToBottom = false;
            }

            Widgets.BeginScrollView(rect, ref _scrollPosition, viewRect);

            float curY = 10f;
            for (int i = 0; i < displayHistory.Count; i++)
            {
                var entry = displayHistory[i];
                float h = heights[i];
                Rect msgRect = new Rect(0, curY, width, h);
                
                if (entry.role == "user")
                {
                    DrawSenseiMessage(msgRect, entry.message);
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

                curY += h + reducedSpacing;
            }

            if (_core.IsThinking)
            {
                Rect thinkingRect = new Rect(0, curY, width, 30f);
                DrawThinkingIndicator(thinkingRect);
            }

            Widgets.EndScrollView();
        }

        private static string StripXmlTags(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            // Remove XML tags with content: <tag>content</tag>
            string stripped = System.Text.RegularExpressions.Regex.Replace(text, @"<([a-zA-Z0-9_]+)[^>]*>.*?</\1>", "", System.Text.RegularExpressions.RegexOptions.Singleline);
            // Remove self-closing tags: <tag/>
            stripped = System.Text.RegularExpressions.Regex.Replace(stripped, @"<([a-zA-Z0-9_]+)[^>]*/?>", "");
            return stripped;
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
            Text.Font = WulaLinkStyles.MessageFont;
            float textH = Text.CalcHeight(text, maxBubbleWidth - (BubblePadding * 2));
            return Mathf.Max(textH + (BubblePadding * 2), AvatarSize);
        }

        private void DrawSenseiMessage(Rect rect, string text)
        {
            // Right aligned Blue Bubble (MomoTalk Sensei style)
            float maxBubbleWidth = rect.width * MaxBubbleWidthRatio;
            Text.Font = WulaLinkStyles.MessageFont;
            float textWidth = maxBubbleWidth - (BubblePadding * 2);
            float textHeight = Text.CalcHeight(text, textWidth);
            float bubbleHeight = textHeight + (BubblePadding * 2);
            float bubbleWidth = Mathf.Min(Text.CalcSize(text).x + (BubblePadding * 2) + 10f, maxBubbleWidth);
            
            // 气泡位置 - 右对齐，留出箭头空间
            float arrowSize = 8f;
            Rect bubbleRect = new Rect(rect.xMax - bubbleWidth - arrowSize - 5f, rect.y, bubbleWidth, bubbleHeight);
            
            // 绘制圆角气泡背景 - 蓝色 (Sensei color)
            Color bubbleColor = new Color(0.29f, 0.54f, 0.78f, 1f); // #4a8ac6 MomoTalk Sensei blue
            DrawRoundedBubble(bubbleRect, bubbleColor, 8f);
            
            // 绘制右侧箭头
            DrawBubbleArrow(bubbleRect.xMax, bubbleRect.y + 10f, arrowSize, bubbleColor, false);
            
            // 绘制文字
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(bubbleRect.ContractedBy(BubblePadding), text);
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawStudentMessage(Rect rect, string text)
        {
            // Left aligned Gray Bubble + Avatar (MomoTalk Student style)
            float avatarX = 10f;
            Rect avatarRect = new Rect(avatarX, rect.y, AvatarSize, AvatarSize);
            
            // 绘制圆形头像背景
            Color avatarBgColor = new Color(0.2f, 0.2f, 0.25f, 1f);
            DrawRoundedBubble(avatarRect, avatarBgColor, AvatarSize / 2f);
            
            // 绘制头像
            int expId = _core?.ExpressionId ?? 1;
            string portraitPath = _def.portraitPath ?? $"Wula/Events/Portraits/WULA_Legion_{expId}";
            if (expId > 1 && _def.portraitPath == null)
            {
                portraitPath = $"Wula/Events/Portraits/WULA_Legion_{expId}";
            }
            
            Texture2D portrait = ContentFinder<Texture2D>.Get(portraitPath, false);
            if (portrait != null)
            {
                GUI.DrawTexture(avatarRect.ContractedBy(2f), portrait, ScaleMode.ScaleToFit);
            }
            else
            {
                // 显示占位符
                GUI.color = Color.white;
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(avatarRect, "P.I.A");
            }
            GUI.color = Color.white;

            // 气泡
            float maxBubbleWidth = rect.width * MaxBubbleWidthRatio;
            float arrowSize = 8f;
            float bubbleX = avatarRect.xMax + arrowSize + 5f;
            
            Text.Font = WulaLinkStyles.MessageFont;
            float textWidth = maxBubbleWidth - AvatarSize - arrowSize - 20f;
            float textHeight = Text.CalcHeight(text, textWidth);
            float bubbleHeight = textHeight + (BubblePadding * 2);
            float bubbleWidth = Mathf.Min(Text.CalcSize(text).x + (BubblePadding * 2) + 10f, maxBubbleWidth - AvatarSize - arrowSize - 20f);
            
            Rect bubbleRect = new Rect(bubbleX, rect.y, bubbleWidth, bubbleHeight);

            // 绘制圆角气泡背景 - 灰色 (Student color)
            Color bubbleColor = new Color(0.85f, 0.85f, 0.87f, 1f); // Light gray like MomoTalk
            DrawRoundedBubble(bubbleRect, bubbleColor, 8f);
            
            // 绘制左侧箭头
            DrawBubbleArrow(bubbleRect.x, bubbleRect.y + 10f, arrowSize, bubbleColor, true);

            // 绘制文字
            GUI.color = new Color(0.1f, 0.1f, 0.1f, 1f); // Dark text
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

        private void DrawThinkingIndicator(Rect rect)
        {
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.gray;
            Rect iconRect = new Rect(rect.x + 60f, rect.y, 24f, 24f); 
            Rect labelRect = new Rect(iconRect.xMax + 5f, rect.y, 200f, 24f);
            
            // Draw a simple box as thinking indicator if TexUI is missing
            Widgets.DrawBoxSolid(iconRect, Color.gray);
            Widgets.Label(labelRect, "P.I.A is thinking...");
            
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

        // MomoTalk 风格的圆角气泡
        private void DrawRoundedBubble(Rect rect, Color color, float radius)
        {
            var originalColor = GUI.color;
            GUI.color = color;
            
            // 主体矩形
            Widgets.DrawBoxSolid(new Rect(rect.x + radius, rect.y, rect.width - radius * 2, rect.height), color);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y + radius, rect.width, rect.height - radius * 2), color);
            
            // 四个角的近似圆角
            float step = radius / 4f;
            for (float dx = 0; dx < radius; dx += step)
            {
                for (float dy = 0; dy < radius; dy += step)
                {
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    if (dist <= radius)
                    {
                        Widgets.DrawBoxSolid(new Rect(rect.x + radius - dx - step, rect.y + radius - dy - step, step, step), color);
                        Widgets.DrawBoxSolid(new Rect(rect.xMax - radius + dx, rect.y + radius - dy - step, step, step), color);
                        Widgets.DrawBoxSolid(new Rect(rect.x + radius - dx - step, rect.yMax - radius + dy, step, step), color);
                        Widgets.DrawBoxSolid(new Rect(rect.xMax - radius + dx, rect.yMax - radius + dy, step, step), color);
                    }
                }
            }
            
            GUI.color = originalColor;
        }

        // MomoTalk 风格的气泡箭头
        private void DrawBubbleArrow(float x, float y, float size, Color color, bool pointLeft)
        {
            var originalColor = GUI.color;
            GUI.color = color;
            
            // 用小方块模拟三角形箭头
            float step = size / 4f;
            for (int i = 0; i < 4; i++)
            {
                float width = step * (i + 1);
                float offsetX = pointLeft ? (x - size + step * i) : (x + step * i);
                float offsetY = y + step * i;
                Widgets.DrawBoxSolid(new Rect(offsetX, offsetY, step, step), color);
            }
            
            GUI.color = originalColor;
        }
    }
}





