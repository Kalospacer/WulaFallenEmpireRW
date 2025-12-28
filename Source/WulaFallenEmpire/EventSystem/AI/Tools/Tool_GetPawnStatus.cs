using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace WulaFallenEmpire.EventSystem.AI.Tools
{
    public class Tool_GetPawnStatus : AITool
    {
        public override string Name => "get_pawn_status";
        public override string Description => "Returns detailed status (health, needs, gear) of specified pawns. Use this to check for sickness, injuries, mood, or equipment. Can filter by name, category (colonist/animal/prisoner/guest), or status (sick/injured).";
        public override string UsageSchema => "<get_pawn_status><name>optional_partial_name</name><category>colonist/animal/prisoner/guest/all (default: all)</category><filter>sick/injured/downed/dead (optional)</filter></get_pawn_status>";

        public override string Execute(string args)
        {
            try
            {
                var parsed = ParseXmlArgs(args);
                string nameTarget = parsed.TryGetValue("name", out string n) ? n.ToLower() : null;
                string category = parsed.TryGetValue("category", out string c) ? c.ToLower() : "all";
                string filter = parsed.TryGetValue("filter", out string f) ? f.ToLower() : null;

                Map map = Find.CurrentMap;
                if (map == null) return "Error: No active map.";

                List<Pawn> pawns = map.mapPawns.AllPawnsSpawned.ToList();
                var matches = new List<Pawn>();

                foreach (var pawn in pawns)
                {
                    // Filter by Category
                    bool catMatch = false;
                    switch (category)
                    {
                        case "colonist": catMatch = pawn.IsFreeColonist; break;
                        case "animal": catMatch = pawn.RaceProps.Animal; break;
                        case "prisoner": catMatch = pawn.IsPrisonerOfColony; break;
                        case "guest": catMatch = pawn.guest != null && !pawn.IsPrisoner; break;
                        case "all": catMatch = true; break;
                        default: catMatch = true; break; 
                    }
                    if (!catMatch) continue;

                    // Filter by Name
                    if (!string.IsNullOrEmpty(nameTarget))
                    {
                        string pName = pawn.Name?.ToStringFull?.ToLower() ?? pawn.LabelShort?.ToLower() ?? "";
                        if (!pName.Contains(nameTarget)) continue;
                    }

                    // Filter by Status
                    if (!string.IsNullOrEmpty(filter))
                    {
                        bool statusMatch = false;
                        if (filter == "sick")
                        {
                            // Check for visible hediffs that are bad (not implants)
                            statusMatch = pawn.health.hediffSet.hediffs.Any(h => h.Visible && h.def.isBad && !h.IsPermanent() && h.def.makesSickThought);
                        }
                        else if (filter == "injured")
                        {
                            statusMatch = pawn.health.summaryHealth.SummaryHealthPercent < 1.0f;
                        }
                        else if (filter == "downed") statusMatch = pawn.Downed;
                        else if (filter == "dead") statusMatch = pawn.Dead;
                        else statusMatch = true; // Unknown filter?

                        if (!statusMatch) continue;
                    }

                    matches.Add(pawn);
                }

                if (matches.Count == 0) return "No matching pawns found.";

                // Sort by relevance (colonists first, then sick/injured)
                matches = matches.OrderBy(p => p.RaceProps.Animal).ThenBy(p => p.health.summaryHealth.SummaryHealthPercent).Take(10).ToList();

                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"Found {matches.Count} matching pawns:");
                
                foreach (var pawn in matches)
                {
                    AppendPawnStatus(sb, pawn);
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }

        private void AppendPawnStatus(StringBuilder sb, Pawn pawn)
        {
            if (pawn == null) return;
            sb.AppendLine($"- {pawn.Name?.ToStringShort ?? pawn.LabelCap} ({pawn.def.label}, Age {pawn.ageTracker.AgeBiologicalYears}):");
            
            // Health
            if (pawn.health != null)
            {
                sb.Append("  Health: ");
                var hediffs = pawn.health.hediffSet.hediffs;
                bool anyHediff = false;
                if (hediffs != null && hediffs.Count > 0)
                {
                    var visibleHediffs = hediffs.Where(h => h.Visible).ToList();
                    foreach (var h in visibleHediffs)
                    {
                        string severity = h.SeverityLabel;
                        if (!string.IsNullOrEmpty(severity)) severity = $" ({severity})";
                        sb.Append($"{h.LabelCap}{severity}, ");
                        anyHediff = true;
                    }
                }
                if (anyHediff) sb.Length -= 2;
                else sb.Append("Healthy");
                
                // Bleeding
                if (pawn.health.hediffSet.BleedRateTotal > 0.01f)
                {
                    sb.Append($" [Bleeding: {pawn.health.hediffSet.BleedRateTotal:P0}/day]");
                }
                sb.AppendLine();
            }

            // Needs (only if applicable)
            if (pawn.needs != null && pawn.RaceProps.Humanlike)
            {
                sb.Append("  Needs: ");
                var allNeeds = pawn.needs.AllNeeds;
                if (allNeeds != null)
                {
                    var lowNeeds = allNeeds.Where(n => n.CurLevelPercentage < 0.3f).ToList();
                    if (lowNeeds.Count > 0)
                    {
                        foreach (var need in lowNeeds)
                        {
                            sb.Append($"!{need.LabelCap}: {need.CurLevelPercentage:P0}, ");
                        }
                        sb.Length -= 2;
                    }
                    else
                    {
                        sb.Append("Satisfied.");
                    }
                }
                sb.AppendLine();
            }
            
            // Mood (Humanlike)
            if (pawn.needs?.mood != null)
            {
                 sb.AppendLine($"  Mood: {pawn.needs.mood.CurLevelPercentage:P0} ({pawn.needs.mood.MoodString})");
            }

            // Current Activity/Job
            if (pawn.CurJob != null)
            {
                sb.AppendLine($"  Activity: {pawn.CurJob.def.reportString.Replace("TargetA", pawn.CurJob.targetA.Thing?.LabelShort ?? "area")}");
            }
        }
    }
}
