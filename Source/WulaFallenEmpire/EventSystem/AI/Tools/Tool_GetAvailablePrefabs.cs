using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace WulaFallenEmpire.EventSystem.AI.Tools
{
    public class Tool_GetAvailablePrefabs : AITool
    {
        public override string Name => "get_available_prefabs";
        public override string Description => "Returns a list of available building prefabs (blueprints) that can be summoned. " +
                                              "Use this to find the correct 'prefabDefName' for the 'call_prefab_airdrop' tool.";
        public override string UsageSchema => "<get_available_prefabs/>";

        public override string Execute(string args)
        {
            try
            {
                var prefabs = DefDatabase<PrefabDef>.AllDefs.ToList();
                if (prefabs.Count == 0)
                {
                    return "No prefabs found in the database.";
                }

                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"Found {prefabs.Count} available prefabs:");
                
                // Group by prefix to help AI categorize
                var wulaPrefabs = prefabs.Where(p => p.defName.StartsWith("WULA_", StringComparison.OrdinalIgnoreCase)).ToList();
                var otherPrefabs = prefabs.Where(p => !p.defName.StartsWith("WULA_", StringComparison.OrdinalIgnoreCase)).ToList();

                if (wulaPrefabs.Count > 0)
                {
                    sb.AppendLine("\n[Wula Empire Specialized Prefabs]:");
                    foreach (var p in wulaPrefabs)
                    {
                        string label = !string.IsNullOrEmpty(p.label) ? $" ({p.label})" : "";
                        sb.AppendLine($"- {p.defName}{label}, Size: {p.size}");
                    }
                }

                if (otherPrefabs.Count > 0)
                {
                    sb.AppendLine("\n[Generic/Other Prefabs]:");
                    // Limit generic ones to avoid token bloat
                    var genericToShow = otherPrefabs.Take(20).ToList();
                    foreach (var p in genericToShow)
                    {
                        string label = !string.IsNullOrEmpty(p.label) ? $" ({p.label})" : "";
                        sb.AppendLine($"- {p.defName}{label}, Size: {p.size}");
                    }
                    if (otherPrefabs.Count > 20)
                    {
                        sb.AppendLine($"- ... and {otherPrefabs.Count - 20} more generic prefabs.");
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
