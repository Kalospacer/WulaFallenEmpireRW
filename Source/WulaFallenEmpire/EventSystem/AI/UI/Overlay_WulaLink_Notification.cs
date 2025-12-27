using System;
using UnityEngine;
using Verse;
using RimWorld;
using WulaFallenEmpire.EventSystem.AI;

namespace WulaFallenEmpire.EventSystem.AI.UI
{
    public class Overlay_WulaLink_Notification : Window
    {
        private string _message;
        private int _tickCreated;
        private const int DisplayTicks = 180; // 3 seconds
        private Vector2 _size = new Vector2(320f, 65f);

        public override Vector2 InitialSize => _size;

        public Overlay_WulaLink_Notification(string message)
        {
            _message = message;
            _tickCreated = Find.TickManager.TicksGame;

            // Calculate adaptive size (Instance-based)
            Text.Font = GameFont.Small;
            float textWidth = Text.CalcSize(message).x;
            
            // Limit max width to 500f, wrapping for long text
            if (textWidth > 450f)
            {
                float width = 550f; // Wider
                float height = Text.CalcHeight(message, width - 65f) + 65f; // Extra buffer for bottom lines
                _size = new Vector2(width, height);
            }
            else
            {
                _size = new Vector2(Mathf.Max(250f, textWidth + 85f), 85f); // Taller base
            }

            // Window properties
            this.layer = WindowLayer.Super;
            this.closeOnClickedOutside = false;
            this.forcePause = false;
            this.absorbInputAroundWindow = false;
            this.doWindowBackground = false;
            this.drawShadow = false;
        }

        protected override void SetInitialSizeAndPosition()
        {
            // Position at top-left
            this.windowRect = new Rect(20f, 20f, InitialSize.x, InitialSize.y);
        }

        public override void DoWindowContents(Rect inRect)
        {
            // Auto Close after time
            if (Find.TickManager.TicksGame > _tickCreated + DisplayTicks)
            {
                Close();
                return;
            }

            // UI Styling (Legion Style)
            Widgets.DrawBoxSolid(inRect, new Color(0.12f, 0.05f, 0.05f, 0.95f)); // Dark blood red background
            GUI.color = WulaLinkStyles.HeaderColor;
            Widgets.DrawBox(inRect, 2);
            GUI.color = Color.white;

            // 恢复刚才的视觉风格 (Content areas)
            Rect iconRect = new Rect(10f, 12f, 25f, 25f);
            Rect titleRect = new Rect(45f, 5f, inRect.width - 60f, 20f);
            Rect textRect = new Rect(45f, 25f, inRect.width - 55f, inRect.height - 30f);

            // 绘制视觉装饰 (装饰性图标和标题)
            Widgets.DrawBoxSolid(iconRect, WulaLinkStyles.HeaderColor);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Tiny;
            Widgets.Label(iconRect, "!");

            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.yellow;
            Widgets.Label(titleRect, "WULA LINK :: MESSAGE");
            GUI.color = Color.white;

            // Body Text
            Text.Font = GameFont.Small;
            Widgets.Label(textRect, _message);

            // Hover and Click Logic (全窗口交互，无需按钮)
            if (Mouse.IsOver(inRect))
            {
                Widgets.DrawHighlight(inRect);
                // 0 = Left Click, 1 = Right Click
                if (Event.current.type == EventType.MouseDown)
                {
                    if (Event.current.button == 0) // Left click: Open
                    {
                        OpenWulaLink();
                        Close();
                        Event.current.Use();
                    }
                    else if (Event.current.button == 1) // Right click: Close
                    {
                        Close();
                        Event.current.Use();
                    }
                }
            }
            
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }

        public void OpenWulaLink()
        {
            var existing = Find.WindowStack.WindowOfType<Overlay_WulaLink>();
            if (existing != null)
            {
                existing.Expand();
            }
            else
            {
                // Fallback: If no overlay exists, try to find EventDef or just show message
                Messages.Message("Wula_AI_Notification_ClickTips".Translate(), MessageTypeDefOf.NeutralEvent);
            }
        }
    }
}
