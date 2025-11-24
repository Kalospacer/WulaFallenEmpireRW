using System; // Add this line
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    public enum DescriptionSelectionMode
    {
        Random,
        Sequential
    }

    public class EventDef : Def
    {
        public string portraitPath;
        [MustTranslate]
        public string characterName;
        
        // New system: list of descriptions
        [MustTranslate]
        public List<string> descriptions;
        public DescriptionSelectionMode descriptionMode = DescriptionSelectionMode.Random;
        public bool hiddenWindow = false;

        // Backwards compatibility: old single description field
        [MustTranslate]
        public new string description = null;

        public Vector2 windowSize = Vector2.zero;

        public Type windowType = typeof(Dialog_CustomDisplay); // 默认窗口类型
        public List<EventOption> options;
        public string backgroundImagePath;
        public List<ConditionalEffects> immediateEffects;
        public List<ConditionalEffects> dismissEffects;
        public List<ConditionalDescription> conditionalDescriptions;
        public Color? defaultOptionColor = null;
        public Color? defaultOptionTextColor = null;

        public override void PostLoad()
        {
            base.PostLoad();
#pragma warning disable 0618
            // If the old description field is used, move its value to the new list for processing.
            if (!description.NullOrEmpty())
            {
                if (descriptions.NullOrEmpty())
                {
                    descriptions = new List<string>();
                }
                descriptions.Insert(0, description);
                description = null; // Clear the old field to prevent confusion
            }
#pragma warning restore 0618
            // If hiddenWindow is true, merge immediateEffects into dismissEffects at load time.
            if (hiddenWindow && !immediateEffects.NullOrEmpty())
            {
                if (dismissEffects.NullOrEmpty())
                {
                    dismissEffects = new List<ConditionalEffects>();
                }
                dismissEffects.AddRange(immediateEffects);
                immediateEffects = null; // Clear to prevent double execution
            }
        }
    }

    public class EventOption
    {
        [MustTranslate]
        public string label;
        public List<ConditionBase> conditions;
        [MustTranslate]
        public string disabledReason;
        public bool hideWhenDisabled = true;
        public List<ConditionalEffects> optionEffects;

        // 新增：选项颜色设置
        public Color? normalColor = null;      // 正常状态颜色
        public Color? hoverColor = null;       // 悬停状态颜色
        public Color? activeColor = null;      // 激活状态颜色
        public Color? disabledColor = null;    // 禁用状态颜色

        // 新增：文本颜色设置
        public Color? textColor = null;        // 文本颜色
        public Color? textHoverColor = null;   // 悬停时文本颜色
        public Color? textActiveColor = null;  // 激活时文本颜色
        public Color? textDisabledColor = null;// 禁用时文本颜色

        // 新增：是否使用自定义颜色
        public bool useCustomColors = false;
    }

    public class LoopEffects
    {
        public int count = 1;
        public string countVariableName;
        public List<EffectBase> effects;
    }

    public class ConditionalEffects
    {
        public List<ConditionBase> conditions;
        public List<EffectBase> effects;
        public List<EffectBase> randomlistEffects;
        public List<LoopEffects> loopEffects;

        public void Execute(Window dialog)
        {
            // Execute all standard effects
            if (!effects.NullOrEmpty())
            {
                foreach (var effect in effects)
                {
                    effect.Execute(dialog);
                }
            }

            // Execute one random effect from the random list
            if (!randomlistEffects.NullOrEmpty())
            {
                float totalWeight = randomlistEffects.Sum(e => e.weight);
                float randomPoint = Rand.Value * totalWeight;

                foreach (var effect in randomlistEffects)
                {
                    if (randomPoint < effect.weight)
                    {
                        effect.Execute(dialog);
                        break;
                    }
                    randomPoint -= effect.weight;
                }
            }

            // Execute looped effects
            if (!loopEffects.NullOrEmpty())
            {
                var eventVarManager = Find.World.GetComponent<EventVariableManager>();
                foreach (var loop in loopEffects)
                {
                    int loopCount = loop.count;
                    if (!loop.countVariableName.NullOrEmpty() && eventVarManager.HasVariable(loop.countVariableName))
                    {
                        loopCount = eventVarManager.GetVariable<int>(loop.countVariableName);
                    }

                    for (int i = 0; i < loopCount; i++)
                    {
                        if (!loop.effects.NullOrEmpty())
                        {
                            foreach (var effect in loop.effects)
                            {
                                effect.Execute(dialog);
                            }
                        }
                    }
                }
            }
        }
    }

    public class ConditionalDescription
    {
        public List<ConditionBase> conditions;
        [MustTranslate]
        public string text;
    }
}
