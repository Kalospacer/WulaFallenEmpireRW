using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    public class Dialog_CustomDisplay : Window
    {
        private EventDef def;
        private Texture2D portrait;
        private Texture2D background;
        private string selectedDescription;

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

        public override Vector2 InitialSize
        {
            get
            {
                if (def.windowSize != Vector2.zero)
                {
                    return def.windowSize;
                }
                return Config.defaultWindowSize;
            }
        }

        public Dialog_CustomDisplay(EventDef def)
        {
            this.def = def;
            this.forcePause = true;
            this.absorbInputAroundWindow = true;
            this.doCloseX = true;

            var eventVarManager = Find.World.GetComponent<EventVariableManager>();
            if (!def.descriptions.NullOrEmpty())
            {
                if (def.descriptionMode == DescriptionSelectionMode.Random)
                {
                    selectedDescription = def.descriptions.RandomElement();
                }
                else 
                {
                    string indexVarName = $"_seq_desc_index_{def.defName}";
                    int currentIndex = eventVarManager.GetVariable<int>(indexVarName, 0);

                    selectedDescription = def.descriptions[currentIndex];

                    int nextIndex = (currentIndex + 1) % def.descriptions.Count;
                    eventVarManager.SetVariable(indexVarName, nextIndex);
                }
            }
            else
            {
                selectedDescription = "Error: No descriptions found in def.";
            }
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
            
            HandleAction(def.immediateEffects);
            
            if (!def.conditionalDescriptions.NullOrEmpty())
            {
                foreach (var condDesc in def.conditionalDescriptions)
                {
                    string reason;
                    if (AreConditionsMet(condDesc.conditions, out reason))
                    {
                        selectedDescription += "\n\n" + condDesc.text;
                    }
                }
            }
            
            selectedDescription = FormatDescription(selectedDescription);
        }

        public override void DoWindowContents(Rect inRect)
        {
            // 绘制背景
            if (background != null)
            {
                GUI.DrawTexture(inRect, background, ScaleMode.ScaleToFit);
            }

            // 调试信息（def名称）
            if (Config.showDefName)
            {
                Text.Font = GameFont.Tiny;
                GUI.color = Color.gray;
                Widgets.Label(new Rect(5, 5, inRect.width - 10, 20f), def.defName);
                GUI.color = Color.white;
            }

            // 使用新的布局参数
            float scale = CalculateScale(inRect);
            
            // 使用新的布局尺寸
            float scaledLihuiWidth = Config.newLayoutLihuiSize.x * scale;
            float scaledLihuiHeight = Config.newLayoutLihuiSize.y * scale;
            float scaledTextWidth = Config.newLayoutTextSize.x * scale;
            float scaledTextHeight = Config.newLayoutTextSize.y * scale;
            float scaledOptionsWidth = Config.newLayoutOptionsWidth * scale;

            // 计算各元素高度
            float labelHeight = 30f * scale;
            float characterNameHeight = 25f * scale;
            float descriptionHeight = scaledTextHeight;
            float optionsHeight = CalculateOptionsHeight(def.options, scaledOptionsWidth, scale);

            // 使用新的间距参数
            float topMargin = Config.newLayoutPadding * scale;
            float elementSpacing = Config.newLayoutTextNameOffset * scale;
            float textOptionsSpacing = Config.newLayoutOptionsTextOffset * scale;

            float currentY = topMargin;

            // 1. 立绘（水平居中，顶着顶部）
            Rect lihuiRect = new Rect((inRect.width - scaledLihuiWidth) / 2, currentY, scaledLihuiWidth, scaledLihuiHeight);
            if (portrait != null)
            {
                GUI.DrawTexture(lihuiRect, portrait, ScaleMode.ScaleToFit);
            }
            if (Config.drawBorders)
            {
                Widgets.DrawBox(lihuiRect);
            }
            currentY += scaledLihuiHeight + elementSpacing;

            // 2. Label（水平居中）
            if (Config.showLabel)
            {
                Rect labelRect = new Rect(0, currentY, inRect.width, labelHeight);
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.Font = Config.labelFont;
                Widgets.Label(labelRect, def.label);
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                
                if (Config.drawBorders)
                {
                    Widgets.DrawBox(labelRect);
                }
                currentY += labelHeight + elementSpacing;
            }

            // 3. CharacterName（水平居中）
            Rect nameRect = new Rect(0, currentY, inRect.width, characterNameHeight);
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Medium;
            Widgets.Label(nameRect, def.characterName);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            
            if (Config.drawBorders)
            {
                Widgets.DrawBox(nameRect);
            }
            currentY += characterNameHeight + elementSpacing;

            // 4. Descriptions（水平居中）
            Rect textRect = new Rect((inRect.width - scaledTextWidth) / 2, currentY, scaledTextWidth, descriptionHeight);
            if (Config.drawBorders)
            {
                Widgets.DrawBox(textRect);
            }
            
            // 增加内边距
            float textInnerPadding = 15f * scale;
            Rect textInnerRect = textRect.ContractedBy(textInnerPadding);
            Widgets.LabelScrollable(textInnerRect, selectedDescription, ref scrollPosition);
            
            currentY += descriptionHeight + textOptionsSpacing;

            // 5. Options（水平居中）
            Rect optionRect = new Rect((inRect.width - scaledOptionsWidth) / 2, currentY, scaledOptionsWidth, optionsHeight);
            if (Config.drawBorders)
            {
                Widgets.DrawBox(optionRect);
            }
            
            // 增加内边距
            float optionsInnerPadding = 10f * scale;
            DrawOptions(optionRect.ContractedBy(optionsInnerPadding), def.options, scale);
        }

        // 计算缩放比例 - 使用新的布局参数
        private float CalculateScale(Rect inRect)
        {
            float virtualWidth = Mathf.Max(
                Config.newLayoutLihuiSize.x, 
                Config.newLayoutTextSize.x, 
                Config.newLayoutOptionsWidth
            );
            float scaleX = inRect.width / virtualWidth;
            return Mathf.Min(scaleX, 1.0f) * 0.85f; // 稍微减少缩放以留出更多边距
        }

        // 计算选项区域高度
        private float CalculateOptionsHeight(List<EventOption> options, float optionsWidth, float scale)
        {
            if (options == null || options.Count == 0)
                return 0f;

            float totalHeight = 0f;
            var eventVarManager = Find.World.GetComponent<EventVariableManager>();

            foreach (var option in options)
            {
                string reason;
                bool conditionsMet = AreConditionsMet(option.conditions, out reason);

                if (!conditionsMet && option.hideWhenDisabled)
                {
                    continue;
                }

                // 增加选项高度和间距
                totalHeight += 35f * scale; // 每个选项高度
                totalHeight += 8f * scale; // 选项间距
            }

            return totalHeight;
        }

        // 绘制选项
        private void DrawOptions(Rect rect, List<EventOption> options, float scale)
        {
            if (options == null || options.Count == 0)
                return;

            Listing_Standard listing = new Listing_Standard();
            listing.Begin(rect);

            // 增加选项之间的间距
            listing.verticalSpacing = 8f * scale;

            foreach (var option in options)
            {
                string reason;
                bool conditionsMet = AreConditionsMet(option.conditions, out reason);

                if (conditionsMet)
                {
                    if (listing.ButtonText(option.label))
                    {
                        HandleAction(option.optionEffects);
                    }
                }
                else
                {
                    if (option.hideWhenDisabled)
                    {
                        continue;
                    }
                    Rect buttonRect = listing.GetRect(35f * scale); // 增加按钮高度
                    Widgets.ButtonText(buttonRect, option.label, false, true, false);
                    TooltipHandler.TipRegion(buttonRect, GetDisabledReason(option, reason));
                }
            }

            listing.End();
        }

        // 滚动位置用于描述文本
        private Vector2 scrollPosition = Vector2.zero;

        private void HandleAction(List<ConditionalEffects> conditionalEffects)
        {
            if (conditionalEffects.NullOrEmpty())
            {
                return;
            }

            foreach (var ce in conditionalEffects)
            {
                if (AreConditionsMet(ce.conditions, out _))
                {
                    ce.Execute(this);
                }
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

        private string GetDisabledReason(EventOption option, string reason)
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
        
        private string FormatDescription(string description)
        {
            var eventVarManager = Find.World.GetComponent<EventVariableManager>();
            return Regex.Replace(description, @"\{(.+?)\}", match =>
            {
                string varName = match.Groups[1].Value;
                if (eventVarManager.HasVariable(varName))
                {
                    return eventVarManager.GetVariable<object>(varName)?.ToString() ?? "";
                }
                return match.Value;
            });
        }
    }
}
