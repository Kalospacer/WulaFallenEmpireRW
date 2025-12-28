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
                if (circular != null) return ExecuteCircularBombardment(map, targetCell, abilityDef, circular, parsed);

                var bombard = abilityDef.comps?.OfType<CompProperties_AbilityBombardment>().FirstOrDefault();
                if (bombard != null) return ExecuteStrafeBombardment(map, targetCell, abilityDef, bombard, parsed);

                var lance = abilityDef.comps?.OfType<CompProperties_AbilityEnergyLance>().FirstOrDefault();
                if (lance != null) return ExecuteEnergyLance(map, targetCell, abilityDef, lance, parsed);

                var skyfaller = abilityDef.comps?.OfType<CompProperties_AbilityCallSkyfaller>().FirstOrDefault();
                if (skyfaller != null) return ExecuteCallSkyfaller(map, targetCell, abilityDef, skyfaller);

                return $"Error: AbilityDef '{abilityDefName}' is not a supported bombardment/support type.";

            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }

        // Shared helper for determining direction and end point
        private void ParseDirectionInfo(Dictionary<string, string> parsed, IntVec3 startPos, float moveDistance, bool useFixedDistance, out Vector3 direction, out IntVec3 endPos)
        {
            direction = Vector3.forward;
            endPos = startPos;

            if (parsed.TryGetValue("angle", out var angleStr) && float.TryParse(angleStr, out float angle))
            {
                direction = Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward;
                endPos = (startPos.ToVector3() + direction * moveDistance).ToIntVec3();
            }
            else if (TryParseDirectionCell(parsed, out IntVec3 dirCell))
            {
                // direction towards dirCell
                direction = (dirCell.ToVector3() - startPos.ToVector3()).normalized;
                if (direction == Vector3.zero) direction = Vector3.forward;

                if (useFixedDistance)
                {
                    endPos = (startPos.ToVector3() + direction * moveDistance).ToIntVec3();
                }
                else
                {
                    endPos = dirCell;
                }
            }
            else
            {
                // Default North
                endPos = (startPos.ToVector3() + Vector3.forward * moveDistance).ToIntVec3();
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

        private static List<IntVec3> SelectTargetCells(Map map, IntVec3 center, CompProperties_AbilityCircularBombardment props, bool filterFriendlyFire)
        {
            var candidates = GenRadial.RadialCellsAround(center, props.radius, true)
                .Where(c => c.InBounds(map))
                .Where(c => IsValidTargetCell(map, c, center, props, filterFriendlyFire))
                .ToList();

            if (candidates.Count == 0) return new List<IntVec3>();

            var selected = new List<IntVec3>();
            foreach (var cell in candidates.InRandomOrder())
            {
                if (Rand.Value <= props.targetSelectionChance)
                {
                    selected.Add(cell);
                }

                if (selected.Count >= props.maxTargets) break;
            }

            if (selected.Count < props.minTargets)
            {
                var missedCells = candidates.Except(selected).InRandomOrder().ToList();
                int needed = props.minTargets - selected.Count;
                if (needed > 0 && missedCells.Count > 0)
                {
                    selected.AddRange(missedCells.Take(Math.Min(needed, missedCells.Count)));
                }
            }
            else if (selected.Count > props.maxTargets)
            {
                selected = selected.InRandomOrder().Take(props.maxTargets).ToList();
            }

            return selected;
        }

        private static bool IsValidTargetCell(Map map, IntVec3 cell, IntVec3 center, CompProperties_AbilityCircularBombardment props, bool filterFriendlyFire)
        {
            if (props.minDistanceFromCenter > 0f)
            {
                float distance = Vector3.Distance(cell.ToVector3(), center.ToVector3());
                if (distance < props.minDistanceFromCenter) return false;
            }

            if (props.avoidBuildings && cell.GetEdifice(map) != null)
            {
                return false;
            }

            if (filterFriendlyFire && props.avoidFriendlyFire)
            {
                var things = map.thingGrid.ThingsListAt(cell);
                if (things != null)
                {
                    for (int i = 0; i < things.Count; i++)
                    {
                        if (things[i] is Pawn pawn && pawn.Faction == Faction.OfPlayer)
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        private static int ScheduleBombardment(Map map, List<IntVec3> targets, CompProperties_AbilityCircularBombardment props, bool spawnImmediately)
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            int startTick = now + props.warmupTicks;
            int launchesCompleted = 0;
            int groupIndex = 0;

            var remainingTargets = new List<IntVec3>(targets);

            MapComponent_SkyfallerDelayed delayed = null;
            if (!spawnImmediately)
            {
                delayed = map.GetComponent<MapComponent_SkyfallerDelayed>();
                if (delayed == null)
                {
                    delayed = new MapComponent_SkyfallerDelayed(map);
                    map.components.Add(delayed);
                }
            }

            while (remainingTargets.Count > 0 && launchesCompleted < props.maxLaunches)
            {
                int groupSize = Math.Min(props.simultaneousLaunches, remainingTargets.Count);
                var groupTargets = remainingTargets.Take(groupSize).ToList();
                remainingTargets.RemoveRange(0, groupSize);

                if (props.useIndependentIntervals)
                {
                    for (int i = 0; i < groupTargets.Count && launchesCompleted < props.maxLaunches; i++)
                    {
                        int scheduledTick = startTick + groupIndex * props.launchIntervalTicks + i * props.innerLaunchIntervalTicks;
                        SpawnOrSchedule(map, delayed, props.skyfallerDef, groupTargets[i], spawnImmediately, scheduledTick - now);
                        launchesCompleted++;
                    }
                    groupIndex++;
                }
                else
                {
                    int scheduledTick = startTick + groupIndex * props.launchIntervalTicks;
                    for (int i = 0; i < groupTargets.Count && launchesCompleted < props.maxLaunches; i++)
                    {
                        SpawnOrSchedule(map, delayed, props.skyfallerDef, groupTargets[i], spawnImmediately, scheduledTick - now);
                        launchesCompleted++;
                    }
                    groupIndex++;
                }
            }

            return launchesCompleted;
        }

        private string ExecuteEnergyLance(Map map, IntVec3 targetCell, AbilityDef def, CompProperties_AbilityEnergyLance props, Dictionary<string, string> parsed)
        {
            // Determine EnergyLanceDef
            ThingDef lanceDef = props.energyLanceDef ?? DefDatabase<ThingDef>.GetNamedSilentFail("EnergyLance");
            if (lanceDef == null) return $"Error: Could not resolve EnergyLance ThingDef for '{def.defName}'.";

            // Determine Start and End positions
            // For AI usage, 'targetCell' is primarily the START position (focus point), 
            // but we need a direction to make it move effectively.
            
            IntVec3 startPos = targetCell;
            IntVec3 endPos = targetCell; // Default if no direction

             // Determine direction/end position
            Vector3 direction = Vector3.forward;
            if (parsed.TryGetValue("angle", out var angleStr) && float.TryParse(angleStr, out float angle))
            {
                direction = Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward;
                endPos = (startPos.ToVector3() + direction * props.moveDistance).ToIntVec3();
            }
            else if (TryParseDirectionCell(parsed, out IntVec3 dirCell))
            {
                // If a specific cell is given for direction, that acts as the "target/end" point or direction vector
                direction = (dirCell.ToVector3() - startPos.ToVector3()).normalized;
                if (direction == Vector3.zero) direction = Vector3.forward;
                 
                // If using fixed distance, calculate end based on direction and distance
                if (props.useFixedDistance)
                {
                    endPos = (startPos.ToVector3() + direction * props.moveDistance).ToIntVec3();
                }
                else
                {
                    // Otherwise, move TO the specific cell
                    endPos = dirCell;
                }
            }
            else
            {
                // Default direction (North) if none specified, moving props.moveDistance
                 endPos = (startPos.ToVector3() + Vector3.forward * props.moveDistance).ToIntVec3();
            }

            try 
            {
                EnergyLance.MakeEnergyLance(
                    lanceDef,
                    startPos,
                    endPos,
                    map,
                    props.moveDistance,
                    props.useFixedDistance,
                    props.durationTicks,
                    null // No specific pawn instigator available for AI calls
                );
                
                return $"Success: Triggered Energy Lance '{def.defName}' from {startPos} towards {endPos}. Type: {lanceDef.defName}.";
            }
            catch (Exception ex)
            {
                return $"Error: Failed to spawn EnergyLance: {ex.Message}";
            }
        }
        private string ExecuteCircularBombardment(Map map, IntVec3 targetCell, AbilityDef def, CompProperties_AbilityCircularBombardment props, Dictionary<string, string> parsed)
        {
            if (props.skyfallerDef == null) return $"Error: '{def.defName}' has no skyfallerDef.";
            
            bool filter = true;
            if (parsed.TryGetValue("filterFriendlyFire", out var ffStr) && bool.TryParse(ffStr, out bool ff)) filter = ff;

            List<IntVec3> selectedTargets = SelectTargetCells(map, targetCell, props, filter);
            if (selectedTargets.Count == 0) return $"Error: No valid target cells near {targetCell}.";

            bool isPaused = Find.TickManager != null && Find.TickManager.Paused;
            int totalLaunches = ScheduleBombardment(map, selectedTargets, props, spawnImmediately: isPaused);

            return $"Success: Scheduled Circular Bombardment '{def.defName}' at {targetCell}. Launches: {totalLaunches}/{props.maxLaunches}.";
        }

        private string ExecuteCallSkyfaller(Map map, IntVec3 targetCell, AbilityDef def, CompProperties_AbilityCallSkyfaller props)
        {
            if (props.skyfallerDef == null) return $"Error: '{def.defName}' has no skyfallerDef.";

            var delayed = map.GetComponent<MapComponent_SkyfallerDelayed>();
            if (delayed == null)
            {
                delayed = new MapComponent_SkyfallerDelayed(map);
                map.components.Add(delayed);
            }

            // Using the delay from props
            int delay = props.delayTicks;
            if (delay <= 0)
            {
                Skyfaller skyfaller = SkyfallerMaker.MakeSkyfaller(props.skyfallerDef);
                GenSpawn.Spawn(skyfaller, targetCell, map);
                return $"Success: Spawned Skyfaller '{def.defName}' immediately at {targetCell}.";
            }
            else
            {
                delayed.ScheduleSkyfaller(props.skyfallerDef, targetCell, delay);
                return $"Success: Scheduled Skyfaller '{def.defName}' at {targetCell} in {delay} ticks.";
            }
        }

        private string ExecuteStrafeBombardment(Map map, IntVec3 targetCell, AbilityDef def, CompProperties_AbilityBombardment props, Dictionary<string, string> parsed)
        {
            if (props.skyfallerDef == null) return $"Error: '{def.defName}' has no skyfallerDef.";

            // Determine direction
            // Use shared helper - though Strafe uses width/length, the direction logic is same.
            // Strafe doesn't really have a "moveDistance" in the same way, but it aligns along direction.
            // We use dummy distance for calculation.
            ParseDirectionInfo(parsed, targetCell, props.bombardmentLength, true, out Vector3 direction, out IntVec3 _);

            // Calculate target cells based on direction (Simulating CompAbilityEffect_Bombardment logic)
            // We use a simplified version here suitable for AI god-mode instant scheduling.
            // Note: Since we don't have a Comp instance attached to a Pawn, we rely on a static helper or just spawn them.
            // To make it look "progressive" like the real ability, we need a MapComponent or just reuse the SkyfallerDelayed logic.

            var targetCells = CalculateBombardmentAreaCells(map, targetCell, direction, props.bombardmentWidth, props.bombardmentLength);
            
            if (targetCells.Count == 0) return $"Error: No valid targets found for strafe at {targetCell}.";

            // Filter cells by selection chance
            var selectedCells = new List<IntVec3>();
            var missedCells = new List<IntVec3>();
            foreach (var cell in targetCells)
            {
                if (Rand.Value <= props.targetSelectionChance) selectedCells.Add(cell);
                else missedCells.Add(cell);
            }

            // Apply min/max constraints
            if (selectedCells.Count < props.minTargetCells && missedCells.Count > 0)
            {
                int needed = props.minTargetCells - selectedCells.Count;
                selectedCells.AddRange(missedCells.InRandomOrder().Take(Math.Min(needed, missedCells.Count)));
            }
            else if (selectedCells.Count > props.maxTargetCells)
            {
                selectedCells = selectedCells.InRandomOrder().Take(props.maxTargetCells).ToList();
            }

            if (selectedCells.Count == 0) return $"Error: No cells selected for strafe after chance filter.";

            // Organize into rows for progressive effect
            var rows = OrganizeIntoRows(targetCell, direction, selectedCells);

            // Schedule via MapComponent_SkyfallerDelayed
            var delayed = map.GetComponent<MapComponent_SkyfallerDelayed>();
            if (delayed == null)
            {
                delayed = new MapComponent_SkyfallerDelayed(map);
                map.components.Add(delayed); 
            }

            int now = Find.TickManager.TicksGame;
            int startTick = now + props.warmupTicks;
            int totalScheduled = 0;

            foreach (var row in rows)
            {
                // Each row starts after rowDelayTicks
                int rowStartTick = startTick + (row.Key * props.rowDelayTicks);
                
                for (int i = 0; i < row.Value.Count; i++)
                {
                    // Within a row, each cell is hit after impactDelayTicks
                    int hitTick = rowStartTick + (i * props.impactDelayTicks);
                    int delay = hitTick - now;
                    
                    if (delay <= 0)
                    {
                        Skyfaller skyfaller = SkyfallerMaker.MakeSkyfaller(props.skyfallerDef);
                        GenSpawn.Spawn(skyfaller, row.Value[i], map);
                    }
                    else
                    {
                         delayed.ScheduleSkyfaller(props.skyfallerDef, row.Value[i], delay);
                    }
                    totalScheduled++;
                }
            }

            return $"Success: Scheduled Strafe Bombardment '{def.defName}' at {targetCell}. Direction: {direction}. Targets: {totalScheduled}.";
        }

        private static bool TryParseDirectionCell(Dictionary<string, string> parsed, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            if (parsed.TryGetValue("dirX", out var xStr) && parsed.TryGetValue("dirZ", out var zStr) &&
                int.TryParse(xStr, out int x) && int.TryParse(zStr, out int z))
            {
                cell = new IntVec3(x, 0, z);
                return true;
            }
            // Optional: Support <direction>x,z</direction>
            if (parsed.TryGetValue("direction", out var dirStr) && !string.IsNullOrWhiteSpace(dirStr))
            {
                 var parts = dirStr.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                 if (parts.Length >= 2 && int.TryParse(parts[0], out int dx) && int.TryParse(parts[1], out int dz))
                 {
                     cell = new IntVec3(dx, 0, dz);
                     return true;
                 }
            }
            return false;
        }

        // Logic adapted from CompAbilityEffect_Bombardment
        private List<IntVec3> CalculateBombardmentAreaCells(Map map, IntVec3 startCell, Vector3 direction, int width, int length)
        {
            var areaCells = new List<IntVec3>();
            Vector3 start = startCell.ToVector3();
            Vector3 perpendicularDirection = new Vector3(-direction.z, 0, direction.x).normalized;
            
            float halfWidth = width * 0.5f;
            float totalLength = length;
            
            int widthSteps = Math.Max(1, width);
            int lengthSteps = Math.Max(1, length);
            
            for (int l = 0; l <= lengthSteps; l++)
            {
                float lengthProgress = (float)l / lengthSteps;
                float lengthOffset = UnityEngine.Mathf.Lerp(0, totalLength, lengthProgress);
                
                for (int w = 0; w <= widthSteps; w++)
                {
                    float widthProgress = (float)w / widthSteps;
                    float widthOffset = UnityEngine.Mathf.Lerp(-halfWidth, halfWidth, widthProgress);
                    
                    Vector3 cellPos = start + direction * lengthOffset + perpendicularDirection * widthOffset;
                    IntVec3 cell = new IntVec3(
                        UnityEngine.Mathf.RoundToInt(cellPos.x),
                        UnityEngine.Mathf.RoundToInt(cellPos.y),
                        UnityEngine.Mathf.RoundToInt(cellPos.z)
                    );
                    
                    if (cell.InBounds(map) && !areaCells.Contains(cell))
                    {
                        areaCells.Add(cell);
                    }
                }
            }
            return areaCells;
        }

        private Dictionary<int, List<IntVec3>> OrganizeIntoRows(IntVec3 startCell, Vector3 direction, List<IntVec3> cells)
        {
            var rows = new Dictionary<int, List<IntVec3>>();
            Vector3 perpendicularDirection = new Vector3(-direction.z, 0, direction.x).normalized;

            foreach (var cell in cells)
            {
                Vector3 cellVector = cell.ToVector3() - startCell.ToVector3();
                float dot = Vector3.Dot(cellVector, direction);
                int rowIndex = UnityEngine.Mathf.RoundToInt(dot);
                
                if (!rows.ContainsKey(rowIndex)) rows[rowIndex] = new List<IntVec3>();
                rows[rowIndex].Add(cell);
            }

            // Sort rows by index (distance from start)
            var sortedRows = rows.OrderBy(x => x.Key).ToDictionary(x => x.Key, x => x.Value);

             // Sort cells within rows by width position
            foreach (var key in sortedRows.Keys.ToList())
            {
                sortedRows[key] = sortedRows[key].OrderBy(c => Vector3.Dot((c.ToVector3() - startCell.ToVector3()), perpendicularDirection)).ToList();
            }

            return sortedRows;
        }

        private static void SpawnOrSchedule(Map map, MapComponent_SkyfallerDelayed delayed, ThingDef skyfallerDef, IntVec3 cell, bool spawnImmediately, int delayTicks)
        {
            if (!cell.IsValid || !cell.InBounds(map)) return;

            if (spawnImmediately || delayTicks <= 0)
            {
                Skyfaller skyfaller = SkyfallerMaker.MakeSkyfaller(skyfallerDef);
                GenSpawn.Spawn(skyfaller, cell, map);
                return;
            }

            delayed?.ScheduleSkyfaller(skyfallerDef, cell, delayTicks);
        }
    }
}

