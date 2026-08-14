using System;
using System.Threading;
using System.Threading.Tasks;
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
        public override string Description => "Searches the AI's long-term memory. If query is empty, returns recent durable memories.";
        public override Dictionary<string, object> GetParametersSchema()
        {
            var properties = new Dictionary<string, object>
            {
                ["query"] = SchemaString("Search query.", nullable: true),
                ["limit"] = SchemaInteger("Max memories to return.", nullable: true)
            };
            return SchemaObject(properties, RequiredList());
        }

        public override Task<string> ExecuteAsync(string args, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ExecuteCore(args));
        }

        private string ExecuteCore(string args)
        {
            var argsDict = ParseJsonArgs(args);
            string query = TryGetString(argsDict, "query", out string q) ? q : "";
            int limit = TryGetInt(argsDict, "limit", out int parsedLimit) ? parsedLimit : 5;

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
