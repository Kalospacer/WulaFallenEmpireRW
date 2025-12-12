using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace WulaFallenEmpire.EventSystem.AI.Tools
{
    public class Tool_GetColonistStatus : AITool
    {
        public override string Name => "get_colonist_status";
        public override string Description => "Returns detailed status of colonists. Can be filtered to find the colonist in the worst condition (e.g., lowest mood, most injured). This helps the AI understand the colony's state without needing to know specific names.";
        public override string UsageSchema => "{'filter': 'string (optional, can be 'lowest_mood', 'most_injured', 'hungriest', 'most_tired')'}";

        public override string Execute(string args)
        {
            try
            {
                string filter = null;
                if (!string.IsNullOrEmpty(args))
                {
                    var json = SimpleJsonParser.Parse(args);
                    if (json != null && json.TryGetValue("filter", out var filterObj) && filterObj is string filterStr)
                    {
                        filter = filterStr.ToLower();
                    }
                }

                List<Pawn> allColonists = new List<Pawn>();
                if (Find.Maps != null)
                {
                    foreach (var map in Find.Maps)
                    {
                        if (map.mapPawns != null)
                        {
                            allColonists.AddRange(map.mapPawns.FreeColonists);
                        }
                    }
                }

                if (allColonists.Count == 0)
                {
                    return "No active colonists found.";
                }

                List<Pawn> colonistsToReport = new List<Pawn>();

                if (string.IsNullOrEmpty(filter))
                {
                    colonistsToReport.AddRange(allColonists);
                }
                else
                {
                    Pawn targetPawn = null;
                    switch (filter)
                    {
                        case "lowest_mood":
                            targetPawn = allColonists.Where(p => p.needs?.mood != null).OrderBy(p => p.needs.mood.CurLevelPercentage).FirstOrDefault();
                            break;
                        case "most_injured":
                            targetPawn = allColonists.Where(p => p.health?.summaryHealth != null).OrderBy(p => p.health.summaryHealth.SummaryHealthPercent).FirstOrDefault();
                            break;
                        case "hungriest":
                            targetPawn = allColonists.Where(p => p.needs?.food != null).OrderBy(p => p.needs.food.CurLevelPercentage).FirstOrDefault();
                            break;
                        case "most_tired":
                            targetPawn = allColonists.Where(p => p.needs?.rest != null).OrderBy(p => p.needs.rest.CurLevelPercentage).FirstOrDefault();
                            break;
                    }
                    if (targetPawn != null)
                    {
                        colonistsToReport.Add(targetPawn);
                    }
                }

                if (colonistsToReport.Count == 0)
                {
                    return string.IsNullOrEmpty(filter) ? "No active colonists found." : $"No colonist found for filter '{filter}'. This could be because all colonists are healthy or their needs are met.";
                }

                StringBuilder sb = new StringBuilder();
                sb.AppendLine(string.IsNullOrEmpty(filter)
                    ? $"Found {colonistsToReport.Count} colonists:"
                    : $"Reporting on colonist with {filter.Replace("_", " ")}:");

                foreach (var pawn in colonistsToReport)
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
            sb.AppendLine($"- {pawn.Name.ToStringShort} ({pawn.def.label}, Age {pawn.ageTracker.AgeBiologicalYears}):");
            
            // Needs
            if (pawn.needs != null)
            {
                sb.Append("  Needs: ");
                bool anyNeedLow = false;
                foreach (var need in pawn.needs.AllNeeds)
                {
                    if (need.CurLevelPercentage < 0.3f) // Report low needs
                    {
                        sb.Append($"{need.LabelCap} ({need.CurLevelPercentage:P0}), ");
                        anyNeedLow = true;
                    }
                }
                if (!anyNeedLow) sb.Append("All needs satisfied. ");
                else sb.Length -= 2; // Remove trailing comma
                sb.AppendLine();
            }

            // Health
            if (pawn.health != null)
            {
                sb.Append("  Health: ");
                var hediffs = pawn.health.hediffSet.hediffs;
                if (hediffs != null && hediffs.Count > 0)
                {
                    var visibleHediffs = hediffs.Where(h => h.Visible).ToList();

                    if (visibleHediffs.Count > 0)
                    {
                        foreach (var h in visibleHediffs)
                        {
                            string severity = h.SeverityLabel;
                            if (!string.IsNullOrEmpty(severity)) severity = $" ({severity})";
                            sb.Append($"{h.LabelCap}{severity}, ");
                        }
                        sb.Length -= 2;
                    }
                    else
                    {
                        sb.Append("Healthy.");
                    }
                }
                else
                {
                    sb.Append("Healthy.");
                }
                
                // Bleeding
                if (pawn.health.hediffSet.BleedRateTotal > 0.01f)
                {
                    sb.Append($" [Bleeding: {pawn.health.hediffSet.BleedRateTotal:P0}/day]");
                }
                sb.AppendLine();
            }
            
            // Mood
            if (pawn.needs?.mood != null)
            {
                sb.AppendLine($"  Mood: {pawn.needs.mood.CurLevelPercentage:P0} ({pawn.needs.mood.MoodString})");
            }

            // Equipment
            if (pawn.equipment?.Primary != null)
            {
                sb.AppendLine($"  Weapon: {pawn.equipment.Primary.LabelCap}");
            }

            // Apparel
            if (pawn.apparel?.WornApparelCount > 0)
            {
                sb.Append("  Apparel: ");
                foreach (var apparel in pawn.apparel.WornApparel)
                {
                    sb.Append($"{apparel.LabelCap}, ");
                }
                sb.Length -= 2; // Remove trailing comma
                sb.AppendLine();
            }

            // Inventory
            if (pawn.inventory != null && pawn.inventory.innerContainer.Count > 0)
            {
                sb.Append("  Inventory: ");
                foreach (var item in pawn.inventory.innerContainer)
                {
                    sb.Append($"{item.LabelCap}, ");
                }
                sb.Length -= 2; // Remove trailing comma
                sb.AppendLine();
            }
        }
    }
}