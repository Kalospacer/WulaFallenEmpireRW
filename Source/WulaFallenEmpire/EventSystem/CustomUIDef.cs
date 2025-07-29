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

    public class CustomUIDef : Def
    {
        public string portraitPath;
        public string characterName;
        
        // New system: list of descriptions
        public List<string> descriptions;
        public DescriptionSelectionMode descriptionMode = DescriptionSelectionMode.Random;

        // Backwards compatibility: old single description field
        [System.Obsolete("Use 'descriptions' list instead. This field is for backwards compatibility only.")]
        public new string description = null;

        public Vector2 windowSize = Vector2.zero;

        public List<CustomUIOption> options;
        public string backgroundImagePath;
        public List<Effect> onOpenEffects;
        public List<Effect> dismissEffects;

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
        }
    }

    public class CustomUIOption
    {
        public string label;
        public List<Effect> effects;
        public List<Condition> conditions;
        public string disabledReason;
    }
}
