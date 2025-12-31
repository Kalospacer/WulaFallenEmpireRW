using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace WulaFallenEmpire.EventSystem.AI.Tools
{
    public class Tool_GetAvailableBombardments : AITool
    {
        public override string Name => "get_available_bombardments";
        public override string Description => "Returns a list of available orbital bombardment abilities (AbilityDefs) that can be called. " +
                                              "Use this to find the correct 'abilityDef' for the 'call_bombardment' tool.";
        public override string UsageSchema => "{}";
        public override Dictionary<string, object> GetParametersSchema()
        {
            return SchemaObject(new Dictionary<string, object>(), RequiredList());
        }

        public override string Execute(string args)
        {
            try
            {
                var allAbilityDefs = DefDatabase<AbilityDef>.AllDefs.ToList();
                var validBombardments = new List<AbilityDef>();

                foreach (var def in allAbilityDefs)
                {
                    if (def.comps == null) continue;
                    
                    // Support multiple bombardment types:
                    // 1. Circular Bombardment (original)
                    var circularProps = def.comps.OfType<CompProperties_AbilityCircularBombardment>().FirstOrDefault();
                    if (circularProps != null && circularProps.skyfallerDef != null)
                    {
                        validBombardments.Add(def);
                        continue;
                    }

                    // 2. Standard/Rectangular Bombardment (e.g. Minigun Strafe)
                    var bombardProps = def.comps.OfType<CompProperties_AbilityBombardment>().FirstOrDefault();
                    if (bombardProps != null && bombardProps.skyfallerDef != null)
                    {
                        validBombardments.Add(def);
                        continue;
                    }

                    // 3. Energy Lance (e.g. EnergyLance Strafe)
                    var lanceProps = def.comps.OfType<CompProperties_AbilityEnergyLance>().FirstOrDefault();
                    if (lanceProps != null)
                    {
                        validBombardments.Add(def);
                        continue;
                    }
                    
                    // 4. Call Skyfaller / Surveillance (e.g. Cannon Surveillance)
                    var skyfallerProps = def.comps.OfType<CompProperties_AbilityCallSkyfaller>().FirstOrDefault();
                    if (skyfallerProps != null && skyfallerProps.skyfallerDef != null)
                    {
                        validBombardments.Add(def);
                        continue;
                    }
                }

                if (validBombardments.Count == 0)
                {
                    return "No valid bombardment abilities found in the database.";
                }

                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"Found {validBombardments.Count} available bombardment options:");
                
                // Group by prefix to help AI categorize
                var wulaBombardments = validBombardments.Where(p => p.defName.StartsWith("WULA_", StringComparison.OrdinalIgnoreCase)).ToList();
                var otherBombardments = validBombardments.Where(p => !p.defName.StartsWith("WULA_", StringComparison.OrdinalIgnoreCase)).ToList();

                if (wulaBombardments.Count > 0)
                {
                    sb.AppendLine("\n[Wula Empire Specialized Bombardments]:");
                    foreach (var p in wulaBombardments)
                    {
                        string label = !string.IsNullOrEmpty(p.label) ? $" ({p.label})" : "";
                        var details = "";
                        
                        var circular = p.comps.OfType<CompProperties_AbilityCircularBombardment>().FirstOrDefault();
                        if (circular != null) details = $"Type: Circular, Radius: {circular.radius}, Launches: {circular.maxLaunches}";
                        
                        var bombard = p.comps.OfType<CompProperties_AbilityBombardment>().FirstOrDefault();
                        if (bombard != null) details = $"Type: Strafe, Area: {bombard.bombardmentWidth}x{bombard.bombardmentLength}";
                        
                        var lance = p.comps.OfType<CompProperties_AbilityEnergyLance>().FirstOrDefault();
                        if (lance != null) details = $"Type: Energy Lance, Duration: {lance.durationTicks}";
                        
                        var skyfaller = p.comps.OfType<CompProperties_AbilityCallSkyfaller>().FirstOrDefault();
                        if (skyfaller != null) details = $"Type: Surveillance/Signal, Delay: {skyfaller.delayTicks}";

                        sb.AppendLine($"- {p.defName}{label} [{details}]");
                    }
                }

                if (otherBombardments.Count > 0)
                {
                    sb.AppendLine("\n[Generic/Other Bombardments]:");
                    // Limit generic ones to avoid token bloat
                    var genericToShow = otherBombardments.Take(20).ToList();
                    foreach (var p in genericToShow)
                    {
                        string label = !string.IsNullOrEmpty(p.label) ? $" ({p.label})" : "";
                        var props = p.comps.OfType<CompProperties_AbilityCircularBombardment>().First();
                        sb.AppendLine($"- {p.defName}{label} [MaxLaunches: {props.maxLaunches}, Radius: {props.radius}]");
                    }
                    if (otherBombardments.Count > 20)
                    {
                        sb.AppendLine($"- ... and {otherBombardments.Count - 20} more generic bombardments.");
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
