using System;
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
        public override string UsageSchema => "<search_pawn_kind><query>string</query><maxResults>int (optional, default 10)</maxResults><minScore>float (optional, default 0.15)</minScore></search_pawn_kind>";

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

                float minScore = 0.15f;
                if (parsed.TryGetValue("minScore", out string minStr) && float.TryParse(minStr, out float ms))
                {
                    minScore = Math.Max(0.01f, Math.Min(1.0f, ms));
                }

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
