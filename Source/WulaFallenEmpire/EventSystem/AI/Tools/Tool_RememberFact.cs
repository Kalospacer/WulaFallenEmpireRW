using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace WulaFallenEmpire.EventSystem.AI.Tools
{
    public class Tool_RememberFact : AITool
    {
        public override string Name => "remember_fact";
        public override string Description => "Stores a specific fact or piece of information into the AI's long-term memory for future retrieval.";
        public override string UsageSchema => "<remember_fact><fact>Text content to remember</fact><category>optional_category</category></remember_fact>";

        public override string Execute(string args)
        {
            var argsDict = ParseXmlArgs(args);
            if (!argsDict.TryGetValue("fact", out string fact) || string.IsNullOrWhiteSpace(fact))
            {
                return "Error: <fact> content is required.";
            }

            string category = argsDict.TryGetValue("category", out string cat) ? cat : "misc";

            var memoryManager = Find.World?.GetComponent<AIMemoryManager>();
            if (memoryManager == null)
            {
                return "Error: AIMemoryManager world component not found.";
            }

            var entry = memoryManager.AddMemory(fact, category);
            if (entry != null)
            {
                return $"Success: Memory stored. ID: {entry.Id}";
            }
            else
            {
                return "Error: Failed to store memory.";
            }
        }
    }
}
