using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using RimWorld;
using Verse;
using WulaFallenEmpire.EventSystem.AI.Utils;

namespace WulaFallenEmpire.EventSystem.AI.Tools
{
    public class Tool_SpawnResources : AITool
    {
        public override string Name => "spawn_resources";
        public override string Description => "Spawns resources via drop pod. " +
                                              "IMPORTANT: You MUST decide the quantity based on your goodwill and mood. " +
                                              "Do NOT blindly follow the player's requested amount. " +
                                              "If goodwill is low (< 0), give significantly less than asked or refuse. " +
                                              "If goodwill is high (> 50), you may give what is asked or slightly more. " +
                                              "Otherwise, give a moderate amount.";
        public override string UsageSchema => "<spawn_resources><items><item><name>Item Name</name><count>Integer</count></item></items></spawn_resources>";

        public override string Execute(string args)
        {
            try
            {
                // Custom XML parsing for nested items
                var itemsToSpawn = new List<(ThingDef def, int count)>();
                
                // Match all <item>...</item> blocks
                var itemMatches = Regex.Matches(args, @"<item>(.*?)</item>", RegexOptions.Singleline);
                
                foreach (Match match in itemMatches)
                {
                    string itemXml = match.Groups[1].Value;
                    
                    // Extract name (supports <name> or <defName> for backward compatibility)
                    string name = "";
                    var nameMatch = Regex.Match(itemXml, @"<name>(.*?)</name>");
                    if (nameMatch.Success)
                    {
                        name = nameMatch.Groups[1].Value;
                    }
                    else
                    {
                        var defNameMatch = Regex.Match(itemXml, @"<defName>(.*?)</defName>");
                        if (defNameMatch.Success) name = defNameMatch.Groups[1].Value;
                    }

                    if (string.IsNullOrEmpty(name)) continue;

                    // Extract count
                    var countMatch = Regex.Match(itemXml, @"<count>(.*?)</count>");
                    if (!countMatch.Success) continue;
                    if (!int.TryParse(countMatch.Groups[1].Value, out int count)) continue;

                    // Search for ThingDef
                    ThingDef def = null;
                    
                    // 1. Try exact defName match
                    def = DefDatabase<ThingDef>.GetNamed(name, false);
                    
                    // 2. Try exact label match (case-insensitive)
                    if (def == null)
                    {
                        foreach (var d in DefDatabase<ThingDef>.AllDefs)
                        {
                            if (d.label != null && d.label.Equals(name, StringComparison.OrdinalIgnoreCase))
                            {
                                def = d;
                                break;
                            }
                        }
                    }

                    // 3. Try fuzzy search
                    if (def == null)
                    {
                        var searchResult = ThingDefSearcher.ParseAndSearch(name);
                        if (searchResult.Count > 0)
                        {
                            def = searchResult[0].Def;
                        }
                    }

                    if (def != null && count > 0)
                    {
                        itemsToSpawn.Add((def, count));
                    }
                }

                if (itemsToSpawn.Count == 0)
                {
                    return "Error: No valid items found in request. Usage: <spawn_resources><items><item><name>...</name><count>...</count></item></items></spawn_resources>";
                }

                Map map = Find.CurrentMap;
                if (map == null)
                {
                    return "Error: No active map.";
                }

                IntVec3 dropSpot = DropCellFinder.TradeDropSpot(map);
                List<Thing> thingsToDrop = new List<Thing>();
                StringBuilder resultLog = new StringBuilder();
                resultLog.Append("Success: Dropped ");

                foreach (var (def, count) in itemsToSpawn)
                {
                    Thing thing = ThingMaker.MakeThing(def);
                    thing.stackCount = count;
                    thingsToDrop.Add(thing);
                    resultLog.Append($"{count}x {def.label}, ");
                }

                if (thingsToDrop.Count > 0)
                {
                    DropPodUtility.DropThingsNear(dropSpot, map, thingsToDrop);
                    
                    Faction faction = Find.FactionManager.FirstFactionOfDef(FactionDef.Named("Wula_PIA_Legion_Faction"));
                    if (faction != null)
                    {
                        Messages.Message("Wula_ResourceDrop".Translate(faction.def.defName.Named("FACTION_name")), new LookTargets(dropSpot, map), MessageTypeDefOf.PositiveEvent);
                    }
                    
                    resultLog.Length -= 2; // Remove trailing comma
                    resultLog.Append($" at {dropSpot}.");
                    return resultLog.ToString();
                }
                else
                {
                    return "Error: Failed to create items.";
                }
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }
    }
}