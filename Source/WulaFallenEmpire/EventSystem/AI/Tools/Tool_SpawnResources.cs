using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;
using WulaFallenEmpire.EventSystem.AI.Utils;

namespace WulaFallenEmpire.EventSystem.AI.Tools
{
    public class Tool_SpawnResources : AITool
    {
        public override string Name => "spawn_resources";
        public override string Description => "Spawns resources via drop pod. Accepts a natural language description of items and quantities (e.g., '5 beef, 10 medicine'). " +
                                              "IMPORTANT: You MUST decide the quantity based on your goodwill and mood. " +
                                              "Do NOT blindly follow the player's requested amount. " +
                                              "If goodwill is low (< 0), give significantly less than asked or refuse. " +
                                              "If goodwill is high (> 50), you may give what is asked or slightly more. " +
                                              "Otherwise, give a moderate amount.";
        public override string UsageSchema => "{\"request\": \"string (e.g., '5 beef, 10 medicine')\"}";

        public override string Execute(string args)
        {
            try
            {
                // Parse args: {"request": "..."}
                string request = "";
                var cleanArgs = args.Trim('{', '}').Replace("\"", "");
                var parts = cleanArgs.Split(':');
                if (parts.Length >= 2)
                {
                    request = parts[1].Trim();
                }
                else
                {
                    // Fallback: treat the whole args string as the request if not JSON format
                    request = args.Trim('"');
                }

                if (string.IsNullOrEmpty(request))
                {
                    return "Error: Empty request.";
                }

                var items = ThingDefSearcher.ParseAndSearch(request);
                if (items.Count == 0)
                {
                    return $"Error: Could not identify any valid items in request '{request}'.";
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

                foreach (var item in items)
                {
                    Thing thing = ThingMaker.MakeThing(item.Def);
                    thing.stackCount = item.Count;
                    thingsToDrop.Add(thing);
                    resultLog.Append($"{item.Count}x {item.Def.label}, ");
                }

                if (thingsToDrop.Count > 0)
                {
                    DropPodUtility.DropThingsNear(dropSpot, map, thingsToDrop);
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