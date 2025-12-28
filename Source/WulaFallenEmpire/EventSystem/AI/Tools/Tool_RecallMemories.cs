using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace WulaFallenEmpire.EventSystem.AI.Tools
{
    public class Tool_RecallMemories : AITool
    {
        public override string Name => "recall_memories";
        public override string Description => "Searches the AI's long-term memory for facts matching a specific query or keyword.";
        public override string UsageSchema => "<recall_memories><query>Search keywords</query><limit>optional_int_max_results</limit></recall_memories>";

        public override string Execute(string args)
        {
            var argsDict = ParseXmlArgs(args);
            string query = argsDict.TryGetValue("query", out string q) ? q : "";
            string limitStr = argsDict.TryGetValue("limit", out string lStr) ? lStr : "5";
            
            int limit = 5;
            if (int.TryParse(limitStr, out int parsedLimit))
            {
                limit = parsedLimit;
            }

            var memoryManager = Find.World?.GetComponent<AIMemoryManager>();
            if (memoryManager == null)
            {
                return "Error: AIMemoryManager world component not found.";
            }

            if (string.IsNullOrWhiteSpace(query))
            {
                var recent = memoryManager.GetRecentMemories(limit);
                if (recent.Count == 0) return "No recent memories found.";
                return FormatMemories(recent);
            }

            var results = memoryManager.SearchMemories(query, limit);
            if (results.Count == 0)
            {
                return "No memories found matching the query.";
            }

            return FormatMemories(results);
        }

        private string FormatMemories(List<AIMemoryEntry> memories)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Found Memories:");
            foreach (var m in memories)
            {
                sb.AppendLine($"- [{m.Category}] {m.Fact} (ID: {m.Id})");
            }
            return sb.ToString();
        }
    }
}
