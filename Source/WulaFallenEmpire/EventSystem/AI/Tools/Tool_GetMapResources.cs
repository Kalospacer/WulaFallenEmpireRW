using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using WulaFallenEmpire.EventSystem.AI.Utils;

namespace WulaFallenEmpire.EventSystem.AI.Tools
{
    public class Tool_GetMapResources : AITool
    {
        public override string Name => "get_map_resources";
        public override string Description => "Checks the player's map for specific resources or buildings. Use this to verify if the player is truly lacking something they requested (e.g., 'we need steel'). Returns inventory count and mineable deposits.";
        public override string UsageSchema => "{\"resourceName\": \"string (optional, e.g., 'Steel')\"}";

        public override string Execute(string args)
        {
            try
            {
                Map map = Find.CurrentMap;
                if (map == null) return "Error: No active map.";

                string resourceName = "";
                var cleanArgs = args.Trim('{', '}').Replace("\"", "");
                var parts = cleanArgs.Split(':');
                if (parts.Length >= 2)
                {
                    resourceName = parts[1].Trim();
                }

                StringBuilder sb = new StringBuilder();

                if (!string.IsNullOrEmpty(resourceName))
                {
                    // Specific resource check
                    var searchResult = ThingDefSearcher.ParseAndSearch(resourceName);
                    if (searchResult.Count == 0) return $"Error: Could not identify resource '{resourceName}'.";

                    ThingDef def = searchResult[0].Def;
                    sb.AppendLine($"Status for '{def.label}':");

                    // 1. Total Count on Map (Items, Buildings, etc.)
                    int totalCount = 0;
                    var things = map.listerThings.ThingsOfDef(def);
                    if (things != null)
                    {
                        foreach (var t in things)
                        {
                            totalCount += t.stackCount;
                        }
                    }
                    sb.AppendLine($"- Total Found on Map: {totalCount} (includes items on ground, in storage, and constructed buildings)");

                    // 2. Inventory Count (In Storage)
                    int inventoryCount = map.resourceCounter.GetCount(def);
                    if (inventoryCount > 0)
                    {
                        sb.AppendLine($"- In Stock (Storage): {inventoryCount}");
                    }

                    // 3. Mineable Deposits (if applicable)
                    // Find mineables that drop this
                    var mineables = DefDatabase<ThingDef>.AllDefs.Where(d => d.building != null && d.building.mineableThing == def);
                    int mineableCount = 0;
                    foreach (var mineable in mineables)
                    {
                        mineableCount += map.listerThings.ThingsOfDef(mineable).Count;
                    }
                    
                    if (mineableCount > 0)
                    {
                        sb.AppendLine($"- Mineable Deposits: Found {mineableCount} veins/blocks on map.");
                    }
                }
                else
                {
                    // General overview
                    sb.AppendLine("Map Resource Overview:");
                    
                    // Key resources
                    var keyResources = new[] { "Steel", "WoodLog", "ComponentIndustrial", "MedicineIndustrial", "MealSimple" };
                    foreach (var resName in keyResources)
                    {
                        ThingDef def = DefDatabase<ThingDef>.GetNamed(resName, false);
                        if (def != null)
                        {
                            int count = map.resourceCounter.GetCount(def);
                            sb.AppendLine($"- {def.label}: {count}");
                        }
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