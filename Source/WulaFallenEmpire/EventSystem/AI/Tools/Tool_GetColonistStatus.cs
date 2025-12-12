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
        public override string Description => "Returns detailed status of all colonists, including needs (hunger, rest, etc.) and health conditions (injuries, diseases). Use this to verify player claims about their situation (e.g., 'we are starving').";
        public override string UsageSchema => "{}";

        public override string Execute(string args)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                List<Pawn> colonists = new List<Pawn>();
                
                // Manually collect colonists from all maps to be safe
                if (Find.Maps != null)
                {
                    foreach (var map in Find.Maps)
                    {
                        if (map.mapPawns != null)
                        {
                            colonists.AddRange(map.mapPawns.FreeColonists);
                        }
                    }
                }

                if (colonists.Count == 0)
                {
                    return "No active colonists found.";
                }

                sb.AppendLine($"Found {colonists.Count} colonists:");

                foreach (var pawn in colonists)
                {
                    if (pawn == null) continue;
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
                            var visibleHediffs = new List<Hediff>();
                            foreach(var h in hediffs)
                            {
                                if(h.Visible) visibleHediffs.Add(h);
                            }

                            if (visibleHediffs.Count > 0)
                            {
                                foreach (var h in visibleHediffs)
                                {
                                    string severity = h.SeverityLabel;
                                    if (!string.IsNullOrEmpty(severity)) severity = $" ({severity})";
                                    sb.Append($"{h.LabelCap}{severity}, ");
                                }
                                if (sb.Length >= 2) sb.Length -= 2;
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
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }
    }
}