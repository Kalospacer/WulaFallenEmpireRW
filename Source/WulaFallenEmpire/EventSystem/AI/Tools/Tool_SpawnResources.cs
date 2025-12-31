using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
                                              "Otherwise, give a moderate amount. " +
                                              "TIP: Use the `search_thing_def` tool first and then spawn by DefName to avoid language mismatch.";
        public override string UsageSchema => "{\"items\":[{\"name\":\"Steel\",\"count\":100,\"stuffDefName\":\"Steel\"}]}";
        public override Dictionary<string, object> GetParametersSchema()
        {
            var itemProperties = new Dictionary<string, object>
            {
                ["name"] = SchemaString("Thing defName or label.", nullable: true),
                ["defName"] = SchemaString("ThingDef defName alias for name.", nullable: true),
                ["count"] = SchemaInteger("Stack count.", nullable: true),
                ["stuffDefName"] = SchemaString("Stuff defName for made-from-stuff items.", nullable: true),
                ["stuff"] = SchemaString("Alias for stuffDefName.", nullable: true),
                ["material"] = SchemaString("Alias for stuffDefName.", nullable: true)
            };
            var itemSchema = SchemaObject(itemProperties, RequiredList("name", "defName", "count", "stuffDefName", "stuff", "material"));

            var properties = new Dictionary<string, object>
            {
                ["items"] = SchemaArray(itemSchema, "List of items to spawn.", nullable: true),
                ["name"] = SchemaString("Single item defName or label.", nullable: true),
                ["count"] = SchemaInteger("Single item count.", nullable: true),
                ["stuffDefName"] = SchemaString("Stuff defName for single item.", nullable: true),
                ["stuff"] = SchemaString("Alias for stuffDefName.", nullable: true),
                ["material"] = SchemaString("Alias for stuffDefName.", nullable: true)
            };
            return SchemaObject(properties, RequiredList("items", "name", "count", "stuffDefName", "stuff", "material"));
        }

        public override string Execute(string args)
        {
            try
            {
                if (args == null) args = "";

                var parsedArgs = ParseJsonArgs(args);

                var itemsToSpawn = new List<(ThingDef def, int count, string requestedName, string stuffDefName)>();
                var substitutions = new List<string>();

                if (TryGetList(parsedArgs, "items", out List<object> itemsRaw))
                {
                    foreach (var item in itemsRaw)
                    {
                        if (item is not Dictionary<string, object> itemDict) continue;

                        string name = TryGetString(itemDict, "name", out string n) ? n :
                                      (TryGetString(itemDict, "defName", out string dn) ? dn : null);

                        string stuffDefName = TryGetString(itemDict, "stuffDefName", out string sdn) ? sdn :
                                              (TryGetString(itemDict, "stuff", out string s) ? s :
                                              (TryGetString(itemDict, "material", out string m) ? m : null));

                        if (string.IsNullOrWhiteSpace(name)) continue;
                        if (!TryGetInt(itemDict, "count", out int count) || count <= 0) continue;

                        AddItem(name, count, stuffDefName, itemsToSpawn, substitutions);
                    }
                }

                if (itemsToSpawn.Count == 0)
                {
                    if (TryGetString(parsedArgs, "name", out string singleName) && TryGetInt(parsedArgs, "count", out int singleCount))
                    {
                        string stuffDefName = TryGetString(parsedArgs, "stuffDefName", out string sdn) ? sdn :
                                              (TryGetString(parsedArgs, "stuff", out string s) ? s :
                                              (TryGetString(parsedArgs, "material", out string m) ? m : null));
                        AddItem(singleName, singleCount, stuffDefName, itemsToSpawn, substitutions);
                    }
                }

                if (itemsToSpawn.Count == 0 && !LooksLikeJson(args))
                {
                    var parsed = ThingDefSearcher.ParseAndSearch(args);
                    foreach (var r in parsed)
                    {
                        if (r.Def != null && r.Count > 0)
                        {
                            itemsToSpawn.Add((r.Def, r.Count, r.Def.defName, null));
                        }
                    }
                }

                if (itemsToSpawn.Count == 0)
                {
                    string msg = "Error: No valid items found in request. Usage: {\"items\":[{\"name\":\"Steel\",\"count\":100}]}";
                    Messages.Message(msg, MessageTypeDefOf.RejectInput);
                    return msg;
                }

                Map map = GetTargetMap();
                if (map == null)
                {
                    string msg = "Error: No active map.";
                    Messages.Message(msg, MessageTypeDefOf.RejectInput);
                    return msg;
                }

                IntVec3 dropSpot = DropCellFinder.TradeDropSpot(map);
                if (!dropSpot.IsValid || !dropSpot.InBounds(map))
                {
                    dropSpot = DropCellFinder.RandomDropSpot(map);
                }
                List<Thing> thingsToDrop = new List<Thing>();
                var summary = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var skipped = new List<string>();

                ThingDef ResolveStuffDef(ThingDef productDef, string preferredStuffDefName)
                {
                    if (productDef == null || !productDef.MadeFromStuff) return null;

                    List<ThingDef> allowed = null;
                    try
                    {
                        allowed = GenStuff.AllowedStuffsFor(productDef)?.ToList();
                    }
                    catch
                    {
                        allowed = null;
                    }

                    if (!string.IsNullOrWhiteSpace(preferredStuffDefName))
                    {
                        ThingDef preferred = DefDatabase<ThingDef>.GetNamed(preferredStuffDefName.Trim(), false);
                        if (preferred != null && preferred.IsStuff && (allowed == null || allowed.Contains(preferred)))
                        {
                            return preferred;
                        }
                    }

                    ThingDef defaultStuff = null;
                    try
                    {
                        defaultStuff = GenStuff.DefaultStuffFor(productDef);
                    }
                    catch
                    {
                        defaultStuff = null;
                    }

                    if (defaultStuff != null) return defaultStuff;
                    if (allowed != null && allowed.Count > 0) return allowed[0];

                    return ThingDefOf.Steel;
                }

                void AddSummary(ThingDef def, int count, bool minified)
                {
                    if (def == null || count <= 0) return;
                    string key = minified ? $"{def.label} (minified)" : def.label;
                    if (summary.TryGetValue(key, out int existing))
                    {
                        summary[key] = existing + count;
                    }
                    else
                    {
                        summary[key] = count;
                    }
                }

                foreach (var (def, count, requestedName, preferredStuffDefName) in itemsToSpawn)
                {
                    if (def == null || count <= 0) continue;

                    if (def.category == ThingCategory.Building)
                    {
                        int created = 0;
                        for (int i = 0; i < count; i++)
                        {
                            try
                            {
                                ThingDef stuff = ResolveStuffDef(def, preferredStuffDefName);
                                Thing building = def.MadeFromStuff ? ThingMaker.MakeThing(def, stuff) : ThingMaker.MakeThing(def);
                                Thing minified = MinifyUtility.MakeMinified(building);
                                if (minified == null)
                                {
                                    skipped.Add($"{requestedName} -> {def.defName} (not minifiable)");
                                    break;
                                }
                                thingsToDrop.Add(minified);
                                created++;
                            }
                            catch (Exception ex)
                            {
                                skipped.Add($"{requestedName} -> {def.defName} (build/minify failed: {ex.Message})");
                                break;
                            }
                        }

                        AddSummary(def, created, minified: true);
                        continue;
                    }

                    int remaining = count;
                    int stackLimit = Math.Max(1, def.stackLimit);
                    while (remaining > 0)
                    {
                        int stackCount = Math.Min(remaining, stackLimit);
                        ThingDef stuff = ResolveStuffDef(def, preferredStuffDefName);
                        Thing thing = def.MadeFromStuff ? ThingMaker.MakeThing(def, stuff) : ThingMaker.MakeThing(def);
                        thing.stackCount = stackCount;
                        thingsToDrop.Add(thing);
                        AddSummary(def, stackCount, minified: false);
                        remaining -= stackCount;
                    }
                }

                if (thingsToDrop.Count > 0)
                {
                    DropPodUtility.DropThingsNear(dropSpot, map, thingsToDrop);

                    Faction faction = Find.FactionManager.FirstFactionOfDef(FactionDef.Named("Wula_PIA_Legion_Faction"));
                    // Avoid unresolved named placeholders if the translation system doesn't pick up NamedArguments as expected.
                    string template = "Wula_ResourceDrop".Translate();
                    string factionName = faction?.Name ?? "Unknown";
                    string letterText = template.Replace("{FACTION_name}", factionName);
                    Messages.Message(letterText, new LookTargets(dropSpot, map), MessageTypeDefOf.PositiveEvent);

                    StringBuilder resultLog = new StringBuilder();
                    resultLog.Append("Success: Dropped ");
                    foreach (var kv in summary)
                    {
                        resultLog.Append($"{kv.Value}x {kv.Key}, ");
                    }
                    if (summary.Count > 0)
                    {
                        resultLog.Length -= 2;
                    }
                    resultLog.Append($" at {dropSpot}. (drop pods inbound)");
                    if (skipped.Count > 0)
                    {
                        resultLog.Append($" | Skipped: {string.Join("; ", skipped)}");
                    }

                    if (Prefs.DevMode && substitutions.Count > 0)
                    {
                        Messages.Message($"[WulaAI] Substitutions: {string.Join(", ", substitutions)}", MessageTypeDefOf.NeutralEvent);
                    }
                    return resultLog.ToString();
                }
                else
                {
                    string msg = skipped.Count > 0
                        ? $"Error: Failed to create any items. Skipped: {string.Join("; ", skipped)}"
                        : "Error: Failed to create items.";
                    Messages.Message(msg, MessageTypeDefOf.RejectInput);
                    return msg;
                }
            }
            catch (Exception ex)
            {
                string msg = $"Error: {ex.Message}";
                Messages.Message(msg, MessageTypeDefOf.RejectInput);
                return msg;
            }
        }

        private static void AddItem(string name, int count, string stuffDefName, List<(ThingDef def, int count, string requestedName, string stuffDefName)> itemsToSpawn, List<string> substitutions)
        {
            if (string.IsNullOrWhiteSpace(name) || count <= 0) return;

            ThingDef def = DefDatabase<ThingDef>.GetNamed(name.Trim(), false);

            if (def == null)
            {
                foreach (var d in DefDatabase<ThingDef>.AllDefs)
                {
                    if (d.label != null && d.label.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        def = d;
                        break;
                    }
                }
            }

            if (def == null)
            {
                var searchResult = ThingDefSearcher.ParseAndSearch(name);
                if (searchResult.Count > 0)
                {
                    def = searchResult[0].Def;
                }
            }

            if (def == null)
            {
                ThingDefSearcher.TryFindBestThingDef(name, out ThingDef best, out float score, itemsOnly: true, minScore: 0.15f);
                if (best != null && score >= 0.15f)
                {
                    def = best;
                    substitutions.Add($"'{name}' -> '{best.label}' (score {score:F2})");
                }
            }

            if (def != null)
            {
                itemsToSpawn.Add((def, count, name, stuffDefName));
            }
        }

        private static Map GetTargetMap()
        {
            Map map = Find.CurrentMap;
            if (map != null) return map;

            if (Find.Maps != null)
            {
                Map homeMap = Find.Maps.FirstOrDefault(m => m != null && m.IsPlayerHome);
                if (homeMap != null) return homeMap;

                if (Find.Maps.Count > 0) return Find.Maps[0];
            }

            return null;
        }
    }
}
