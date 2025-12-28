using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using WulaFallenEmpire;

namespace WulaFallenEmpire.EventSystem.AI.Tools
{
    public static class BombardmentUtility
    {
        public static string ExecuteCircularBombardment(Map map, IntVec3 targetCell, AbilityDef def, CompProperties_AbilityCircularBombardment props, Dictionary<string, string> parsed = null)
        {
            if (props.skyfallerDef == null) return $"Error: '{def.defName}' has no skyfallerDef.";
            
            bool filter = true;
            if (parsed != null && parsed.TryGetValue("filterFriendlyFire", out var ffStr) && bool.TryParse(ffStr, out bool ff)) filter = ff;

            List<IntVec3> selectedTargets = SelectTargetCells(map, targetCell, props, filter);
            if (selectedTargets.Count == 0) return $"Error: No valid target cells near {targetCell}.";

            bool isPaused = Find.TickManager != null && Find.TickManager.Paused;
            int totalLaunches = ScheduleBombardment(map, selectedTargets, props, spawnImmediately: isPaused);

            return $"Success: Scheduled Circular Bombardment '{def.defName}' at {targetCell}. Launches: {totalLaunches}/{props.maxLaunches}.";
        }

        public static string ExecuteStrafeBombardment(Map map, IntVec3 targetCell, AbilityDef def, CompProperties_AbilityBombardment props, Dictionary<string, string> parsed = null)
        {
            if (props.skyfallerDef == null) return $"Error: '{def.defName}' has no skyfallerDef.";

            ParseDirectionInfo(parsed, targetCell, props.bombardmentLength, true, out Vector3 direction, out IntVec3 _);

            var targetCells = CalculateBombardmentAreaCells(map, targetCell, direction, props.bombardmentWidth, props.bombardmentLength);
            
            if (targetCells.Count == 0) return $"Error: No valid targets found for strafe at {targetCell}.";

            var selectedCells = new List<IntVec3>();
            var missedCells = new List<IntVec3>();
            foreach (var cell in targetCells)
            {
                if (Rand.Value <= props.targetSelectionChance) selectedCells.Add(cell);
                else missedCells.Add(cell);
            }

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

            var rows = OrganizeIntoRows(targetCell, direction, selectedCells);

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
                int rowStartTick = startTick + (row.Key * props.rowDelayTicks);
                for (int i = 0; i < row.Value.Count; i++)
                {
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
        
        public static string ExecuteStrafeBombardmentDirect(Map map, IntVec3 targetCell, AbilityDef def, CompProperties_AbilityBombardment props, float angle)
        {
             // Overload for direct execution with angle (no parsing needed)
             Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward;
             // Reuse the main logic by passing a mock dictionary or separating the logic further?
             // To simplify, let's just copy the core logic or create a private helper that takes explicit args.
             // Actually, the main method parses direction from 'parsed'. 
             // Let's make a Dictionary to pass to it.
             var dict = new Dictionary<string, string> { { "angle", angle.ToString() } };
             return ExecuteStrafeBombardment(map, targetCell, def, props, dict);
        }

        public static string ExecuteEnergyLance(Map map, IntVec3 targetCell, AbilityDef def, CompProperties_AbilityEnergyLance props, Dictionary<string, string> parsed = null)
        {
            ThingDef lanceDef = props.energyLanceDef ?? DefDatabase<ThingDef>.GetNamedSilentFail("EnergyLance");
            if (lanceDef == null) return $"Error: Could not resolve EnergyLance ThingDef for '{def.defName}'.";

            ParseDirectionInfo(parsed, targetCell, props.moveDistance, props.useFixedDistance, out Vector3 direction, out IntVec3 endPos);

            try 
            {
                EnergyLance.MakeEnergyLance(
                    lanceDef,
                    targetCell,
                    endPos,
                    map,
                    props.moveDistance,
                    props.useFixedDistance,
                    props.durationTicks,
                    null 
                );
                
                return $"Success: Triggered Energy Lance '{def.defName}' from {targetCell} towards {endPos}. Type: {lanceDef.defName}.";
            }
            catch (Exception ex)
            {
                return $"Error: Failed to spawn EnergyLance: {ex.Message}";
            }
        }
        
        public static string ExecuteEnergyLanceDirect(Map map, IntVec3 targetCell, AbilityDef def, CompProperties_AbilityEnergyLance props, float angle)
        {
             var dict = new Dictionary<string, string> { { "angle", angle.ToString() } };
             return ExecuteEnergyLance(map, targetCell, def, props, dict);
        }

        public static string ExecuteCallSkyfaller(Map map, IntVec3 targetCell, AbilityDef def, CompProperties_AbilityCallSkyfaller props)
        {
            if (props.skyfallerDef == null) return $"Error: '{def.defName}' has no skyfallerDef.";

            var delayed = map.GetComponent<MapComponent_SkyfallerDelayed>();
            if (delayed == null)
            {
                delayed = new MapComponent_SkyfallerDelayed(map);
                map.components.Add(delayed);
            }

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

        // --- Helpers ---

        private static void ParseDirectionInfo(Dictionary<string, string> parsed, IntVec3 startPos, float moveDistance, bool useFixedDistance, out Vector3 direction, out IntVec3 endPos)
        {
            direction = Vector3.forward;
            endPos = startPos;
            
            if (parsed == null)
            {
                // Default North
                endPos = (startPos.ToVector3() + Vector3.forward * moveDistance).ToIntVec3();
                return;
            }

            if (parsed.TryGetValue("angle", out var angleStr) && float.TryParse(angleStr, out float angle))
            {
                direction = Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward;
                endPos = (startPos.ToVector3() + direction * moveDistance).ToIntVec3();
            }
            else if (TryParseDirectionCell(parsed, out IntVec3 dirCell))
            {
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

        private static bool TryParseDirectionCell(Dictionary<string, string> parsed, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            if (parsed == null) return false;

            if (parsed.TryGetValue("dirX", out var xStr) && parsed.TryGetValue("dirZ", out var zStr) &&
                int.TryParse(xStr, out int x) && int.TryParse(zStr, out int z))
            {
                cell = new IntVec3(x, 0, z);
                return true;
            }
            
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

        private static List<IntVec3> CalculateBombardmentAreaCells(Map map, IntVec3 startCell, Vector3 direction, int width, int length)
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

        private static Dictionary<int, List<IntVec3>> OrganizeIntoRows(IntVec3 startCell, Vector3 direction, List<IntVec3> cells)
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
    }
}
