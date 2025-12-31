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
        public override string Description => "Scans the current map and lists pawns (including corpses). Supports filtering by relation (friendly/hostile/neutral), type (colonist/animal/mech/humanlike), and status (prisoner/slave/guest/wild/downed/dead).";
        public override string UsageSchema =>
            "{\"filter\":\"friendly,hostile,colonist\",\"includeDead\":true,\"maxResults\":50}";
        public override Dictionary<string, object> GetParametersSchema()
        {
            var properties = new Dictionary<string, object>
            {
                ["filter"] = SchemaString("Comma-separated filters (friendly, hostile, colonist, etc.).", nullable: true),
                ["includeDead"] = SchemaBoolean("Include corpses.", nullable: true),
                ["maxResults"] = SchemaInteger("Max results to show.", nullable: true)
            };
            return SchemaObject(properties, RequiredList("filter", "includeDead", "maxResults"));
        }

        private struct MapPawnEntry
        {
            public Pawn Pawn;
            public bool IsDead;
            public IntVec3 Position;
        }

        public override string Execute(string args)
        {
            try
            {
                var parsed = ParseJsonArgs(args);

                string filterRaw = null;
                if (TryGetString(parsed, "filter", out string f)) filterRaw = f;

                int maxResults = 50;
                if (TryGetInt(parsed, "maxResults", out int mr)) maxResults = Math.Max(1, Math.Min(200, mr));

                bool includeDead = true;
                if (TryGetBool(parsed, "includeDead", out bool parsedIncludeDead)) includeDead = parsedIncludeDead;

                Map map = Find.CurrentMap;
                if (map == null) return "Error: No active map.";

                var filters = ParseFilters(filterRaw);
                if (filters.Contains("dead")) includeDead = true;

                var entries = new List<MapPawnEntry>();

                var livePawns = map.mapPawns?.AllPawnsSpawned?.Where(p => p != null).ToList() ?? new List<Pawn>();
                foreach (var pawn in livePawns)
                {
                    entries.Add(new MapPawnEntry
                    {
                        Pawn = pawn,
                        IsDead = pawn.Dead,
                        Position = pawn.Position
                    });
                }

                if (includeDead && map.listerThings != null)
                {
                    var corpses = map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse);
                    if (corpses != null)
                    {
                        foreach (var thing in corpses)
                        {
                            if (thing is not Corpse corpse) continue;
                            Pawn inner = corpse.InnerPawn;
                            if (inner == null) continue;

                            entries.Add(new MapPawnEntry
                            {
                                Pawn = inner,
                                IsDead = true,
                                Position = corpse.Position
                            });
                        }
                    }
                }

                entries = entries
                    .Where(e => e.Pawn != null)
                    .GroupBy(e => e.Pawn.thingIDNumber)
                    .Select(g => g.First())
                    .Where(e => includeDead || !e.IsDead)
                    .Where(e => MatchesFilters(e, filters))
                    .ToList();

                if (entries.Count == 0) return "No pawns matched.";

                int matched = entries.Count;
                var selected = entries
                    .OrderByDescending(e => IsHostileToPlayer(e.Pawn))
                    .ThenBy(e => e.IsDead) // living first
                    .ThenByDescending(e => e.Pawn.RaceProps?.Humanlike ?? false)
                    .ThenBy(e => e.Pawn.def?.label ?? "")
                    .ThenBy(e => e.Pawn.Name?.ToStringShort ?? "")
                    .Take(maxResults)
                    .ToList();

                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"Found {matched} matching pawns on map (showing {selected.Count}):");

                foreach (var entry in selected)
                {
                    AppendPawnLine(sb, entry);
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

            var parts = filterRaw.Split(new[] { ',', '\uFF0C', ';', '\u3001', '|' }, StringSplitOptions.RemoveEmptyEntries);
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
                else if (token == "死亡" || token == "尸体") token = "dead";

                set.Add(token);
            }
            return set;
        }

        private static bool MatchesFilters(MapPawnEntry entry, HashSet<string> filters)
        {
            if (filters == null || filters.Count == 0) return true;

            Pawn pawn = entry.Pawn;
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
                    "dead" => entry.IsDead || pawn.Dead,
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
            if (pawn.Faction == null) return true;
            if (pawn.Faction == Faction.OfPlayer) return false;
            return !pawn.HostileTo(Faction.OfPlayer);
        }

        private static void AppendPawnLine(StringBuilder sb, MapPawnEntry entry)
        {
            Pawn pawn = entry.Pawn;
            string name = pawn.Name?.ToStringShort ?? pawn.LabelShortCap;
            string kind = pawn.def?.label ?? "unknown";
            string faction = pawn.Faction?.Name ?? (pawn.RaceProps?.Animal == true ? "Wild" : "None");
            string relation = IsHostileToPlayer(pawn) ? "Hostile" : (pawn.Faction == Faction.OfPlayer ? "Player" : "Non-hostile");
            string tags = BuildTags(pawn, entry.IsDead);
            string pos = entry.Position.IsValid ? entry.Position.ToString() : (pawn.Position.IsValid ? pawn.Position.ToString() : "?");

            sb.Append($"- {name} ({kind})");
            sb.Append($" faction={faction} relation={relation} pos={pos}");
            if (!string.IsNullOrEmpty(tags)) sb.Append($" tags=[{tags}]");
            sb.AppendLine();
        }

        private static string BuildTags(Pawn pawn, bool isDead)
        {
            var tags = new List<string>();
            if (pawn.IsFreeColonist) tags.Add("colonist");
            if (pawn.IsPrisonerOfColony) tags.Add("prisoner");
            if (pawn.IsSlaveOfColony) tags.Add("slave");
            if (pawn.guest != null && pawn.Faction != null && pawn.Faction != Faction.OfPlayer) tags.Add("guest");
            if (pawn.Downed) tags.Add("downed");
            if (isDead || pawn.Dead) tags.Add("dead");
            if (pawn.InMentalState) tags.Add("mental");
            if (pawn.Drafted) tags.Add("drafted");
            if (pawn.RaceProps?.Humanlike ?? false) tags.Add("humanlike");
            if (pawn.RaceProps?.Animal ?? false) tags.Add("animal");
            if (pawn.RaceProps?.IsMechanoid ?? false) tags.Add("mech");
            return string.Join(", ", tags.Distinct());
        }
    }
}
