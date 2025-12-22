using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using Verse.AI.Group;
using WulaFallenEmpire;

namespace WulaFallenEmpire.EventSystem.AI.Tools
{
    public class Tool_SendReinforcement : AITool
    {
        public override string Name => "send_reinforcement";
        
        public override string Description
        {
            get
            {
                StringBuilder sb = new StringBuilder();
                sb.Append("Sends military units to the player's map. If hostile, this triggers a raid. If neutral/allied, this sends reinforcements. ");

                float basePoints = 0f;
                Map map = Find.CurrentMap;
                if (map != null)
                {
                    basePoints = StorytellerUtility.DefaultThreatPointsNow(map);
                }

                int goodwill = 0;
                float goodwillFactor = 1.0f;
                bool hostile = false;
                var eventVarManager = Find.World?.GetComponent<EventVariableManager>();
                if (eventVarManager != null)
                {
                    goodwill = eventVarManager.GetVariable<int>("Wula_Goodwill_To_PIA", 0);
                }

                Faction faction = Find.FactionManager.FirstFactionOfDef(FactionDef.Named("Wula_PIA_Legion_Faction"));
                if (faction != null)
                {
                    hostile = faction.HostileTo(Faction.OfPlayer);
                }

                if (hostile)
                {
                    if (goodwill < -50) goodwillFactor = 1.5f;
                    else if (goodwill < 0) goodwillFactor = 1.2f;
                    else if (goodwill > 50) goodwillFactor = 0.8f;
                }
                else
                {
                    if (goodwill < -50) goodwillFactor = 0.5f;
                    else if (goodwill < 0) goodwillFactor = 0.8f;
                    else if (goodwill > 50) goodwillFactor = 1.5f;
                }

                float adjustedMaxPoints = basePoints * goodwillFactor * 1.5f;

                sb.Append($"Current Raid Points Budget: {basePoints:F0}. ");
                sb.Append($"Adjusted Budget (Goodwill {goodwill}, Hostile={hostile}): {adjustedMaxPoints:F0}. ");
                sb.Append("Available Units (defName | label | cost): ");

                if (faction != null)
                {
                    var pawnKinds = DefDatabase<PawnKindDef>.AllDefs
                        .Where(pk => IsWulaPawnKind(pk, faction))
                        .Distinct()
                        .OrderBy(pk => pk.combatPower)
                        .ThenBy(pk => pk.defName)
                        .Take(40)
                        .ToList();

                    bool first = true;
                    foreach (var pk in pawnKinds)
                    {
                        string label = string.IsNullOrWhiteSpace(pk.label) ? pk.defName : pk.label;
                        if (!first) sb.Append("; ");
                        sb.Append($"{pk.defName} | {label} | {pk.combatPower:F0}");
                        first = false;
                    }
                }
                else
                {
                    sb.Append("Error: Wula_PIA_Legion_Faction not found.");
                }

                sb.Append("Usage: Provide a list of 'PawnKindDefName: Count'. Total cost must not exceed budget significantly.");
                return sb.ToString();
            }
        }

