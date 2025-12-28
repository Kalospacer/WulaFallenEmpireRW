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
            
            // 如果目前是空的，默认显示当前 XML/Def 的内容供玩家修改
            if (string.IsNullOrWhiteSpace(_tempPrompt))
            {
                var core = Find.World?.GetComponent<AIIntelligenceCore>();
                if (core != null)
                {
                    _tempPrompt = core.GetEffectiveBasePersona();
                }
            }
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

            Rect saveBtnRect = new Rect(inRect.width / 2 - 130f, inRect.height - 40f, 120f, 35f);
            if (Widgets.ButtonText(saveBtnRect, "Wula_Save".Translate()))
            {
                if (WulaFallenEmpireMod.settings != null)
                {
                    WulaFallenEmpireMod.settings.extraPersonalityPrompt = _tempPrompt;
                    WulaFallenEmpireMod.settings.Write();
                }
                this.Close();
            }

            Rect resetBtnRect = new Rect(inRect.width / 2 + 10f, inRect.height - 40f, 120f, 35f);
            if (Widgets.ButtonText(resetBtnRect, "Wula_Reset".Translate()))
            {
                var core = Find.World?.GetComponent<AIIntelligenceCore>();
                if (core != null)
                {
                    _tempPrompt = core.GetEffectiveBasePersona();
                }
            }
        }
    }
}
