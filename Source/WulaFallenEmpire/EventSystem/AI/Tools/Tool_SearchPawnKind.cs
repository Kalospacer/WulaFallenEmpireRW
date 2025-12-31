using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;
using WulaFallenEmpire.EventSystem.AI.Utils;

namespace WulaFallenEmpire.EventSystem.AI.Tools
{
    public class Tool_SearchPawnKind : AITool
    {
        public override string Name => "search_pawn_kind";
        public override string Description => "Rough-searches PawnKindDefs by natural language (label/defName). Returns candidate defNames for send_reinforcement.";
        public override string UsageSchema => "{\"query\":\"escort\",\"maxResults\":10,\"minScore\":0.15}";
        public override Dictionary<string, object> GetParametersSchema()
        {
            var properties = new Dictionary<string, object>
            {
                ["query"] = SchemaString("Search query.", nullable: true),
                ["maxResults"] = SchemaInteger("Max candidates to return.", nullable: true),
                ["minScore"] = SchemaNumber("Minimum similarity score.", nullable: true)
            };
            return SchemaObject(properties, RequiredList("query", "maxResults", "minScore"));
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

                float minScore = 0.15f;
                if (TryGetFloat(parsed, "minScore", out float ms)) minScore = Math.Max(0.01f, Math.Min(1.0f, ms));

                var candidates = PawnKindDefSearcher.Search(query, maxResults: maxResults, minScore: minScore);
                if (candidates.Count == 0)
                {
                    return $"No matches for '{query}'.";
                }

                var best = candidates[0];
                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"BEST_DEFNAME: {best.Def.defName}");
                sb.AppendLine($"BEST_LABEL: {best.Def.label ?? best.Def.defName}");
                sb.AppendLine($"BEST_SCORE: {best.Score:F2}");
                sb.AppendLine("CANDIDATES:");

                int idx = 1;
                foreach (var c in candidates)
                {
                    var def = c.Def;
                    string label = def.label ?? def.defName;
                    string race = def.race != null ? def.race.defName : "None";
                    sb.AppendLine($"{idx}. defName='{def.defName}' label='{label}' race='{race}' score={c.Score:F2}");
                    idx++;
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
