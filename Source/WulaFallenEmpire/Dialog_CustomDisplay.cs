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
            // Top-left defName
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(0, 0, inRect.width, 30f), def.defName);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            // Scaling factor to fit the new window size while maintaining layout proportions.
            float scale = 0.65f;

            // The original CSS was based on a large canvas. We create a virtual canvas inside our window.
            // Center the main content block.
            float contentWidth = 1200f * scale;
            float contentHeight = 1100f * scale;
            Rect contentRect = new Rect((inRect.width - contentWidth) / 2, (inRect.height - contentHeight) / 2, contentWidth, contentHeight);

            // All original positions are now relative to this contentRect and scaled.
            Rect mainBodySRect = new Rect(contentRect.x + 200f * scale, contentRect.y + 400f * scale, 1050f * scale, 1000f * scale);

            // lihui (Portrait)
            Rect lihuiRect = new Rect(mainBodySRect.x - 150f * scale, mainBodySRect.y - 200f * scale, 500f * scale, 800f * scale);
            if (portrait != null)
            {
                GUI.DrawTexture(lihuiRect, portrait, ScaleMode.ScaleToFit);
            }
            GUI.color = Color.white;
            Widgets.DrawBox(lihuiRect);
            GUI.color = Color.white; // Reset color


            // name
            Rect nameRect = new Rect(lihuiRect.xMax, mainBodySRect.y - 30f * scale, 260f * scale, 130f * scale);
            GUI.color = Color.white;
            Widgets.DrawBox(nameRect);
            GUI.color = Color.white; // Reset color
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Medium;
            Widgets.Label(nameRect, def.characterName);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;

            // text (Description)
            Rect textRect = new Rect(nameRect.x, nameRect.yMax + 50f * scale, 650f * scale, 250f * scale);
            GUI.color = Color.white;
            Widgets.DrawBox(textRect);
            GUI.color = Color.white; // Reset color
            Rect textInnerRect = textRect.ContractedBy(10f * scale);
            Widgets.Label(textInnerRect, def.description);

            // option (Buttons)
            Rect optionRect = new Rect(nameRect.x, textRect.yMax, 610f * scale, 300f * scale);
            // No need to draw a box for the options area, the buttons will be listed inside.

            Listing_Standard listing = new Listing_Standard();
            listing.Begin(optionRect.ContractedBy(10f * scale));
            if (def.options != null)
            {
                foreach (var option in def.options)
                {
                    if (listing.ButtonText(option.label))
                    {
                        HandleAction(option.effects);
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
    }
}
