using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RimWorld;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire.EventSystem.AI.Tools
{
    public class Tool_CallPrefabAirdrop : AITool
    {
        public override string Name => "call_prefab_airdrop";
        public override string Description => "Calls a large prefab building airdrop at the specified coordinates. " +
                                              "You must specify the prefabDefName (e.g., 'WULA_NewColonyBase') and the coordinates (x, z). " +
                                              "TIP: Use the 'get_available_prefabs' tool first to see which structures are available. " +
                                              "The default skyfaller animation is 'WULA_Prefab_Incoming'.";
        public override string UsageSchema => "<call_prefab_airdrop><prefabDefName>DefName of the prefab</prefabDefName><skyfallerDef>Optional, default is WULA_Prefab_Incoming</skyfallerDef><x>int</x><z>int</z></call_prefab_airdrop>";

        public override string Execute(string args)
        {
            try
            {
                var parsed = ParseXmlArgs(args);

                if (!parsed.TryGetValue("prefabDefName", out string prefabDefName) || string.IsNullOrWhiteSpace(prefabDefName))
                {
                    return "Error: Missing <prefabDefName>. Example: <prefabDefName>WULA_NewColonyBase</prefabDefName>";
                }

                if (!parsed.TryGetValue("x", out string xStr) || !int.TryParse(xStr, out int x) ||
                    !parsed.TryGetValue("z", out string zStr) || !int.TryParse(zStr, out int z))
                {
                    return "Error: Missing or invalid target coordinates. Provide <x> and <z>.";
                }

                string skyfallerDefName = parsed.TryGetValue("skyfallerDef", out string sd) && !string.IsNullOrWhiteSpace(sd)
                    ? sd.Trim()
                    : "WULA_Prefab_Incoming";

                Map map = Find.CurrentMap;
                if (map == null) return "Error: No active map.";

                IntVec3 targetCell = new IntVec3(x, 0, z);
                if (!targetCell.InBounds(map)) return $"Error: Target {targetCell} is out of bounds.";

                // Check if prefab exists
                PrefabDef prefabDef = DefDatabase<PrefabDef>.GetNamed(prefabDefName, false);
                if (prefabDef == null)
                {
                    return $"Error: PrefabDef '{prefabDefName}' not found.";
                }

                // Check if skyfaller exists
                ThingDef skyfallerDef = DefDatabase<ThingDef>.GetNamed(skyfallerDefName, false);
                if (skyfallerDef == null)
                {
                    return $"Error: Skyfaller ThingDef '{skyfallerDefName}' not found.";
                }

                // Spawning must happen on main thread
                string resultMessage = $"Success: Scheduled airdrop for '{prefabDefName}' at {targetCell} using {skyfallerDefName}.";
                
                // We use a closure to capture the parameters
                string pDef = prefabDefName;
                ThingDef sDef = skyfallerDef;
                IntVec3 cell = targetCell;
                Map targetMap = map;

                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    try
                    {
                        var skyfaller = (Skyfaller_PrefabSpawner)SkyfallerMaker.MakeSkyfaller(sDef);
                        skyfaller.prefabDefName = pDef;
                        GenSpawn.Spawn(skyfaller, cell, targetMap);
                        WulaLog.Debug($"[WulaAI] Prefab airdrop spawned: {pDef} at {cell}");
                    }
                    catch (Exception ex)
                    {
                        WulaLog.Debug($"[WulaAI] Failed to spawn prefab airdrop on main thread: {ex.Message}");
                    }
                });

                return resultMessage;
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }
    }
}
