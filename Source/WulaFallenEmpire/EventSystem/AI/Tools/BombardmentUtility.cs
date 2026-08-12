using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using WulaFallenEmpire;

namespace WulaFallenEmpire.EventSystem.AI.Tools
{
    public static class BombardmentUtility
    {
        /// <summary>
        /// Cells within this distance of a colony pawn or player-owned building are never targeted.
        /// </summary>
        private const float FriendlyFireSafetyRadius = 2.9f;

        /// <summary>
        /// Builds the set of cells that no bombardment may target, covering colony pawns, colony
        /// prisoners, and player-owned buildings.
        /// </summary>
        /// <remarks>
        /// This is the host-side floor. It is deliberately not switchable from tool arguments: the model
        /// calling these tools must not be able to opt out of protecting the colony. Def-level flags such
        /// as <c>avoidFriendlyFire</c> can still restrict targeting further, but never loosen it.
        /// Computed once per call and reused for every candidate cell, rather than scanning a radius per
        /// candidate.
        /// </remarks>
        /// <param name="map">Map being bombarded.</param>
        /// <returns>Cells that must be excluded from targeting.</returns>
        private static HashSet<IntVec3> BuildFriendlyExclusionZone(Map map)
        {
            var blocked = new HashSet<IntVec3>();
            if (map == null) return blocked;

            var playerFaction = Faction.OfPlayer;
            var pawns = map.mapPawns?.AllPawnsSpawned;
            if (pawns != null)
            {
                for (int i = 0; i < pawns.Count; i++)
                {
                    var pawn = pawns[i];
                    if (pawn == null || !pawn.Spawned) continue;
                    if (pawn.Faction != playerFaction && !pawn.IsPrisonerOfColony) continue;
                    BlockAround(map, blocked, pawn.Position);
                }
            }

            var buildings = map.listerBuildings?.allBuildingsColonist;
            if (buildings != null)
            {
                for (int i = 0; i < buildings.Count; i++)
                {
                    var building = buildings[i];
                    if (building == null || building.Destroyed || !building.Spawned) continue;
                    foreach (var cell in building.OccupiedRect())
                    {
                        BlockAround(map, blocked, cell);
                    }
                }
            }

            return blocked;
        }

        private static void BlockAround(Map map, HashSet<IntVec3> blocked, IntVec3 origin)
        {
            foreach (var cell in GenRadial.RadialCellsAround(origin, FriendlyFireSafetyRadius, true))
            {
                if (cell.InBounds(map)) blocked.Add(cell);
            }
        }

        public static string ExecuteCircularBombardment(Map map, IntVec3 targetCell, AbilityDef def, CompProperties_AbilityCircularBombardment props)
        {
            if (props.skyfallerDef == null) return $"Error: '{def.defName}' has no skyfallerDef.";

            List<IntVec3> selectedTargets = SelectTargetCells(map, targetCell, props, BuildFriendlyExclusionZone(map));
            if (selectedTargets.Count == 0) return $"Error: No valid target cells near {targetCell}. Every candidate cell was inside the colony safety zone or rejected by '{def.defName}'.";

            bool isPaused = Find.TickManager != null && Find.TickManager.Paused;
            int totalLaunches = ScheduleBombardment(map, selectedTargets, props, spawnImmediately: isPaused);

            return $"Success: Scheduled Circular Bombardment '{def.defName}' at {targetCell}. Launches: {totalLaunches}/{props.maxLaunches}.";
        }

        public static string ExecuteStrafeBombardment(Map map, IntVec3 targetCell, AbilityDef def, CompProperties_AbilityBombardment props, Dictionary<string, object> parsed = null)
        {
            if (props.skyfallerDef == null) return $"Error: '{def.defName}' has no skyfallerDef.";

            ParseDirectionInfo(parsed, targetCell, props.bombardmentLength, true, out Vector3 direction, out IntVec3 _);

            var targetCells = CalculateBombardmentAreaCells(map, targetCell, direction, props.bombardmentWidth, props.bombardmentLength);

            if (targetCells.Count == 0) return $"Error: No valid targets found for strafe at {targetCell}.";

            var exclusionZone = BuildFriendlyExclusionZone(map);
            targetCells.RemoveAll(exclusionZone.Contains);
            if (targetCells.Count == 0) return $"Error: Strafe run at {targetCell} along {direction} would cross the colony safety zone; refused.";

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
            var dict = new Dictionary<string, object> { { "angle", angle } };
            return ExecuteStrafeBombardment(map, targetCell, def, props, dict);
        }

        public static string ExecuteEnergyLance(Map map, IntVec3 targetCell, AbilityDef def, CompProperties_AbilityEnergyLance props, Dictionary<string, object> parsed = null)
        {
            ThingDef lanceDef = props.energyLanceDef ?? DefDatabase<ThingDef>.GetNamedSilentFail("EnergyLance");
            if (lanceDef == null) return $"Error: Could not resolve EnergyLance ThingDef for '{def.defName}'.";

            ParseDirectionInfo(parsed, targetCell, props.moveDistance, props.useFixedDistance, out Vector3 direction, out IntVec3 endPos);

            var exclusionZone = BuildFriendlyExclusionZone(map);
            foreach (var cell in CellsAlongLine(targetCell, endPos))
            {
                if (exclusionZone.Contains(cell))
                {
                    return $"Error: Energy Lance from {targetCell} to {endPos} would sweep the colony safety zone at {cell}; refused.";
                }
            }

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
             var dict = new Dictionary<string, object> { { "angle", angle } };
             return ExecuteEnergyLance(map, targetCell, def, props, dict);
        }

