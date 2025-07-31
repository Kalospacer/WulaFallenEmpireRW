using System.Collections.Generic;
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
        public string characterName;
        
        // New system: list of descriptions
        public List<string> descriptions;
        public DescriptionSelectionMode descriptionMode = DescriptionSelectionMode.Random;
        public bool hiddenWindow = false;

        // Backwards compatibility: old single description field
        public new string description = null;

        public Vector2 windowSize = Vector2.zero;

        public List<EventOption> options;
        public string backgroundImagePath;
        public List<ConditionalEffects> immediateEffects;
        public List<ConditionalEffects> dismissEffects;
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
        public string label;
        public List<ConditionalEffects> optionEffects;
        public List<Condition> conditions;
        public string disabledReason;
    }

    public class ConditionalEffects
    {
        public List<Condition> conditions;
        public List<Effect> effects;
    }
}
