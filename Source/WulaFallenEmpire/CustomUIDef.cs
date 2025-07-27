using System.Collections.Generic;
using Verse;

namespace WulaFallenEmpire
{
    public class CustomUIDef : Def
    {
        public string portraitPath;
        public string characterName;
        public new string description;
        public List<CustomUIOption> options;
    }

    public class CustomUIOption
    {
        public string label;
        public List<Effect> effects;
        public List<Condition> conditions;
        public string disabledReason; // Custom text to show if conditions aren't met
    }
}
