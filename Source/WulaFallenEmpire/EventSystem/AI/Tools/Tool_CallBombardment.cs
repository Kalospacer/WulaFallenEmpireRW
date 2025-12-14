using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire.EventSystem.AI.Tools
{
    public class Tool_CallBombardment : AITool
    {
        public override string Name => "call_bombardment";
        public override string Description => "Calls orbital bombardment support at a specified map coordinate using an AbilityDef's bombardment configuration (e.g., WULA_Firepower_Cannon_Salvo).";
        public override string UsageSchema => "<call_bombardment><abilityDef>string (optional, default WULA_Firepower_Cannon_Salvo)</abilityDef><x>int</x><z>int</z><cell>x,z (optional)</cell><filterFriendlyFire>true/false (optional, default true)</filterFriendlyFire></call_bombardment>";

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

                var bombardmentProps = abilityDef.comps?.OfType<CompProperties_AbilityCircularBombardment>().FirstOrDefault();
                if (bombardmentProps == null) return $"Error: AbilityDef '{abilityDefName}' has no CompProperties_AbilityCircularBombardment.";
                if (bombardmentProps.skyfallerDef == null) return $"Error: AbilityDef '{abilityDefName}' has no skyfallerDef configured.";

                bool filterFriendlyFire = true;
                if (parsed.TryGetValue("filterFriendlyFire", out var ffStr) && bool.TryParse(ffStr, out bool ff))
                {
                    filterFriendlyFire = ff;
                }

                List<IntVec3> selectedTargets = SelectTargetCells(map, targetCell, bombardmentProps, filterFriendlyFire);
                if (selectedTargets.Count == 0) return $"Error: No valid target cells near {targetCell}.";

                bool isPaused = Find.TickManager != null && Find.TickManager.Paused;
                int totalLaunches = ScheduleBombardment(map, selectedTargets, bombardmentProps, spawnImmediately: isPaused);

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("Success: Bombardment scheduled.");
                sb.AppendLine($"- abilityDef: {abilityDefName}");
                sb.AppendLine($"- center: {targetCell}");
                sb.AppendLine($"- skyfallerDef: {bombardmentProps.skyfallerDef.defName}");
                sb.AppendLine($"- launches: {totalLaunches}/{bombardmentProps.maxLaunches}");
                sb.AppendLine($"- mode: {(isPaused ? "spawned immediately (game paused)" : "delayed schedule")}");
                sb.AppendLine("- prereqs: ignored (facility/cooldown/non-hostility/research)");
                return sb.ToString().TrimEnd();
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
    }
}

