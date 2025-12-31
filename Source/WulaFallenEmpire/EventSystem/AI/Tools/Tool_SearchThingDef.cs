using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using WulaFallenEmpire.EventSystem.AI.Utils;

namespace WulaFallenEmpire.EventSystem.AI.Tools
{
    public class Tool_SearchThingDef : AITool
    {
        public override string Name => "search_thing_def";
        public override string Description => "Rough-searches RimWorld ThingDefs by natural language (label/defName). Returns candidate defNames so you can use them in other tools like spawn_resources.";
        public override string UsageSchema => "{\"query\":\"Steel\",\"maxResults\":10,\"itemsOnly\":true}";
        public override Dictionary<string, object> GetParametersSchema()
        {
            var properties = new Dictionary<string, object>
            {
                ["query"] = SchemaString("Search query.", nullable: true),
                ["maxResults"] = SchemaInteger("Max candidates to return.", nullable: true),
                ["itemsOnly"] = SchemaBoolean("Restrict to item defs.", nullable: true)
            };
            return SchemaObject(properties, RequiredList("query", "maxResults", "itemsOnly"));
        }

        public override string Execute(string args)
        {
            try
            {
                var parsed = ParseJsonArgs(args);
                string query = null;
                if (TryGetString(parsed, "query", out string q)) query = q;
                if (string.IsNullOrWhiteSpace(query))
                {
                    if (!string.IsNullOrWhiteSpace(args) && !LooksLikeJson(args))
                    {
                        query = args;
                    }
                }

                if (string.IsNullOrWhiteSpace(query))
                {
                    return "Error: Missing <query>.";
                }

                int maxResults = 10;
                if (TryGetInt(parsed, "maxResults", out int mr)) maxResults = Math.Max(1, Math.Min(50, mr));

                bool itemsOnly = true;
                if (TryGetBool(parsed, "itemsOnly", out bool parsedItemsOnly)) itemsOnly = parsedItemsOnly;

                var candidates = ThingDefSearcher.Search(query, maxResults: maxResults, itemsOnly: itemsOnly, minScore: 0.15f);
                if (candidates.Count == 0)
                {
                    return $"No matches for '{query}'.";
                }

                var best = candidates[0];
                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"BEST_DEFNAME: {best.Def.defName}");
                sb.AppendLine($"BEST_LABEL: {best.Def.label}");
                sb.AppendLine($"BEST_SCORE: {best.Score:F2}");
                sb.AppendLine("CANDIDATES:");

                int idx = 1;
                foreach (var c in candidates)
                {
                    var def = c.Def;
                    string cat = def.category.ToString();
                    string ingest = def.ingestible != null ? " ingestible" : "";
                    sb.AppendLine($"{idx}. defName='{def.defName}' label='{def.label}' category={cat}{ingest} score={c.Score:F2}");
                    idx++;
                }

                // Hint for common "meal" queries where game language may be non-English.
                if (Prefs.DevMode && query.IndexOf("meal", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var mealDefs = candidates.Where(c => c.Def.ingestible != null && c.Def.defName.ToLowerInvariant().Contains("meal")).Take(5).ToList();
                    if (mealDefs.Count > 0)
                    {
                        sb.AppendLine("DEV_HINT: meal-like candidates:");
                        foreach (var c in mealDefs)
                        {
                            sb.AppendLine($"- {c.Def.defName} ({c.Def.label}) score={c.Score:F2}");
                        }
                    }
                }

                return sb.ToString().Trim();
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }
    }
}
