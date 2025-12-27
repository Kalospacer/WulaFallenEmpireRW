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
        private const int DisplayTicks = 300; // 5 seconds

        public override Vector2 InitialSize => new Vector2(300f, 80f);

        public Overlay_WulaLink_Notification(string message)
        {
            _message = message;
            _tickCreated = Find.TickManager.TicksGame;

            // Transient properties
            this.layer = WindowLayer.Super; // Topmost
            this.closeOnClickedOutside = false;
            this.forcePause = false;
            this.absorbInputAroundWindow = false;
            this.doWindowBackground = false; // Custom bg
            this.drawShadow = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            // Auto Close after time
            if (Find.TickManager.TicksGame > _tickCreated + DisplayTicks)
            {
                Close();
                return;
            }

            // Draw HUD Notification Style
            Widgets.DrawBoxSolid(inRect, new Color(0.1f, 0.1f, 0.1f, 0.9f));
            GUI.color = WulaLinkStyles.SystemAccentColor;
            Widgets.DrawBox(inRect, 1);
            GUI.color = Color.white;

            Rect iconRect = new Rect(10f, 10f, 30f, 30f);
            Rect titleRect = new Rect(50f, 10f, 200f, 20f);
            Rect textRect = new Rect(50f, 30f, 240f, 40f);

            // Icon (Warning / Info)
            Widgets.DrawBoxSolid(iconRect, WulaLinkStyles.SystemAccentColor);
            GUI.color = Color.black;
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Medium;
            Widgets.Label(iconRect, "!");
            GUI.color = Color.white;

            // Title
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Tiny;
            GUI.color = Color.yellow;
            Widgets.Label(titleRect, "WULA LINK :: NEW ALERT");
            GUI.color = Color.white;

            // Text
            Text.Font = GameFont.Small;
            string truncated = _message.Length > 40 ? _message.Substring(0, 38) + "..." : _message;
            Widgets.Label(textRect, truncated);

            // Click to Open/Expand
            if (Widgets.ButtonInvisible(inRect))
            {
                OpenWulaLink();
                Close();
            }
        }

        public void OpenWulaLink()
        {
            // Find existing or open new
            var existing = Find.WindowStack.WindowOfType<Overlay_WulaLink>();
            if (existing != null)
            {
                existing.Expand();
                Find.WindowStack.Notify_ManuallySetFocus(existing);
            }
            else
            {
                 // Create new if not exists
                 var core = AIIntelligenceCore.Instance;
                 // Without EventDef we can't easily open. 
                 // Assuming notification implies active core state.
            }
        }
    }
}
