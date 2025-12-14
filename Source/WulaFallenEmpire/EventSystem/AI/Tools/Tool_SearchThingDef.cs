using System;
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
        public override string UsageSchema => "<search_thing_def><query>string</query><maxResults>int (optional, default 10)</maxResults><itemsOnly>true/false (optional, default true)</itemsOnly></search_thing_def>";

        public override string Execute(string args)
        {
            try
            {
                var parsed = ParseXmlArgs(args);
                string query = null;
                if (parsed.TryGetValue("query", out string q)) query = q;
                if (string.IsNullOrWhiteSpace(query))
                {
                    if (!string.IsNullOrWhiteSpace(args) && !args.Trim().StartsWith("<"))
                    {
                        query = args;
                    }
                }

                if (string.IsNullOrWhiteSpace(query))
                {
                    return "Error: Missing <query>.";
                }

                int maxResults = 10;
                if (parsed.TryGetValue("maxResults", out string maxStr) && int.TryParse(maxStr, out int mr))
                {
                    maxResults = Math.Max(1, Math.Min(50, mr));
                }

                bool itemsOnly = true;
                if (parsed.TryGetValue("itemsOnly", out string itemsOnlyStr) && bool.TryParse(itemsOnlyStr, out bool parsedItemsOnly))
                {
                    itemsOnly = parsedItemsOnly;
                }

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

