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

        public override Vector2 InitialSize => new Vector2(1000f, 750f);

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
                this.portrait = ContentFinder<Texture2D>.Get(def.portraitPath);
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            // Top-left defName and Label
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(5, 5, inRect.width - 10, 20f), def.defName);
            GUI.color = Color.white;

            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(5, 20f, inRect.width - 10, 30f), def.label);


            // Define virtual total size from the CSS layout
            float virtualWidth = 500f + 650f; // lihui + text
            float virtualHeight = 800f; // lihui height

            // Calculate scale to fit the window, maintaining aspect ratio
            float scaleX = inRect.width / virtualWidth;
            float scaleY = inRect.height / virtualHeight;
            float scale = Mathf.Min(scaleX, scaleY) * 0.95f; // Use 95% of space to leave some margin

            // Calculate scaled dimensions
            float scaledLihuiWidth = 500f * scale;
            float scaledLihuiHeight = 800f * scale;
            float scaledNameWidth = 260f * scale;
            float scaledNameHeight = 130f * scale;
            float scaledTextWidth = 650f * scale;
            float scaledTextHeight = 250f * scale;
            float scaledOptionsWidth = 610f * scale;

            // Center the whole content block
            float totalContentWidth = scaledLihuiWidth + scaledTextWidth;
            float totalContentHeight = scaledLihuiHeight;
            float startX = (inRect.width - totalContentWidth) / 2;
            float startY = (inRect.height - totalContentHeight) / 2;

            // lihui (Portrait)
            Rect lihuiRect = new Rect(startX, startY, scaledLihuiWidth, scaledLihuiHeight);
            if (portrait != null)
            {
                GUI.DrawTexture(lihuiRect, portrait, ScaleMode.ScaleToFit);
            }
            GUI.color = Color.white;
            Widgets.DrawBox(lihuiRect);
            GUI.color = Color.white; // Reset color


            // name
            Rect nameRect = new Rect(lihuiRect.xMax, lihuiRect.y, scaledNameWidth, scaledNameHeight);
            GUI.color = Color.white;
            Widgets.DrawBox(nameRect);
            GUI.color = Color.white; // Reset color
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Medium;
            Widgets.Label(nameRect, def.characterName);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;

            // text (Description)
            Rect textRect = new Rect(nameRect.x, nameRect.yMax + 20f * scale, scaledTextWidth, scaledTextHeight);
            GUI.color = Color.white;
            Widgets.DrawBox(textRect);
            GUI.color = Color.white; // Reset color
            Rect textInnerRect = textRect.ContractedBy(10f * scale);
            Widgets.Label(textInnerRect, def.description);

            // option (Buttons)
            Rect optionRect = new Rect(nameRect.x, textRect.yMax + 20f * scale, scaledOptionsWidth, lihuiRect.height - nameRect.height - textRect.height - 40f * scale);
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
    }
}
