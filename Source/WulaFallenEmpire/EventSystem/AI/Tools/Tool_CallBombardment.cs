using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using WulaFallenEmpire;

namespace WulaFallenEmpire.EventSystem.AI.Tools
{
    public class Tool_CallBombardment : AITool
    {
        public override string Name => "call_bombardment";
        public override string Description => "Calls orbital bombardment/support using an AbilityDef configuration (e.g., WULA_Firepower_Cannon_Salvo, WULA_Firepower_EnergyLance_Strafe). Supports Circular Bombardment, Strafe, Energy Lance, and Surveillance.";
        public override string UsageSchema => "<call_bombardment><abilityDef>string</abilityDef><x>int</x><z>int</z><cell>x,z</cell><direction>x,z (optional)</direction><angle>degrees (optional)</angle><filterFriendlyFire>true/false</filterFriendlyFire></call_bombardment>";

        public override string Execute(string args)
        {
            try
            {
                var parsed = ParseXmlArgs(args);

                string abilityDefName = parsed.TryGetValue("abilityDef", out var abilityStr) && !string.IsNullOrWhiteSpace(abilityStr)
                    ? abilityStr.Trim()
                    : "WULA_Firepower_Cannon_Salvo";

                if (!TryParseTargetCell(parsed, out var targetCell))
                {
                    return "Error: Missing target coordinates. Provide <x> and <z> (or <cell>x,z</cell>).";
                }

                Map map = Find.CurrentMap;
                if (map == null) return "Error: No active map.";
                if (!targetCell.InBounds(map)) return $"Error: Target {targetCell} is out of bounds.";

                AbilityDef abilityDef = DefDatabase<AbilityDef>.GetNamed(abilityDefName, false);
                if (abilityDef == null) return $"Error: AbilityDef '{abilityDefName}' not found.";

                // Switch logic based on AbilityDef components
                var circular = abilityDef.comps?.OfType<CompProperties_AbilityCircularBombardment>().FirstOrDefault();
                if (circular != null) return BombardmentUtility.ExecuteCircularBombardment(map, targetCell, abilityDef, circular, parsed);

                var bombard = abilityDef.comps?.OfType<CompProperties_AbilityBombardment>().FirstOrDefault();
                if (bombard != null) return BombardmentUtility.ExecuteStrafeBombardment(map, targetCell, abilityDef, bombard, parsed);

                var lance = abilityDef.comps?.OfType<CompProperties_AbilityEnergyLance>().FirstOrDefault();
                if (lance != null) return BombardmentUtility.ExecuteEnergyLance(map, targetCell, abilityDef, lance, parsed);

                var skyfaller = abilityDef.comps?.OfType<CompProperties_AbilityCallSkyfaller>().FirstOrDefault();
                if (skyfaller != null) return BombardmentUtility.ExecuteCallSkyfaller(map, targetCell, abilityDef, skyfaller);

                return $"Error: AbilityDef '{abilityDefName}' is not a supported bombardment/support type.";
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }
        
        private static bool TryParseTargetCell(Dictionary<string, string> parsed, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;

            if (parsed.TryGetValue("x", out var xStr) && parsed.TryGetValue("z", out var zStr) &&
                int.TryParse(xStr, out int x) && int.TryParse(zStr, out int z))
            {
                cell = new IntVec3(x, 0, z);
                return true;
            }

            if (parsed.TryGetValue("cell", out var cellStr) && !string.IsNullOrWhiteSpace(cellStr))
            {
                var parts = cellStr.Split(new[] { ',', '\uFF0C', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && int.TryParse(parts[0], out int cx) && int.TryParse(parts[1], out int cz))
                {
                    cell = new IntVec3(cx, 0, cz);
                    return true;
                }
            }

            return false;
        }
    }
}
