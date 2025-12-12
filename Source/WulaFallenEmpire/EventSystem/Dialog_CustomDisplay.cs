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
        protected EventDef def;
        protected Texture2D portrait;
        protected Texture2D background;
        protected string selectedDescription;

        protected static EventUIConfigDef config;
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

        // 新增：自定义按钮样式设置
        private static readonly Color CustomButtonNormalColor = new Color(0.5f, 0.2f, 0.2f, 1f);
        private static readonly Color CustomButtonHoverColor = new Color(0.6f, 0.3f, 0.3f, 1f);
        private static readonly Color CustomButtonDisabledColor = new Color(0.15f, 0.15f, 0.15f, 0.6f);

        private static readonly Color CustomButtonTextNormalColor = new Color(0.9f, 0.9f, 0.9f, 1f);
        private static readonly Color CustomButtonTextHoverColor = new Color(1f, 1f, 1f, 1f);
        private static readonly Color CustomButtonTextDisabledColor = new Color(0.6f, 0.6f, 0.6f, 1f);

        // 滚动位置
        protected Vector2 descriptionScrollPosition = Vector2.zero;
        protected Vector2 optionsScrollPosition = Vector2.zero;

        // 使用配置的窗口尺寸
        public override Vector2 InitialSize
        {
            get
            {
                return def.windowSize != Vector2.zero ? def.windowSize : Config.windowSize;
            }
        }

        public Dialog_CustomDisplay(EventDef def)
        {
            this.def = def;
            
            // 关键修改：使用配置控制是否暂停游戏
            this.forcePause = Config.pauseGameOnOpen;
            
            this.absorbInputAroundWindow = false; // Allow interaction with other UI elements
            this.doCloseX = true;
            this.draggable = true; // Allow dragging
            this.resizeable = true; // Allow resizing
            
            // 根据配置设置是否绘制窗口背景和阴影
            this.doWindowBackground = Config.showMainWindow;
            this.drawShadow = Config.showMainWindow;

            var eventVarManager = Find.World.GetComponent<EventVariableManager>();
            if (!def.descriptions.NullOrEmpty())
            {
                if (def.descriptionMode == DescriptionSelectionMode.Random)
                {
                    selectedDescription = def.descriptions.RandomElement().Translate();
                }
                else
                {
                    string indexVarName = $"_seq_desc_index_{def.defName}";
                    int currentIndex = eventVarManager.GetVariable<int>(indexVarName, 0);

                    selectedDescription = def.descriptions[currentIndex].Translate();

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
            
            // 加载立绘
            if (Config.showPortrait && !def.portraitPath.NullOrEmpty())
            {
                portrait = ContentFinder<Texture2D>.Get(def.portraitPath);
            }

            // 加载背景 - 优先级：事件定义背景 > 自定义背景 > 默认背景
            string bgPath = null;
            
            // 1. 首先检查事件定义中的背景
            if (!def.backgroundImagePath.NullOrEmpty())
            {
                bgPath = def.backgroundImagePath;
            }
            // 2. 然后检查自定义背景
            else if (!string.IsNullOrEmpty(Config.customBackgroundImagePath))
            {
                bgPath = Config.customBackgroundImagePath;
            }
            // 3. 最后检查是否使用默认背景
            else if (Config.useDefaultBackground)
            {
                // 这里可以设置一个默认背景路径，或者留空
                // bgPath = "UI/Backgrounds/DefaultEventBackground";
            }
            
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
                        selectedDescription += "\n\n" + condDesc.text.Translate();
                    }
                }
            }
            
            selectedDescription = FormatDescription(selectedDescription);
        }

        public override void DoWindowContents(Rect inRect)
        {
            // 绘制自定义背景（如果有）
            if (background != null)
            {
                GUI.DrawTexture(inRect, background, ScaleMode.StretchToFill);
            }

            float currentY = 0f;

            // 1. 立绘
            if (Config.showPortrait)
            {
                currentY += Config.GetScaledMargin(Config.portraitMargins.x, inRect);
                
                Rect portraitRect = Config.GetScaledRect(Config.portraitSize, inRect);
                portraitRect.y = currentY;
                
                if (portrait != null)
                {
                    // 保持图片比例
                    float aspectRatio = (float)portrait.width / portrait.height;
                    float portraitWidth = portraitRect.height * aspectRatio;
                    
                    // 居中显示
                    Rect centeredPortraitRect = new Rect(
                        portraitRect.center.x - portraitWidth / 2,
                        portraitRect.y,
                        portraitWidth,
                        portraitRect.height
                    );
                    
                    GUI.DrawTexture(centeredPortraitRect, portrait, ScaleMode.ScaleToFit);
                }
                
                if (Config.drawBorders)
                {
                    Widgets.DrawBox(portraitRect);
                }
                
                currentY += portraitRect.height + Config.GetScaledMargin(Config.portraitMargins.y, inRect);
            }

            // 2. Label
            if (Config.showLabel)
            {
                currentY += Config.GetScaledMargin(Config.labelMargins.x, inRect);
                
                Rect labelRect = Config.GetScaledRect(Config.labelSize, inRect);
                labelRect.y = currentY;
                
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.Font = Config.labelFont;
                Widgets.Label(labelRect, def.label.Translate());
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                
                if (Config.drawBorders)
                {
                    Widgets.DrawBox(labelRect);
                }
                
                currentY += labelRect.height + Config.GetScaledMargin(Config.labelMargins.y, inRect);
            }

            // 3. CharacterName
            if (Config.showCharacterName)
            {
                currentY += Config.GetScaledMargin(Config.characterNameMargins.x, inRect);
                
                Rect nameRect = Config.GetScaledRect(Config.characterNameSize, inRect);
                nameRect.y = currentY;
                
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.Font = GameFont.Medium;
                Widgets.Label(nameRect, def.characterName.Translate());
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                
                if (Config.drawBorders)
                {
                    Widgets.DrawBox(nameRect);
                }
                
                currentY += nameRect.height + Config.GetScaledMargin(Config.characterNameMargins.y, inRect);
            }

            // 4. 描述 - 修复滚动问题
            if (Config.showDescriptions)
            {
                currentY += Config.GetScaledMargin(Config.descriptionsMargins.x, inRect);
                
                Rect descriptionRect = Config.GetScaledRect(Config.descriptionsSize, inRect);
                descriptionRect.y = currentY;
                
                if (Config.drawBorders)
                {
                    Widgets.DrawBox(descriptionRect);
                }
                
                // 应用描述区域内边距
                Vector2 scaledDescriptionsPadding = Config.GetScaledDescriptionsPadding(descriptionRect);
                Rect descriptionInnerRect = descriptionRect.ContractedBy(scaledDescriptionsPadding.y, scaledDescriptionsPadding.x);
                
                // 修复：使用正确的滚动视图设置，只显示纵向滚动条
                DrawDescriptionScrollView(descriptionInnerRect, selectedDescription);
                
                currentY += descriptionRect.height + Config.GetScaledMargin(Config.descriptionsMargins.y, inRect);
            }

            // 5. 选项列表
            if (Config.showOptions)
            {
                currentY += Config.GetScaledMargin(Config.optionsListMargins.x, inRect);
                
                Rect optionsRect = Config.GetScaledRect(Config.optionsListSize, inRect);
                optionsRect.y = currentY;
                
                if (Config.drawBorders)
                {
                    Widgets.DrawBox(optionsRect);
                }
                
                DrawOptions(optionsRect, def.options);
                
                currentY += optionsRect.height + Config.GetScaledMargin(Config.optionsListMargins.y, inRect);
            }

            // 调试信息
            if (Config.showDefName)
            {
                Text.Font = GameFont.Tiny;
                GUI.color = Color.gray;
                Widgets.Label(new Rect(5, 5, inRect.width - 10, 20f), def.defName);
                GUI.color = Color.white;
            }
        }

        /// <summary>
        /// 修复的描述区域滚动视图 - 只显示纵向滚动条
        /// </summary>
        protected virtual void DrawDescriptionScrollView(Rect outRect, string text)
        {
            try
            {
                // 计算文本高度 - 使用outRect的宽度（减去滚动条宽度）来计算
                float scrollbarWidth = 16f; // 滚动条的标准宽度
                float textAreaWidth = outRect.width - scrollbarWidth;
                
                // 确保文本区域宽度为正数
                if (textAreaWidth <= 0)
                    textAreaWidth = outRect.width;
                
                float textHeight = Text.CalcHeight(text, textAreaWidth);
                
                // 创建视图矩形 - 宽度设置为文本区域宽度，高度为计算出的文本高度
                Rect viewRect = new Rect(0f, 0f, textAreaWidth, Mathf.Max(textHeight, outRect.height));
                
                // 开始滚动视图 - 只显示纵向滚动条
                Widgets.BeginScrollView(outRect, ref descriptionScrollPosition, viewRect, false);
                {
                    // 绘制文本 - 使用视图矩形的宽度确保文本正确换行
                    Rect textRect = new Rect(0f, 0f, viewRect.width, viewRect.height);
                    Widgets.Label(textRect, text);
                }
                Widgets.EndScrollView();
            }
            catch (Exception ex)
            {
                // 错误处理：如果滚动视图出现问题，回退到简单标签
                Log.Warning($"[CustomDisplay] Error in description scroll view: {ex.Message}");
                Widgets.Label(outRect, text);
            }
        }

        // 绘制单个选项 - 使用自定义样式
        protected virtual void DrawSingleOption(Rect rect, EventOption option)
        {
            string reason;
            bool conditionsMet = AreConditionsMet(option.conditions, out reason);

            // 水平居中选项
            float optionWidth = Mathf.Min(rect.width, Config.optionSize.x * (rect.width / Config.windowSize.x));
            float optionX = rect.x + (rect.width - optionWidth) / 2;
            Rect optionRect = new Rect(optionX, rect.y, optionWidth, rect.height);
            
            // 保存原始状态
            Color originalColor = GUI.color;
            GameFont originalFont = Text.Font;
            Color originalTextColor = GUI.contentColor;
            TextAnchor originalAnchor = Text.Anchor;
            
            try
            {
                // 设置文本居中
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.Font = GameFont.Small;
                
                if (conditionsMet)
                {
                    // 启用状态的选项 - 使用自定义样式
                    if (option.useCustomColors)
                    {
                        // 使用选项自定义颜色
                        DrawCustomButtonWithColors(optionRect, option.label.Translate(), option);
                    }
                    else
                    {
                        // 使用默认自定义颜色
                        DrawCustomButton(optionRect, option.label.Translate(), isEnabled: true);
                    }
                    // 添加点击处理
                    if (Widgets.ButtonInvisible(optionRect))
                    {
                        HandleAction(option.optionEffects);
                    }
                }
                else
                {
                    // 禁用状态的选项 - 使用自定义禁用样式
                    if (option.useCustomColors && option.disabledColor.HasValue)
                    {
                        // 使用选项自定义禁用颜色
                        DrawCustomButtonWithColors(optionRect, option.label.Translate(), option, isEnabled: false);
                    }
                    else
                    {
                        // 使用默认自定义禁用颜色
                        DrawCustomButton(optionRect, option.label.Translate(), isEnabled: false);
                    }
                    // 添加禁用提示
                    TooltipHandler.TipRegion(optionRect, GetDisabledReason(option, reason).Translate());
                }
            }
            finally
            {
                // 恢复原始状态
                GUI.color = originalColor;
                Text.Font = originalFont;
                GUI.contentColor = originalTextColor;
                Text.Anchor = originalAnchor;
            }
        }

        /// <summary>
        /// 绘制自定义按钮（基础版本）
        /// </summary>
        private void DrawCustomButton(Rect rect, string label, bool isEnabled = true)
        {
            bool isMouseOver = Mouse.IsOver(rect);

            // 确定按钮状态颜色
            Color buttonColor, textColor;

            if (!isEnabled)
            {
                // 禁用状态
                buttonColor = CustomButtonDisabledColor;
                textColor = CustomButtonTextDisabledColor;
            }
            else if (isMouseOver)
            {
                // 悬停状态
                buttonColor = CustomButtonHoverColor;
                textColor = CustomButtonTextHoverColor;
            }
            else
            {
                // 正常状态
                buttonColor = CustomButtonNormalColor;
                textColor = CustomButtonTextNormalColor;
            }
            
            // 绘制按钮背景
            GUI.color = buttonColor;
            Widgets.DrawBoxSolid(rect, buttonColor);

            // 绘制边框
            if (isEnabled)
            {
                Widgets.DrawBox(rect, 1);
            }
            else
            {
                // 禁用状态的边框更细更暗
                Widgets.DrawBox(rect, 1);
            }
            
            // 绘制文本
            GUI.color = textColor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect.ContractedBy(4f), label);

            // 如果是禁用状态，添加删除线效果
            if (!isEnabled)
            {
                GUI.color = new Color(0.6f, 0.6f, 0.6f, 0.8f);
                Widgets.DrawLine(
                    new Vector2(rect.x + 10f, rect.center.y),
                    new Vector2(rect.xMax - 10f, rect.center.y),
                    GUI.color,
                    1f
                );
            }
        }

        /// <summary>
        /// 绘制自定义按钮（使用选项自定义颜色）
        /// </summary>
        private void DrawCustomButtonWithColors(Rect rect, string label, EventOption option, bool isEnabled = true)
        {
            bool isMouseOver = Mouse.IsOver(rect);

            // 确定按钮状态颜色
            Color buttonColor, textColor;

            if (!isEnabled)
            {
                // 禁用状态
                buttonColor = option.disabledColor ?? CustomButtonDisabledColor;
                textColor = option.textDisabledColor ?? CustomButtonTextDisabledColor;
            }
            else if (isMouseOver)
            {
                // 悬停状态
                buttonColor = option.hoverColor ?? CustomButtonHoverColor;
                textColor = option.textHoverColor ?? CustomButtonTextHoverColor;
            }
            else
            {
                // 正常状态
                buttonColor = option.normalColor ?? CustomButtonNormalColor;
                textColor = option.textColor ?? CustomButtonTextNormalColor;
            }
            
            // 绘制按钮背景
            GUI.color = buttonColor;
            Widgets.DrawBoxSolid(rect, buttonColor);

            // 绘制边框
            Widgets.DrawBox(rect);
            
            // 绘制文本
            GUI.color = textColor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect.ContractedBy(4f), label);

            // 如果是禁用状态，添加删除线效果
            if (!isEnabled)
            {
                GUI.color = new Color(0.6f, 0.6f, 0.6f, 0.8f);
                Widgets.DrawLine(
                    new Vector2(rect.x + 10f, rect.center.y),
                    new Vector2(rect.xMax - 10f, rect.center.y),
                    GUI.color,
                    1f
                );
            }
        }

        // 绘制选项区域
        protected virtual void DrawOptions(Rect rect, List<EventOption> options)
        {
            if (options == null || options.Count == 0)
                return;

            // 应用选项列表内边距
            Vector2 scaledPadding = Config.GetScaledOptionsListPadding(rect);
            Rect optionsInnerRect = rect.ContractedBy(scaledPadding.x, scaledPadding.y);
            
            // 计算缩放后的选项尺寸和间距
            Vector2 scaledOptionSize = Config.GetScaledOptionSize(optionsInnerRect);
            float scaledOptionSpacing = Config.GetScaledOptionSpacing(optionsInnerRect);
            
            // 计算可见的选项
            var visibleOptions = new List<EventOption>();
            var eventVarManager = Find.World.GetComponent<EventVariableManager>();

            foreach (var option in options)
            {
                string reason;
                bool conditionsMet = AreConditionsMet(option.conditions, out reason);

                if (!conditionsMet && option.hideWhenDisabled)
                {
                    continue;
                }
                visibleOptions.Add(option);
            }

            if (visibleOptions.Count == 0)
                return;

            // 计算选项列表的总高度
            float totalOptionsHeight = (scaledOptionSize.y * visibleOptions.Count) + 
                                     (scaledOptionSpacing * (visibleOptions.Count - 1));
            
            bool needsScroll = totalOptionsHeight > optionsInnerRect.height;
            
            // 如果需要滚动，使用滚动视图
            if (needsScroll)
            {
                Rect viewRect = new Rect(0, 0, optionsInnerRect.width - 20f, totalOptionsHeight);
                Widgets.BeginScrollView(optionsInnerRect, ref optionsScrollPosition, viewRect);
                
                float currentY = 0f;
                foreach (var option in visibleOptions)
                {
                    DrawSingleOption(new Rect(0, currentY, viewRect.width, scaledOptionSize.y), option);
                    currentY += scaledOptionSize.y + scaledOptionSpacing;
                }
                
                Widgets.EndScrollView();
            }
            else
            {
                // 不需要滚动，垂直居中显示所有选项
                float totalHeight = (scaledOptionSize.y * visibleOptions.Count) + 
                                  (scaledOptionSpacing * (visibleOptions.Count - 1));
                float startY = optionsInnerRect.y + (optionsInnerRect.height - totalHeight) / 2;
                
                float currentY = startY;
                foreach (var option in visibleOptions)
                {
                    DrawSingleOption(new Rect(optionsInnerRect.x, currentY, optionsInnerRect.width, scaledOptionSize.y), option);
                    currentY += scaledOptionSize.y + scaledOptionSpacing;
                }
            }
        }

        // 应用选项颜色
        private void ApplyOptionColors(EventOption option, Rect rect)
        {
            if (!option.useCustomColors)
                return;

            // 检查鼠标是否悬停在选项上
            bool isMouseOver = Mouse.IsOver(rect);

            // 设置按钮背景颜色
            if (isMouseOver && option.hoverColor.HasValue)
            {
                GUI.color = option.hoverColor.Value;
            }
            else if (option.normalColor.HasValue)
            {
                GUI.color = option.normalColor.Value;
            }

            // 设置文本颜色
            if (isMouseOver && option.textHoverColor.HasValue)
            {
                GUI.contentColor = option.textHoverColor.Value;
            }
            else if (option.textColor.HasValue)
            {
                GUI.contentColor = option.textColor.Value;
            }
        }

        protected virtual void HandleAction(List<ConditionalEffects> conditionalEffects)
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

        protected bool AreConditionsMet(List<ConditionBase> conditions, out string reason)
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

        protected string GetDisabledReason(EventOption option, string reason)
        {
            if (!option.disabledReason.NullOrEmpty())
            {
                return option.disabledReason.Translate();
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