        private static bool IsWulaPawnKind(PawnKindDef pk, Faction faction)
        {
            if (pk == null) return false;
            string defName = pk.defName ?? "";
            string raceName = pk.race?.defName ?? "";

            if (defName.IndexOf("wula", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (raceName.IndexOf("wula", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (defName.IndexOf("cat", StringComparison.OrdinalIgnoreCase) >= 0) return true;

            return false;
        }

        public override string UsageSchema => "<send_reinforcement><units>string (e.g., 'Wula_PIA_Heavy_Unit_Melee: 2, Wula_PIA_Legion_Escort_Unit: 5')</units></send_reinforcement>";

        public override string Execute(string args)
        {
            try
            {
                Map map = Find.CurrentMap;
                if (map == null) return "Error: No active map.";

                Faction faction = Find.FactionManager.FirstFactionOfDef(FactionDef.Named("Wula_PIA_Legion_Faction"));
                if (faction == null) return "Error: Faction Wula_PIA_Legion_Faction not found.";

                // Parse args
                var parsedArgs = ParseXmlArgs(args);
                string unitString = "";
                
                if (parsedArgs.TryGetValue("units", out string units))
                {
                    unitString = units;
                }
                else
                {
                    // Fallback
                    if (!args.Trim().StartsWith("<"))
                    {
                        unitString = args;
                    }
                }

                var unitPairs = unitString.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                
                List<Pawn> pawnsToSpawn = new List<Pawn>();
                float totalCost = 0;
                foreach (var pair in unitPairs)
                {
                    var kv = pair.Split(':');
                    if (kv.Length != 2) continue;
                    string defName = kv[0].Trim();
                    if (!int.TryParse(kv[1].Trim(), out int count)) continue;
                    PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamed(defName, false);
                    if (kind == null) continue;
                    
                    for(int i=0; i<count; i++)
                    {
                        pawnsToSpawn.Add(PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, faction, PawnGenerationContext.NonPlayer, -1, true)));
                        totalCost += kind.combatPower;
                    }
                }

                if (pawnsToSpawn.Count == 0) return "Error: No valid units specified.";

                // Apply Goodwill modifier to points
                var eventVarManager = Find.World.GetComponent<EventVariableManager>();
                int goodwill = eventVarManager.GetVariable<int>("Wula_Goodwill_To_PIA", 0);
                float goodwillFactor = 1.0f;
                bool hostile = faction.HostileTo(Faction.OfPlayer);

                if (hostile)
                {
                    if (goodwill < -50) goodwillFactor = 1.5f;
                    else if (goodwill < 0) goodwillFactor = 1.2f;
                    else if (goodwill > 50) goodwillFactor = 0.8f;
                }
                else
                {
                    if (goodwill < -50) goodwillFactor = 0.5f;
                    else if (goodwill < 0) goodwillFactor = 0.8f;
                    else if (goodwill > 50) goodwillFactor = 1.5f;
                }

                float baseMaxPoints = StorytellerUtility.DefaultThreatPointsNow(map);
                float adjustedMaxPoints = baseMaxPoints * goodwillFactor * 1.5f;

                WulaLog.Debug($"[WulaAI] send_reinforcement: totalCost={totalCost}, adjustedMaxPoints={adjustedMaxPoints}");
                if (totalCost > adjustedMaxPoints)
                {
                    return $"Error: Total cost {totalCost} exceeds limit {adjustedMaxPoints:F0}. Reduce unit count.";
                }

                IntVec3 spawnSpot;
                
                if (hostile)
                {
                    IncidentParms parms = new IncidentParms
                    {
                        target = map,
                        points = totalCost,
                        faction = faction,
                        forced = true,
                        raidStrategy = RaidStrategyDefOf.ImmediateAttack
                    };
                    
                    if (!RCellFinder.TryFindRandomPawnEntryCell(out spawnSpot, map, CellFinder.EdgeRoadChance_Hostile))
                    {
                        spawnSpot = CellFinder.RandomEdgeCell(map);
                    }
                    parms.spawnCenter = spawnSpot;

                    // Arrive
                    PawnsArrivalModeDefOf.EdgeWalkIn.Worker.Arrive(pawnsToSpawn, parms);
                    
                    // Make Lord
                    parms.raidStrategy.Worker.MakeLords(parms, pawnsToSpawn);
                    
                    Find.LetterStack.ReceiveLetter("Raid", "The Legion has sent a raid force.", LetterDefOf.ThreatBig, pawnsToSpawn);
                    return $"Success: Raid dispatched with {pawnsToSpawn.Count} units (Cost: {totalCost}).";
                }
                else
                {
                    spawnSpot = DropCellFinder.TradeDropSpot(map);
                    DropPodUtility.DropThingsNear(spawnSpot, map, pawnsToSpawn.Cast<Thing>());
                    
                    LordMaker.MakeNewLord(faction, new LordJob_AssistColony(faction, spawnSpot), map, pawnsToSpawn);

                    Find.LetterStack.ReceiveLetter("Reinforcements", "The Legion has sent reinforcements.", LetterDefOf.PositiveEvent, pawnsToSpawn);
                    return $"Success: Reinforcements dropped with {pawnsToSpawn.Count} units (Cost: {totalCost}).";
                }
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }
    }
}
