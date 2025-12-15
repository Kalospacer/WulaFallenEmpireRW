using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using LudeonTK;

namespace WulaFallenEmpire
{
    public class DebugActions_PrefabSkyfallerCaller
    {
        [DebugAction("Wula Fallen Empire", "Spawn Prefab Skyfaller Caller (Single)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.Playing)]
        public static void SpawnPrefabSkyfallerCallerSingle()
        {
            var eligibleDefs = DefDatabase<ThingDef>.AllDefs
                .Where(def => def.comps != null && def.comps.Any(comp => comp is CompProperties_PrefabSkyfallerCaller))
                .ToList();

            if (!eligibleDefs.Any())
            {
                WulaLog.Debug("[Debug] No ThingDefs found with CompProperties_PrefabSkyfallerCaller");
                return;
            }

            var options = new List<DebugMenuOption>();
            foreach (var thingDef in eligibleDefs)
            {
                options.Add(new DebugMenuOption(thingDef.defName, DebugMenuOptionMode.Tool, () =>
                {
                    ShowFactionSelectionMenu(thingDef, false);
                }));
            }

            Find.WindowStack.Add(new Dialog_DebugOptionListLister(options));
        }

        [DebugAction("Wula Fallen Empire", "Spawn Prefab Skyfaller Caller (x10)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.Playing)]
        public static void SpawnPrefabSkyfallerCallerMultiple()
        {
            var eligibleDefs = DefDatabase<ThingDef>.AllDefs
                .Where(def => def.comps != null && def.comps.Any(comp => comp is CompProperties_PrefabSkyfallerCaller))
                .ToList();

            if (!eligibleDefs.Any())
            {
                WulaLog.Debug("[Debug] No ThingDefs found with CompProperties_PrefabSkyfallerCaller");
                return;
            }

            var options = new List<DebugMenuOption>();
            foreach (var thingDef in eligibleDefs)
            {
                options.Add(new DebugMenuOption(thingDef.defName, DebugMenuOptionMode.Tool, () =>
                {
                    ShowFactionSelectionMenu(thingDef, true);
                }));
            }

            Find.WindowStack.Add(new Dialog_DebugOptionListLister(options));
        }

        private static void ShowFactionSelectionMenu(ThingDef thingDef, bool spawnMultiple)
        {
            var allFactions = Find.FactionManager.AllFactions.ToList();
            var options = new List<DebugMenuOption>();

            options.Add(new DebugMenuOption("No Faction", DebugMenuOptionMode.Tool, () =>
            {
                SpawnThingAtValidLocation(thingDef, null, spawnMultiple);
            }));

            foreach (var faction in allFactions)
            {
                options.Add(new DebugMenuOption(faction.Name, DebugMenuOptionMode.Tool, () =>
                {
                    SpawnThingAtValidLocation(thingDef, faction, spawnMultiple);
                }));
            }

            Find.WindowStack.Add(new Dialog_DebugOptionListLister(options));
        }

        private static void SpawnThingAtValidLocation(ThingDef thingDef, Faction faction, bool spawnMultiple)
        {
            var currentMap = Find.CurrentMap;
            if (currentMap == null)
            {
                WulaLog.Debug("[Debug] No current map found");
                return;
            }

            int spawnCount = spawnMultiple ? 10 : 1;
            int successCount = 0;
            int attempts = 0;
            const int maxAttempts = 50;

            var compProps = thingDef.comps.OfType<CompProperties_PrefabSkyfallerCaller>().FirstOrDefault();
            if (compProps == null)
            {
                WulaLog.Debug($"[Debug] Could not find CompProperties_PrefabSkyfallerCaller for {thingDef.defName}");
                return;
            }

            WulaLog.Debug($"[Debug] Looking for spawn positions for {thingDef.defName} (Size: {thingDef.Size})");

            for (int i = 0; i < spawnCount && attempts < maxAttempts; i++)
            {
                attempts++;
                IntVec3 spawnPos = FindSpawnPositionForSkyfaller(currentMap, thingDef, compProps);

                if (spawnPos.IsValid)
                {
                    Thing thing = ThingMaker.MakeThing(thingDef);
                    
                    if (faction != null)
                    {
                        thing.SetFaction(faction);
                    }

                    GenSpawn.Spawn(thing, spawnPos, currentMap);

                    successCount++;
                    WulaLog.Debug($"[Debug] Successfully spawned {thingDef.defName} at {spawnPos} for faction {faction?.Name ?? "None"}");
                }
                else
                {
                    WulaLog.Debug($"[Debug] Failed to find valid spawn position for {thingDef.defName} (attempt {attempts})");
                }
            }

            if (successCount > 0)
            {
                Messages.Message($"[Debug] Successfully spawned {successCount} {thingDef.defName}", MessageTypeDefOf.PositiveEvent);
            }
            else
            {
                Messages.Message($"[Debug] Failed to spawn any {thingDef.defName} after {attempts} attempts", MessageTypeDefOf.NegativeEvent);
            }
        }

        private static IntVec3 FindSpawnPositionForSkyfaller(Map map, ThingDef thingDef, CompProperties_SkyfallerCaller compProps)
        {
            // 基于Skyfaller实际行为的查找逻辑
            var potentialCells = new List<IntVec3>();

            // 策略1：首先尝试玩家基地附近的开放区域
            WulaLog.Debug($"[Debug] Searching near base area...");
            var baseCells = GetOpenAreaCellsNearBase(map, thingDef.Size);
            foreach (var cell in baseCells)
            {
                if (IsValidForSkyfallerDrop(map, cell, thingDef, compProps))
                {
                    potentialCells.Add(cell);
                }
                if (potentialCells.Count > 20) break; // 找到足够位置就停止
            }

            if (potentialCells.Count > 0)
            {
                WulaLog.Debug($"[Debug] Found {potentialCells.Count} positions near base");
                return potentialCells.RandomElement();
            }

            // 策略2：搜索整个地图的开阔区域
            WulaLog.Debug($"[Debug] Searching open areas...");
            var openAreas = FindOpenAreas(map, thingDef.Size, 1000);
            foreach (var cell in openAreas)
            {
                if (IsValidForSkyfallerDrop(map, cell, thingDef, compProps))
                {
                    potentialCells.Add(cell);
                }
                if (potentialCells.Count > 10) break;
            }

            if (potentialCells.Count > 0)
            {
                WulaLog.Debug($"[Debug] Found {potentialCells.Count} positions in open areas");
                return potentialCells.RandomElement();
            }

            // 策略3：使用随机采样
            WulaLog.Debug($"[Debug] Trying random sampling...");
            for (int i = 0; i < 500; i++)
            {
                IntVec3 randomCell = new IntVec3(
                    Rand.Range(thingDef.Size.x, map.Size.x - thingDef.Size.x),
                    0,
                    Rand.Range(thingDef.Size.z, map.Size.z - thingDef.Size.z)
                );

                if (randomCell.InBounds(map) && IsValidForSkyfallerDrop(map, randomCell, thingDef, compProps))
                {
                    potentialCells.Add(randomCell);
                }
                if (potentialCells.Count > 5) break;
            }

            if (potentialCells.Count > 0)
            {
                WulaLog.Debug($"[Debug] Found {potentialCells.Count} positions via random sampling");
                return potentialCells.RandomElement();
            }

            WulaLog.Debug($"[Debug] No valid positions found for {thingDef.defName}");
            return IntVec3.Invalid;
        }

        /// <summary>
        /// 基于Skyfaller实际行为的有效性检查
        /// </summary>
        private static bool IsValidForSkyfallerDrop(Map map, IntVec3 cell, ThingDef thingDef, CompProperties_SkyfallerCaller compProps)
        {
            // 1. 检查边界
            if (!cell.InBounds(map))
                return false;

            // 2. 检查整个建筑区域
            CellRect occupiedRect = GenAdj.OccupiedRect(cell, Rot4.North, thingDef.Size);
            
            foreach (IntVec3 occupiedCell in occupiedRect)
            {
                if (!occupiedCell.InBounds(map))
                    return false;

                // 3. 检查厚岩顶 - 绝对不允许
                RoofDef roof = occupiedCell.GetRoof(map);
                if (roof != null && roof.isThickRoof)
                {
                    if (!compProps.allowThickRoof)
                        return false;
                }

                // 4. 检查水体 - 不允许
                TerrainDef terrain = occupiedCell.GetTerrain(map);
                if (terrain != null && terrain.IsWater)
                    return false;

                // 5. 检查建筑 - 不允许（但自然物体如树、石头是允许的）
                var things = map.thingGrid.ThingsListAtFast(occupiedCell);
                foreach (var thing in things)
                {
                    // 允许自然物体（树、石头等），它们会被空投清除
                    if (thing.def.category == ThingCategory.Plant || 
                        thing.def.category == ThingCategory.Item ||
                        thing.def.category == ThingCategory.Filth)
                    {
                        continue; // 这些是可以被清除的
                    }

                    // 不允许建筑、蓝图、框架等
                    if (thing.def.category == ThingCategory.Building ||
                        thing.def.IsBlueprint || 
                        thing.def.IsFrame)
                    {
                        return false;
                    }

                    // 不允许其他不可清除的物体
                    if (thing.def.passability == Traversability.Impassable && 
                        thing.def.category != ThingCategory.Plant) // 植物是可清除的
                    {
                        return false;
                    }
                }

                // 6. 检查薄岩顶和普通屋顶的条件
                if (roof != null && !roof.isThickRoof)
                {
                    if (!compProps.allowThinRoof)
                        return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 获取基地附近的开阔区域
        /// </summary>
        private static IEnumerable<IntVec3> GetOpenAreaCellsNearBase(Map map, IntVec2 size)
        {
            var homeArea = map.areaManager.Home;
            IntVec3 searchCenter;

            if (homeArea != null && homeArea.ActiveCells.Any())
            {
                searchCenter = homeArea.ActiveCells.First();
            }
            else
            {
                searchCenter = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
            }

            // 在基地周围搜索开阔区域
            int searchRadius = 50;
            var searchArea = CellRect.CenteredOn(searchCenter, searchRadius);

            // 返回该区域内所有可能的中心点
            foreach (var cell in searchArea.Cells)
            {
                if (!cell.InBounds(map)) continue;

                // 检查这个位置周围是否有足够的开阔空间
                if (IsAreaMostlyOpen(map, cell, size, 0.8f)) // 80%的区域需要是开阔的
                {
                    yield return cell;
                }
            }
        }

        /// <summary>
        /// 查找地图上的开阔区域
        /// </summary>
        private static IEnumerable<IntVec3> FindOpenAreas(Map map, IntVec2 size, int maxCellsToCheck)
        {
            int cellsChecked = 0;
            
            // 优先检查地图上已知的开阔区域
            var allCells = map.AllCells.Where(c => c.InBounds(map)).ToList();
            
            foreach (var cell in allCells)
            {
                if (cellsChecked >= maxCellsToCheck) yield break;
                cellsChecked++;

                // 快速检查：如果这个单元格本身就不适合，跳过
                if (!cell.InBounds(map) || cell.GetTerrain(map).IsWater)
                    continue;

                // 检查整个区域是否开阔
                if (IsAreaMostlyOpen(map, cell, size, 0.7f)) // 70%的区域需要是开阔的
                {
                    yield return cell;
                }
            }
        }

        /// <summary>
        /// 检查区域是否大部分是开阔的（没有建筑，允许自然物体）
        /// </summary>
        private static bool IsAreaMostlyOpen(Map map, IntVec3 center, IntVec2 size, float openThreshold)
        {
            CellRect area = GenAdj.OccupiedRect(center, Rot4.North, size);
            int totalCells = area.Area;
            int openCells = 0;

            foreach (IntVec3 cell in area)
            {
                if (!cell.InBounds(map))
                {
                    continue; // 边界外的单元格不计入
                }

                // 检查是否有不可清除的建筑
                bool hasBlockingBuilding = false;
                var things = map.thingGrid.ThingsListAtFast(cell);
                foreach (var thing in things)
                {
                    if (thing.def.category == ThingCategory.Building ||
                        thing.def.IsBlueprint || 
                        thing.def.IsFrame ||
                        (thing.def.passability == Traversability.Impassable && 
                         thing.def.category != ThingCategory.Plant))
                    {
                        hasBlockingBuilding = true;
                        break;
                    }
                }

                // 检查水体
                bool isWater = cell.GetTerrain(map).IsWater;

                if (!hasBlockingBuilding && !isWater)
                {
                    openCells++;
                }
            }

            float openRatio = (float)openCells / totalCells;
            return openRatio >= openThreshold;
        }

        /// <summary>
        /// 强制清除区域（作为最后手段）
        /// </summary>
        private static bool TryForceClearAreaForSkyfaller(Map map, IntVec3 center, IntVec2 size)
        {
            try
            {
                CellRect clearRect = GenAdj.OccupiedRect(center, Rot4.North, size);
                int clearedCount = 0;

                foreach (IntVec3 cell in clearRect)
                {
                    if (!cell.InBounds(map)) continue;

                    // 清除植物和物品
                    var thingsToRemove = map.thingGrid.ThingsAt(cell)
                        .Where(thing => thing.def.category == ThingCategory.Plant || 
                                       thing.def.category == ThingCategory.Item ||
                                       thing.def.category == ThingCategory.Filth)
                        .ToList();

                    foreach (var thing in thingsToRemove)
                    {
                        thing.Destroy();
                        clearedCount++;
                    }

                    // 确保不是水体
                    if (cell.GetTerrain(map).IsWater)
                    {
                        map.terrainGrid.SetTerrain(cell, TerrainDefOf.Soil);
                    }
                }

                WulaLog.Debug($"[Debug] Force cleared {clearedCount} objects for skyfaller drop");
                return clearedCount > 0;
            }
            catch (System.Exception ex)
            {
                WulaLog.Debug($"[Debug] Error force clearing area: {ex}");
                return false;
            }
        }
    }
}
