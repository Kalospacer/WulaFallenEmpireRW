using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    public class Dialog_CustomDisplay : Window
    {
        private CustomUIDef def;
        private Texture2D portrait;
        private Texture2D background;

        private static EventUIConfigDef config;
        public static EventUIConfigDef Config
        {
            get
            {
                if (config == null)
                {
                    config = DefDatabase<EventUIConfigDef>.GetNamed("Wula_EventUIConfig");
                }
                return config;
            }
        }

        public override Vector2 InitialSize => new Vector2(750f, 500f);

        public Dialog_CustomDisplay(CustomUIDef def)
        {
            this.def = def;
            this.forcePause = true;
            this.absorbInputAroundWindow = true;
            this.doCloseX = true; // Add a close button to the window
        }

        public override void PreOpen()
        {
            base.PreOpen();
            if (!def.portraitPath.NullOrEmpty())
            {
                portrait = ContentFinder<Texture2D>.Get(def.portraitPath);
            }

            string bgPath = !def.backgroundImagePath.NullOrEmpty() ? def.backgroundImagePath : Config.defaultBackgroundImagePath;
            if (!bgPath.NullOrEmpty())
            {
                background = ContentFinder<Texture2D>.Get(bgPath);
            }
            
            HandleAction(def.onOpenEffects);
        }

        public override void DoWindowContents(Rect inRect)
        {
            // 1. Draw Background
            if (background != null)
            {
                GUI.DrawTexture(inRect, background, ScaleMode.ScaleToFit);
            }

            // 2. Draw Top-left defName and Label
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(5, 5, inRect.width - 10, 20f), def.defName);
            GUI.color = Color.white;

            Text.Font = Config.labelFont;
            Widgets.Label(new Rect(5, 20f, inRect.width - 10, 30f), def.label);
            Text.Font = GameFont.Small; // Reset to default

            // 3. Calculate Layout based on ConfigDef
            float virtualWidth = Config.lihuiSize.x + Config.textSize.x;
            float virtualHeight = Config.lihuiSize.y;

            float scaleX = inRect.width / virtualWidth;
            float scaleY = inRect.height / virtualHeight;
            float scale = Mathf.Min(scaleX, scaleY) * 0.95f;

            float scaledLihuiWidth = Config.lihuiSize.x * scale;
            float scaledLihuiHeight = Config.lihuiSize.y * scale;
            float scaledNameWidth = Config.nameSize.x * scale;
            float scaledNameHeight = Config.nameSize.y * scale;
            float scaledTextWidth = Config.textSize.x * scale;
            float scaledTextHeight = Config.textSize.y * scale;
            float scaledOptionsWidth = Config.optionsWidth * scale;

            float totalContentWidth = scaledLihuiWidth + scaledTextWidth;
            float totalContentHeight = scaledLihuiHeight;
            float startX = (inRect.width - totalContentWidth) / 2;
            float startY = (inRect.height - totalContentHeight) / 2;

            // 4. Draw UI Elements
            // lihui (Portrait)
            Rect lihuiRect = new Rect(startX, startY, scaledLihuiWidth, scaledLihuiHeight);
            if (portrait != null)
            {
                GUI.DrawTexture(lihuiRect, portrait, ScaleMode.ScaleToFit);
            }
            if (Config.drawBorders)
            {
                GUI.color = Color.white;
                Widgets.DrawBox(lihuiRect);
                GUI.color = Color.white;
            }

            // name
            Rect nameRect = new Rect(lihuiRect.xMax, lihuiRect.y, scaledNameWidth, scaledNameHeight);
            if (Config.drawBorders)
            {
                GUI.color = Color.white;
                Widgets.DrawBox(nameRect);
                GUI.color = Color.white;
            }
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Medium;
            Widgets.Label(nameRect, def.characterName);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;

            // text (Description)
            Rect textRect = new Rect(nameRect.x, nameRect.yMax + Config.textNameOffset * scale, scaledTextWidth, scaledTextHeight);
            if (Config.drawBorders)
            {
                GUI.color = Color.white;
                Widgets.DrawBox(textRect);
                GUI.color = Color.white;
            }
            Rect textInnerRect = textRect.ContractedBy(10f * scale);
            Widgets.Label(textInnerRect, def.description);

            // option (Buttons)
            Rect optionRect = new Rect(nameRect.x, textRect.yMax + Config.optionsTextOffset * scale, scaledOptionsWidth, lihuiRect.height - nameRect.height - textRect.height - (Config.textNameOffset + Config.optionsTextOffset) * scale);
            // No need to draw a box for the options area, the buttons will be listed inside.

            Listing_Standard listing = new Listing_Standard();
            listing.Begin(optionRect.ContractedBy(10f * scale));
            if (def.options != null)
            {
                foreach (var option in def.options)
                {
                    string reason;
                    bool conditionsMet = AreConditionsMet(option.conditions, out reason);

                    if (conditionsMet)
                    {
                        if (listing.ButtonText(option.label))
                        {
                            HandleAction(option.effects);
                        }
                    }
                    else
                    {
                        // Draw a disabled button and add a tooltip
                        Rect rect = listing.GetRect(30f);
                        Widgets.ButtonText(rect, option.label, false, true, false);
                        TooltipHandler.TipRegion(rect, GetDisabledReason(option, reason));
                    }
                }
            }
            listing.End();
        }

        private void HandleAction(List<Effect> effects)
        {
            if (effects.NullOrEmpty())
            {
                return;
            }

            foreach (var effect in effects)
            {
                effect.Execute(this);
            }
        }

        private bool AreConditionsMet(List<Condition> conditions, out string reason)
        {
            reason = "";
            if (conditions.NullOrEmpty())
            {
                return true;
            }

            foreach (var condition in conditions)
            {
                if (!condition.IsMet(out string singleReason))
                {
                    reason = singleReason;
                    return false;
                }
            }
            return true;
        }

        private string GetDisabledReason(CustomUIOption option, string reason)
        {
            if (!option.disabledReason.NullOrEmpty())
            {
                return option.disabledReason;
            }
            return reason;
        }

        public override void PostClose()
        {
            base.PostClose();
            HandleAction(def.dismissEffects);
        }
    }
}
