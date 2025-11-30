using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using RimWorld.QuestGen;

namespace WulaFallenEmpire
{
    public class QuestNode_SpawnPrefabSkyfallerCaller : QuestNode
    {
        [NoTranslate]
        public SlateRef<string> inSignal;
        public SlateRef<ThingDef> thingDef;
        public SlateRef<Faction> faction;
        public SlateRef<int> spawnCount = 1;
        public SlateRef<Map> map;
        public SlateRef<bool> sendMessageOnSuccess = true;
        public SlateRef<bool> sendMessageOnFailure = true;

        protected override bool TestRunInt(Slate slate)
        {
            // 在测试运行中只检查基本条件
            if (thingDef.GetValue(slate) == null)
            {
                Log.Warning("[QuestNode] ThingDef is null in TestRun");
                return false;
            }

            var mapValue = map.GetValue(slate) ?? Find.CurrentMap;
            if (mapValue == null)
            {
                Log.Warning("[QuestNode] Map is null in TestRun");
                return false;
            }

            var compProps = thingDef.GetValue(slate).comps?.OfType<CompProperties_PrefabSkyfallerCaller>().FirstOrDefault();
            if (compProps == null)
            {
                Log.Warning($"[QuestNode] ThingDef {thingDef.GetValue(slate).defName} does not have CompProperties_PrefabSkyfallerCaller");
                return false;
            }

            return true;
        }

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            
            // 获取参数值
            ThingDef targetThingDef = thingDef.GetValue(slate);
            Faction targetFaction = faction.GetValue(slate);
            int targetSpawnCount = spawnCount.GetValue(slate);
            Map targetMap = map.GetValue(slate) ?? Find.CurrentMap;
            bool doSendMessageOnSuccess = sendMessageOnSuccess.GetValue(slate);
            bool doSendMessageOnFailure = sendMessageOnFailure.GetValue(slate);

            if (targetThingDef == null)
            {
                Log.Error("[QuestNode] ThingDef is null in RunInt");
                return;
            }

            if (targetMap == null)
            {
                Log.Error("[QuestNode] Map is null in RunInt");
                return;
            }

            // 获取组件属性
            var compProps = targetThingDef.comps?.OfType<CompProperties_PrefabSkyfallerCaller>().FirstOrDefault();
            if (compProps == null)
            {
                Log.Error($"[QuestNode] ThingDef {targetThingDef.defName} does not have CompProperties_PrefabSkyfallerCaller");
                return;
            }

            Log.Message($"[QuestNode] Attempting to spawn {targetSpawnCount} {targetThingDef.defName} on map {targetMap}");

            // 执行生成
            int successCount = SpawnThingsAtValidLocations(targetThingDef, targetFaction, targetSpawnCount, targetMap);

            // 发送结果消息
            if (successCount > 0)
            {
                if (doSendMessageOnSuccess)
                {
                    Messages.Message($"[Quest] Successfully spawned {successCount} {targetThingDef.label}", MessageTypeDefOf.PositiveEvent);
                }
                Log.Message($"[QuestNode] Successfully spawned {successCount}/{targetSpawnCount} {targetThingDef.defName}");
            }
            else
            {
                if (doSendMessageOnFailure)
                {
                    Messages.Message($"[Quest] Failed to spawn any {targetThingDef.label}", MessageTypeDefOf.NegativeEvent);
                }
                Log.Warning($"[QuestNode] Failed to spawn any {targetThingDef.defName}");
            }

            // 将结果存储到Slate中，供后续节点使用
            QuestGen.slate.Set("prefabSpawnSuccessCount", successCount);
            QuestGen.slate.Set("prefabSpawnRequestedCount", targetSpawnCount);
        }

