using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace WulaFallenEmpire.EventSystem.AI.Tools
{
    public class Tool_GetMapPawns : AITool
    {
        public override string Name => "get_map_pawns";
        public override string Description => "Scans the current map and lists pawns. Supports filtering by relation (friendly/hostile/neutral), type (colonist/animal/mechanoid/humanlike), and status (prisoner/slave/guest/downed).";
        public override string UsageSchema => "<get_map_pawns><filter>string (optional, comma-separated: friendly, hostile, neutral, colonist, animal, mech, humanlike, prisoner, slave, guest, wild, downed)</filter><maxResults>int (optional, default 50)</maxResults></get_map_pawns>";

        public override string Execute(string args)
        {
            try
            {
                var parsed = ParseXmlArgs(args);

                string filterRaw = null;
                if (parsed.TryGetValue("filter", out string f)) filterRaw = f;
                int maxResults = 50;
                if (parsed.TryGetValue("maxResults", out string maxStr) && int.TryParse(maxStr, out int mr))
                {
                    maxResults = Math.Max(1, Math.Min(200, mr));
                }

                Map map = Find.CurrentMap;
                if (map == null) return "Error: No active map.";

                var filters = ParseFilters(filterRaw);

                List<Pawn> pawns = map.mapPawns?.AllPawnsSpawned?.Where(p => p != null).ToList() ?? new List<Pawn>();
                pawns = pawns.Where(p => MatchesFilters(p, filters)).ToList();

                if (pawns.Count == 0) return "No pawns matched.";

                pawns = pawns
                    .OrderByDescending(p => IsHostileToPlayer(p))
                    .ThenByDescending(p => p.RaceProps?.Humanlike ?? false)
                    .ThenBy(p => p.def?.label ?? "")
                    .ThenBy(p => p.Name?.ToStringShort ?? "")
                    .Take(maxResults)
                    .ToList();

                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"Found {pawns.Count} pawns on map (showing up to {maxResults}):");

                foreach (var pawn in pawns)
                {
                    AppendPawnLine(sb, pawn);
                }

                return sb.ToString().TrimEnd();
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }

        private static HashSet<string> ParseFilters(string filterRaw)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(filterRaw)) return set;

            var parts = filterRaw.Split(new[] { ',', '，', ';', '、', '|' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                string token = part.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(token)) continue;

                // Chinese aliases
                if (token == "友方") token = "friendly";
                else if (token == "敌方" || token == "敌对") token = "hostile";
                else if (token == "中立") token = "neutral";
                else if (token == "动物") token = "animal";
                else if (token == "殖民者" || token == "殖民") token = "colonist";
                else if (token == "机械体" || token == "机械" || token == "机甲") token = "mech";
                else if (token == "人形" || token == "类人") token = "humanlike";
                else if (token == "囚犯") token = "prisoner";
                else if (token == "奴隶") token = "slave";
                else if (token == "访客" || token == "客人") token = "guest";
                else if (token == "野生") token = "wild";
                else if (token == "倒地" || token == "昏迷") token = "downed";

                set.Add(token);
            }
            return set;
        }

        private static bool MatchesFilters(Pawn pawn, HashSet<string> filters)
        {
            if (filters == null || filters.Count == 0) return true;

            bool anyMatched = false;
            foreach (var f in filters)
            {
                bool matched = f switch
                {
                    "friendly" => IsFriendlyToPlayer(pawn),
                    "hostile" => IsHostileToPlayer(pawn),
                    "neutral" => IsNeutralToPlayer(pawn),
                    "colonist" => pawn.IsFreeColonist,
                    "animal" => pawn.RaceProps?.Animal ?? false,
                    "mech" => pawn.RaceProps?.IsMechanoid ?? false,
                    "humanlike" => pawn.RaceProps?.Humanlike ?? false,
                    "prisoner" => pawn.IsPrisonerOfColony,
                    "slave" => pawn.IsSlaveOfColony,
                    "guest" => pawn.guest != null && pawn.Faction != null && pawn.Faction != Faction.OfPlayer,
                    "wild" => pawn.Faction == null && (pawn.RaceProps?.Animal ?? false),
                    "downed" => pawn.Downed,
                    _ => false
                };

                anyMatched |= matched;
            }

            return anyMatched;
        }

        private static bool IsHostileToPlayer(Pawn pawn)
        {
            return pawn != null && Faction.OfPlayer != null && pawn.HostileTo(Faction.OfPlayer);
        }

        private static bool IsFriendlyToPlayer(Pawn pawn)
        {
            if (pawn == null || Faction.OfPlayer == null) return false;
            if (pawn.Faction == Faction.OfPlayer) return true;
            if (pawn.Faction == null) return false;
            return !pawn.HostileTo(Faction.OfPlayer);
        }

        private static bool IsNeutralToPlayer(Pawn pawn)
        {
            if (pawn == null || Faction.OfPlayer == null) return false;
            if (pawn.Faction == null) return true; // wild/animals etc.
            if (pawn.Faction == Faction.OfPlayer) return false;
            return !pawn.HostileTo(Faction.OfPlayer);
        }

        private static void AppendPawnLine(StringBuilder sb, Pawn pawn)
        {
            string name = pawn.Name?.ToStringShort ?? pawn.LabelShortCap;
            string kind = pawn.def?.label ?? "unknown";
            string faction = pawn.Faction?.Name ?? (pawn.RaceProps?.Animal == true ? "Wild" : "None");
            string relation = IsHostileToPlayer(pawn) ? "Hostile" : (pawn.Faction == Faction.OfPlayer ? "Player" : "Non-hostile");
            string tags = BuildTags(pawn);
            string pos = pawn.Position.IsValid ? pawn.Position.ToString() : "?";

            sb.Append($"- {name} ({kind})");
            sb.Append($" faction={faction} relation={relation} pos={pos}");
            if (!string.IsNullOrEmpty(tags)) sb.Append($" tags=[{tags}]");
            sb.AppendLine();
        }

        private static string BuildTags(Pawn pawn)
        {
            var tags = new List<string>();
            if (pawn.IsFreeColonist) tags.Add("colonist");
            if (pawn.IsPrisonerOfColony) tags.Add("prisoner");
            if (pawn.IsSlaveOfColony) tags.Add("slave");
            if (pawn.guest != null && pawn.Faction != null && pawn.Faction != Faction.OfPlayer) tags.Add("guest");
            if (pawn.Downed) tags.Add("downed");
            if (pawn.InMentalState) tags.Add("mental");
            if (pawn.Drafted) tags.Add("drafted");
            if (pawn.RaceProps?.Humanlike ?? false) tags.Add("humanlike");
            if (pawn.RaceProps?.Animal ?? false) tags.Add("animal");
            if (pawn.RaceProps?.IsMechanoid ?? false) tags.Add("mech");
            return string.Join(", ", tags.Distinct());
        }
    }
}