        public static string ExecuteCallSkyfaller(Map map, IntVec3 targetCell, AbilityDef def, CompProperties_AbilityCallSkyfaller props)
        {
            if (props.skyfallerDef == null) return $"Error: '{def.defName}' has no skyfallerDef.";

            if (BuildFriendlyExclusionZone(map).Contains(targetCell))
            {
                return $"Error: {targetCell} is inside the colony safety zone; refused.";
            }

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

        private static void ParseDirectionInfo(Dictionary<string, object> parsed, IntVec3 startPos, float moveDistance, bool useFixedDistance, out Vector3 direction, out IntVec3 endPos)
        {
            direction = Vector3.forward;
            endPos = startPos;
            
            if (parsed == null)
            {
                // Default North
                endPos = (startPos.ToVector3() + Vector3.forward * moveDistance).ToIntVec3();
                return;
            }

            if (TryGetFloat(parsed, "angle", out float angle))
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

        private static bool TryParseDirectionCell(Dictionary<string, object> parsed, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            if (parsed == null) return false;

            if (TryGetInt(parsed, "dirX", out int x) && TryGetInt(parsed, "dirZ", out int z))
            {
                cell = new IntVec3(x, 0, z);
                return true;
            }
            
            if (TryGetString(parsed, "direction", out var dirStr) && !string.IsNullOrWhiteSpace(dirStr))
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

        /// <summary>
        /// Walks the cells a straight sweep from <paramref name="from"/> to <paramref name="to"/> covers.
        /// </summary>
        /// <param name="from">Start cell.</param>
        /// <param name="to">End cell.</param>
        /// <returns>Distinct cells along the segment, including both endpoints.</returns>
        private static IEnumerable<IntVec3> CellsAlongLine(IntVec3 from, IntVec3 to)
        {
            Vector3 start = from.ToVector3();
            Vector3 end = to.ToVector3();
            int steps = Math.Max(1, Mathf.RoundToInt(Vector3.Distance(start, end)));
            IntVec3 previous = IntVec3.Invalid;
            for (int i = 0; i <= steps; i++)
            {
                IntVec3 cell = Vector3.Lerp(start, end, (float)i / steps).ToIntVec3();
                if (cell == previous) continue;
                previous = cell;
                yield return cell;
            }
        }

        private static List<IntVec3> SelectTargetCells(Map map, IntVec3 center, CompProperties_AbilityCircularBombardment props, HashSet<IntVec3> exclusionZone)
        {
            var candidates = GenRadial.RadialCellsAround(center, props.radius, true)
                .Where(c => c.InBounds(map))
                .Where(c => !exclusionZone.Contains(c))
                .Where(c => IsValidTargetCell(map, c, center, props))
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

        private static bool IsValidTargetCell(Map map, IntVec3 cell, IntVec3 center, CompProperties_AbilityCircularBombardment props)
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

            // Colony pawns and player buildings are already excluded by the host-side zone in
            // BuildFriendlyExclusionZone; this def flag can only narrow targeting further.
            if (props.avoidFriendlyFire)
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

        private static bool TryGetString(Dictionary<string, object> parsed, string key, out string value)
        {
            value = null;
            if (parsed == null || string.IsNullOrWhiteSpace(key)) return false;
            if (!parsed.TryGetValue(key, out object raw) || raw == null) return false;
            value = Convert.ToString(raw, CultureInfo.InvariantCulture);
            return !string.IsNullOrWhiteSpace(value);
        }

        private static bool TryGetInt(Dictionary<string, object> parsed, string key, out int value)
        {
            value = 0;
            if (!TryGetNumber(parsed, key, out double number)) return false;
            value = (int)Math.Round(number);
            return true;
        }

        private static bool TryGetFloat(Dictionary<string, object> parsed, string key, out float value)
        {
            value = 0f;
            if (!TryGetNumber(parsed, key, out double number)) return false;
            value = (float)number;
            return true;
        }

        private static bool TryGetNumber(Dictionary<string, object> parsed, string key, out double value)
        {
            value = 0;
            if (parsed == null || string.IsNullOrWhiteSpace(key)) return false;
            if (!parsed.TryGetValue(key, out object raw) || raw == null) return false;
            if (raw is double d)
            {
                value = d;
                return true;
            }
            if (raw is float f)
            {
                value = f;
                return true;
            }
            if (raw is int i)
            {
                value = i;
                return true;
            }
            if (raw is long l)
            {
                value = l;
                return true;
            }
            if (raw is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedNum))
            {
                value = parsedNum;
                return true;
            }
            return false;
        }
    }
}