        /// <summary>
        /// 在有效位置生成多个建筑
        /// </summary>
        private int SpawnThingsAtValidLocations(ThingDef thingDef, Faction faction, int spawnCount, Map targetMap)
        {
            int successCount = 0;
            int attempts = 0;
            const int maxAttempts = 100; // 最大尝试次数

            var compProps = thingDef.comps.OfType<CompProperties_PrefabSkyfallerCaller>().FirstOrDefault();
            if (compProps == null)
            {
                Log.Error($"[QuestNode] Could not find CompProperties_PrefabSkyfallerCaller for {thingDef.defName}");
                return 0;
            }

            for (int i = 0; i < spawnCount && attempts < maxAttempts; i++)
            {
                attempts++;
                IntVec3 spawnPos = FindSpawnPositionForSkyfaller(targetMap, thingDef, compProps);

                if (spawnPos.IsValid)
                {
                    Thing thing = ThingMaker.MakeThing(thingDef);
                    
                    if (faction != null)
                    {
                        thing.SetFaction(faction);
                    }

                    GenSpawn.Spawn(thing, spawnPos, targetMap);
                    successCount++;

                    Log.Message($"[QuestNode] Successfully spawned {thingDef.defName} at {spawnPos} for faction {faction?.Name ?? "None"}");
                }
                else
                {
                    Log.Warning($"[QuestNode] Failed to find valid spawn position for {thingDef.defName} (attempt {attempts})");
                }
            }

            return successCount;
        }

        /// <summary>
        /// 查找适合Skyfaller空投的位置
        /// </summary>
        private IntVec3 FindSpawnPositionForSkyfaller(Map map, ThingDef thingDef, CompProperties_SkyfallerCaller compProps)
        {
            var potentialCells = new List<IntVec3>();

            // 策略1：首先尝试玩家基地附近的开放区域
            var baseCells = GetOpenAreaCellsNearBase(map, thingDef.Size);
            foreach (var cell in baseCells)
            {
                if (IsValidForSkyfallerDrop(map, cell, thingDef, compProps))
                {
                    potentialCells.Add(cell);
                }
                if (potentialCells.Count > 20) break;
            }

            if (potentialCells.Count > 0)
            {
                return potentialCells.RandomElement();
            }

            // 策略2：搜索整个地图的开阔区域
            var openAreas = FindOpenAreas(map, thingDef.Size, 500);
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
                return potentialCells.RandomElement();
            }

            // 策略3：使用随机采样
            for (int i = 0; i < 300; i++)
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
                return potentialCells.RandomElement();
            }

            Log.Warning($"[QuestNode] No valid positions found for {thingDef.defName} after exhaustive search");
            return IntVec3.Invalid;
        }

        /// <summary>
        /// 基于Skyfaller实际行为的有效性检查
        /// </summary>
        private bool IsValidForSkyfallerDrop(Map map, IntVec3 cell, ThingDef thingDef, CompProperties_SkyfallerCaller compProps)
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
                        continue;
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
                        thing.def.category != ThingCategory.Plant)
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
        private IEnumerable<IntVec3> GetOpenAreaCellsNearBase(Map map, IntVec2 size)
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

            int searchRadius = 50;
            var searchArea = CellRect.CenteredOn(searchCenter, searchRadius);

            foreach (var cell in searchArea.Cells)
            {
                if (!cell.InBounds(map)) continue;

                if (IsAreaMostlyOpen(map, cell, size, 0.8f))
                {
                    yield return cell;
                }
            }
        }

        /// <summary>
        /// 查找地图上的开阔区域
        /// </summary>
        private IEnumerable<IntVec3> FindOpenAreas(Map map, IntVec2 size, int maxCellsToCheck)
        {
            int cellsChecked = 0;
            var allCells = map.AllCells.Where(c => c.InBounds(map)).ToList();
            
            foreach (var cell in allCells)
            {
                if (cellsChecked >= maxCellsToCheck) yield break;
                cellsChecked++;

                if (!cell.InBounds(map) || cell.GetTerrain(map).IsWater)
                    continue;

                if (IsAreaMostlyOpen(map, cell, size, 0.7f))
                {
                    yield return cell;
                }
            }
        }

        /// <summary>
        /// 检查区域是否大部分是开阔的
        /// </summary>
        private bool IsAreaMostlyOpen(Map map, IntVec3 center, IntVec2 size, float openThreshold)
        {
            CellRect area = GenAdj.OccupiedRect(center, Rot4.North, size);
            int totalCells = area.Area;
            int openCells = 0;

            foreach (IntVec3 cell in area)
            {
                if (!cell.InBounds(map))
                {
                    continue;
                }

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

                bool isWater = cell.GetTerrain(map).IsWater;

                if (!hasBlockingBuilding && !isWater)
                {
                    openCells++;
                }
            }

            float openRatio = (float)openCells / totalCells;
            return openRatio >= openThreshold;
        }
    }
}
