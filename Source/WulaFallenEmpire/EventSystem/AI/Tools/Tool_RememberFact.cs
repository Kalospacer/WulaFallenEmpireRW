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
        public override string UsageSchema => "{\"fact\":\"...\",\"category\":\"misc\"}";
        public override Dictionary<string, object> GetParametersSchema()
        {
            var properties = new Dictionary<string, object>
            {
                ["fact"] = SchemaString("Fact to store.", nullable: true),
                ["category"] = SchemaString("Memory category.", nullable: true)
            };
            return SchemaObject(properties, RequiredList("fact", "category"));
        }

        public override string Execute(string args)
        {
            var argsDict = ParseJsonArgs(args);
            if (!TryGetString(argsDict, "fact", out string fact) || string.IsNullOrWhiteSpace(fact))
            {
                return "Error: 'fact' content is required.";
            }

            string category = TryGetString(argsDict, "category", out string cat) ? cat : "misc";

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
