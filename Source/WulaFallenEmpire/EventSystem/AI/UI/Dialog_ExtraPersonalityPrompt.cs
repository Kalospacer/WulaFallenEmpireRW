using System;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire.EventSystem.AI.UI
{
    public class Dialog_ExtraPersonalityPrompt : Window
    {
        private string _tempPrompt;

        public override Vector2 InitialSize => new Vector2(600f, 500f);

        public Dialog_ExtraPersonalityPrompt()
        {
            this.forcePause = true;
            this.doWindowBackground = true;
            this.absorbInputAroundWindow = true;
            this.closeOnClickedOutside = true;
            _tempPrompt = WulaFallenEmpireMod.settings?.extraPersonalityPrompt ?? "";
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0, 0, inRect.width, 40f), "Wula_ExtraPersonality_Title".Translate());
            Text.Font = GameFont.Small;

            float curY = 45f;
            Widgets.Label(new Rect(0, curY, inRect.width, 60f), "Wula_ExtraPersonality_Desc".Translate());
            curY += 65f;

            Rect textRect = new Rect(0, curY, inRect.width, inRect.height - curY - 50f);
            _tempPrompt = Widgets.TextArea(textRect, _tempPrompt);

            Rect btnRect = new Rect(inRect.width / 2 - 60f, inRect.height - 40f, 120f, 35f);
            if (Widgets.ButtonText(btnRect, "Wula_Save".Translate()))
            {
                if (WulaFallenEmpireMod.settings != null)
                {
                    WulaFallenEmpireMod.settings.extraPersonalityPrompt = _tempPrompt;
                    WulaFallenEmpireMod.settings.Write();
                }
                this.Close();
            }
        }
    }
}
