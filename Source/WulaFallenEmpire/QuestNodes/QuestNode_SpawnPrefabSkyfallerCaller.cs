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
        public SlateRef<bool> allowThickRoof = false;
        public SlateRef<bool> allowThinRoof = true;
        
        // 新增：是否启用分散算法
        public SlateRef<bool> spreadOut = false;
        
        // 新增：最小间隔距离（以单元格为单位）
        public SlateRef<int> minSpacing = 5;
        
        // 新增：是否只对小型目标使用分散算法
        public SlateRef<bool> onlyForSmallTargets = true;
        
        // 新增：小型目标的定义（最大尺寸）
        public SlateRef<int> smallTargetMaxSize = 3;

        protected override bool TestRunInt(Slate slate)
        {
            // 在测试运行中只检查基本条件
            if (thingDef.GetValue(slate) == null)
            {
                WulaLog.Debug("[QuestNode] ThingDef is null in TestRun");
                return false;
            }

            var mapValue = map.GetValue(slate) ?? Find.CurrentMap;
            if (mapValue == null)
            {
                WulaLog.Debug("[QuestNode] Map is null in TestRun");
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
            bool targetAllowThickRoof = allowThickRoof.GetValue(slate);
            bool targetAllowThinRoof = allowThinRoof.GetValue(slate);
            bool doSpreadOut = spreadOut.GetValue(slate);
            int targetMinSpacing = minSpacing.GetValue(slate);
            bool targetOnlyForSmallTargets = onlyForSmallTargets.GetValue(slate);
            int targetSmallTargetMaxSize = smallTargetMaxSize.GetValue(slate);

            if (targetThingDef == null)
            {
                WulaLog.Debug("[QuestNode] ThingDef is null in RunInt");
                return;
            }

            if (targetMap == null)
            {
                WulaLog.Debug("[QuestNode] Map is null in RunInt");
                return;
            }

            // 创建QuestPart来延迟执行，等待信号
            QuestPart_SpawnPrefabSkyfallerCaller questPart = new QuestPart_SpawnPrefabSkyfallerCaller
            {
                thingDef = targetThingDef,
                faction = targetFaction,
                spawnCount = targetSpawnCount,
                map = targetMap,
                sendMessageOnSuccess = doSendMessageOnSuccess,
                sendMessageOnFailure = doSendMessageOnFailure,
                allowThickRoof = targetAllowThickRoof,
                allowThinRoof = targetAllowThinRoof,
                spreadOut = doSpreadOut,
                minSpacing = targetMinSpacing,
                onlyForSmallTargets = targetOnlyForSmallTargets,
                smallTargetMaxSize = targetSmallTargetMaxSize,
                inSignal = QuestGenUtility.HardcodedSignalWithQuestID(inSignal.GetValue(slate))
            };

            // 将QuestPart添加到Quest中
            QuestGen.quest.AddPart(questPart);
            
            // 设置输出变量
            QuestGen.slate.Set("prefabSpawnSuccessCount", questPart.successCount);
            QuestGen.slate.Set("prefabSpawnRequestedCount", targetSpawnCount);
        }
    }

    // 新增：QuestPart来管理延迟执行
    public class QuestPart_SpawnPrefabSkyfallerCaller : QuestPart
    {
        public string inSignal;
        public ThingDef thingDef;
        public Faction faction;
        public int spawnCount = 1;
        public Map map;
        public bool sendMessageOnSuccess = true;
        public bool sendMessageOnFailure = true;
        public bool allowThickRoof = false;
        public bool allowThinRoof = true;
        
        // 新增：分散算法相关
        public bool spreadOut = false;
        public int minSpacing = 5;
        public bool onlyForSmallTargets = true;
        public int smallTargetMaxSize = 3;
        
        // 输出变量
        public int successCount = 0;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref inSignal, "inSignal");
            Scribe_Defs.Look(ref thingDef, "thingDef");
            Scribe_References.Look(ref faction, "faction");
            Scribe_Values.Look(ref spawnCount, "spawnCount", 1);
            Scribe_References.Look(ref map, "map");
            Scribe_Values.Look(ref sendMessageOnSuccess, "sendMessageOnSuccess", true);
            Scribe_Values.Look(ref sendMessageOnFailure, "sendMessageOnFailure", true);
            Scribe_Values.Look(ref allowThickRoof, "allowThickRoof", false);
            Scribe_Values.Look(ref allowThinRoof, "allowThinRoof", true);
            Scribe_Values.Look(ref spreadOut, "spreadOut", false);
            Scribe_Values.Look(ref minSpacing, "minSpacing", 5);
            Scribe_Values.Look(ref onlyForSmallTargets, "onlyForSmallTargets", true);
            Scribe_Values.Look(ref smallTargetMaxSize, "smallTargetMaxSize", 3);
            Scribe_Values.Look(ref successCount, "successCount", 0);
        }

        public override void Notify_QuestSignalReceived(Signal signal)
        {
            base.Notify_QuestSignalReceived(signal);
            
            // 检查是否是我们等待的信号
            if (!(signal.tag == inSignal))
            {
                return;
            }

            if (thingDef == null)
            {
                WulaLog.Debug("[QuestPart] ThingDef is null");
                return;
            }

            if (map == null)
            {
                WulaLog.Debug("[QuestPart] Map is null");
                return;
            }

            WulaLog.Debug($"[QuestPart] Received signal {inSignal}, spawning {spawnCount} {thingDef.defName} on map {map}");

            // 执行生成逻辑
            successCount = ExecuteSpawnLogic();
            
            // 发送结果消息
            if (successCount > 0)
            {
                if (sendMessageOnSuccess)
                {
                    Messages.Message($"[Quest] Successfully spawned {successCount} {thingDef.label}", MessageTypeDefOf.PositiveEvent);
                }
                WulaLog.Debug($"[QuestPart] Successfully spawned {successCount}/{spawnCount} {thingDef.defName}");
            }
            else
            {
                if (sendMessageOnFailure)
                {
                    Messages.Message($"[Quest] Failed to spawn any {thingDef.label}", MessageTypeDefOf.NegativeEvent);
                }
                WulaLog.Debug($"[QuestPart] Failed to spawn any {thingDef.defName}");
            }
        }

        private int ExecuteSpawnLogic()
        {
            // 检查是否应该使用分散算法
            bool useSpreadOutAlgorithm = spreadOut;
            if (onlyForSmallTargets)
            {
                // 检查目标是否为小型目标
                bool isSmallTarget = thingDef.Size.x <= smallTargetMaxSize && 
                                     thingDef.Size.z <= smallTargetMaxSize;
                useSpreadOutAlgorithm = useSpreadOutAlgorithm && isSmallTarget;
                
                if (spreadOut && !isSmallTarget)
                {
                    WulaLog.Debug($"[QuestPart] Target {thingDef.defName} is not considered small (size {thingDef.Size.x}x{thingDef.Size.z}), not using spread-out algorithm");
                }
            }

            // 执行生成
            if (useSpreadOutAlgorithm)
            {
                return SpawnThingsWithSpacing(thingDef, faction, spawnCount, map, allowThickRoof, allowThinRoof, minSpacing);
            }
            else
            {
                return SpawnThingsAtValidLocations(thingDef, faction, spawnCount, map, allowThickRoof, allowThinRoof);
            }
        }

        /// <summary>
        /// 使用分散算法生成多个建筑
        /// </summary>
        private int SpawnThingsWithSpacing(ThingDef thingDef, Faction faction, int spawnCount, Map targetMap, bool allowThickRoof, bool allowThinRoof, int minSpacing)
        {
            WulaLog.Debug($"[QuestPart] Using spread-out algorithm with min spacing {minSpacing} cells");
            
            List<IntVec3> spawnedPositions = new List<IntVec3>();
            int successCount = 0;
            int attempts = 0;
            const int maxTotalAttempts = 500; // 总的最大尝试次数
            const int maxAttemptsPerThing = 100; // 每个建筑的最大尝试次数

            // 生成一个所有可能位置的列表
            List<IntVec3> allPossibleCells = GeneratePossibleCells(targetMap, thingDef, allowThickRoof, allowThinRoof, 1000);
            
            WulaLog.Debug($"[QuestPart] Found {allPossibleCells.Count} possible cells for {thingDef.defName}");
            
            // 如果没有足够的可能位置，直接使用原算法
            if (allPossibleCells.Count < spawnCount)
            {
                WulaLog.Debug($"[QuestPart] Not enough possible cells ({allPossibleCells.Count}) for {spawnCount} spawns, falling back to normal algorithm");
                return SpawnThingsAtValidLocations(thingDef, faction, spawnCount, targetMap, allowThickRoof, allowThinRoof);
            }

            for (int i = 0; i < spawnCount && attempts < maxTotalAttempts; i++)
            {
                bool foundPosition = false;
                int attemptsForThisThing = 0;
                
                // 尝试找到一个满足间距条件的位置
                while (!foundPosition && attemptsForThisThing < maxAttemptsPerThing && attempts < maxTotalAttempts)
                {
                    attempts++;
                    attemptsForThisThing++;
                    
                    // 从可能位置中随机选择一个
                    IntVec3 candidatePos = allPossibleCells.RandomElement();
                    
                    // 检查是否满足间距条件
                    bool meetsSpacing = true;
                    foreach (var existingPos in spawnedPositions)
                    {
                        float distance = candidatePos.DistanceTo(existingPos);
                        if (distance < minSpacing)
                        {
                            meetsSpacing = false;
                            break;
                        }
                    }
                    
                    if (meetsSpacing)
                    {
                        // 创建并生成建筑
                        Thing thing = ThingMaker.MakeThing(thingDef);
                        
                        if (faction != null)
                        {
                            thing.SetFaction(faction);
                        }

                        GenSpawn.Spawn(thing, candidatePos, targetMap);
                        spawnedPositions.Add(candidatePos);
                        successCount++;
                        foundPosition = true;
                        
                        // 从可能位置列表中移除这个位置及其周围的位置（避免重复选择）
                        allPossibleCells.RemoveAll(cell => cell.DistanceTo(candidatePos) < minSpacing / 2);
                        
                        WulaLog.Debug($"[QuestPart] Successfully spawned {thingDef.defName} at {candidatePos} (distance to nearest: {GetMinDistanceToOthers(candidatePos, spawnedPositions)})");
                        break;
                    }
                }
                
                // 如果找不到满足间距条件的位置，放宽条件
                if (!foundPosition)
                {
                    WulaLog.Debug($"[QuestPart] Could not find position with required spacing for {thingDef.defName}, trying with reduced spacing");
                    
                    // 尝试使用减半的间距
                    bool foundWithReducedSpacing = TrySpawnWithReducedSpacing(thingDef, faction, targetMap, spawnedPositions, allPossibleCells, minSpacing / 2, ref successCount, ref attempts);
                    
                    if (!foundWithReducedSpacing)
                    {
                        // 如果还找不到，尝试不使用间距条件
                        WulaLog.Debug($"[QuestPart] Still couldn't find position, falling back to no spacing requirement");
                        foundWithReducedSpacing = TrySpawnWithReducedSpacing(thingDef, faction, targetMap, spawnedPositions, allPossibleCells, 0, ref successCount, ref attempts);
                    }
                    
                    if (!foundWithReducedSpacing)
                    {
                        WulaLog.Debug($"[QuestPart] Failed to spawn {thingDef.defName} after multiple attempts");
                    }
                }
            }
            
            WulaLog.Debug($"[QuestPart] Spread-out algorithm completed: {successCount}/{spawnCount} spawned");
            return successCount;
        }
        
        /// <summary>
        /// 尝试使用减小的间距生成建筑
        /// </summary>
        private bool TrySpawnWithReducedSpacing(ThingDef thingDef, Faction faction, Map targetMap, List<IntVec3> spawnedPositions, List<IntVec3> allPossibleCells, int reducedSpacing, ref int successCount, ref int attempts)
        {
            const int maxReducedAttempts = 50;
            int attemptsForReduced = 0;
            
            while (attemptsForReduced < maxReducedAttempts && attempts < 500)
            {
                attempts++;
                attemptsForReduced++;
                
                IntVec3 candidatePos = allPossibleCells.RandomElement();
                
                // 检查是否满足减小的间距条件
                bool meetsSpacing = true;
                foreach (var existingPos in spawnedPositions)
                {
                    float distance = candidatePos.DistanceTo(existingPos);
                    if (distance < reducedSpacing)
                    {
                        meetsSpacing = false;
                        break;
                    }
                }
                
                if (meetsSpacing)
                {
                    Thing thing = ThingMaker.MakeThing(thingDef);
                    
                    if (faction != null)
                    {
                        thing.SetFaction(faction);
                    }

                    GenSpawn.Spawn(thing, candidatePos, targetMap);
                    spawnedPositions.Add(candidatePos);
                    successCount++;
                    
                    // 从可能位置列表中移除这个位置及其周围的位置
                    allPossibleCells.RemoveAll(cell => cell.DistanceTo(candidatePos) < reducedSpacing / 2);
                    
                    WulaLog.Debug($"[QuestPart] Successfully spawned {thingDef.defName} at {candidatePos} with reduced spacing {reducedSpacing}");
                    return true;
                }
            }
            
            return false;
        }
        
        /// <summary>
        /// 获取到其他位置的最小距离
        /// </summary>
        private float GetMinDistanceToOthers(IntVec3 position, List<IntVec3> otherPositions)
        {
            if (otherPositions.Count == 0) return float.MaxValue;
            
            float minDistance = float.MaxValue;
            foreach (var otherPos in otherPositions)
            {
                if (otherPos == position) continue;
                float distance = position.DistanceTo(otherPos);
                if (distance < minDistance)
                {
                    minDistance = distance;
                }
            }
            return minDistance;
        }
        
        /// <summary>
        /// 生成所有可能的位置列表
        /// </summary>
        private List<IntVec3> GeneratePossibleCells(Map map, ThingDef thingDef, bool allowThickRoof, bool allowThinRoof, int maxCellsToCheck)
        {
            List<IntVec3> possibleCells = new List<IntVec3>();
            int cellsChecked = 0;
            
            // 方法1：随机采样
            for (int i = 0; i < maxCellsToCheck && possibleCells.Count < maxCellsToCheck / 2; i++)
            {
                IntVec3 randomCell = new IntVec3(
                    Rand.Range(thingDef.Size.x, map.Size.x - thingDef.Size.x),
                    0,
                    Rand.Range(thingDef.Size.z, map.Size.z - thingDef.Size.z)
                );

                if (randomCell.InBounds(map) && IsValidForSkyfallerDrop(map, randomCell, thingDef, allowThickRoof, allowThinRoof))
                {
                    possibleCells.Add(randomCell);
                }
                cellsChecked++;
            }
            
            // 方法2：如果随机采样得到的数量不足，尝试使用网格采样
            if (possibleCells.Count < maxCellsToCheck / 4)
            {
                int gridStep = Mathf.Max(3, Mathf.CeilToInt(Mathf.Sqrt(map.Size.x * map.Size.z / maxCellsToCheck)));
                
                for (int x = thingDef.Size.x; x < map.Size.x - thingDef.Size.x && possibleCells.Count < maxCellsToCheck; x += gridStep)
                {
                    for (int z = thingDef.Size.z; z < map.Size.z - thingDef.Size.z && possibleCells.Count < maxCellsToCheck; z += gridStep)
                    {
                        IntVec3 gridCell = new IntVec3(x, 0, z);
                        
                        if (gridCell.InBounds(map) && IsValidForSkyfallerDrop(map, gridCell, thingDef, allowThickRoof, allowThinRoof))
                        {
                            possibleCells.Add(gridCell);
                        }
                        cellsChecked++;
                    }
                }
            }
            
            WulaLog.Debug($"[QuestPart] Generated {possibleCells.Count} possible cells after checking {cellsChecked} cells");
            return possibleCells;
        }

        /// <summary>
        /// 在有效位置生成多个建筑（原算法）
        /// </summary>
        private int SpawnThingsAtValidLocations(ThingDef thingDef, Faction faction, int spawnCount, Map targetMap, bool allowThickRoof, bool allowThinRoof)
        {
            int successCount = 0;
            int attempts = 0;
            const int maxAttempts = 100; // 最大尝试次数

            for (int i = 0; i < spawnCount && attempts < maxAttempts; i++)
            {
                attempts++;
                IntVec3 spawnPos = FindSpawnPositionForSkyfaller(targetMap, thingDef, allowThickRoof, allowThinRoof);

                if (spawnPos.IsValid)
                {
                    Thing thing = ThingMaker.MakeThing(thingDef);
                    
                    if (faction != null)
                    {
                        thing.SetFaction(faction);
                    }

                    GenSpawn.Spawn(thing, spawnPos, targetMap);
                    successCount++;

                    WulaLog.Debug($"[QuestPart] Successfully spawned {thingDef.defName} at {spawnPos} for faction {faction?.Name ?? "None"}");
                }
                else
                {
                    WulaLog.Debug($"[QuestPart] Failed to find valid spawn position for {thingDef.defName} (attempt {attempts})");
                }
            }

            return successCount;
        }

        /// <summary>
        /// 查找适合Skyfaller空投的位置
        /// </summary>
        private IntVec3 FindSpawnPositionForSkyfaller(Map map, ThingDef thingDef, bool allowThickRoof, bool allowThinRoof)
        {
            var potentialCells = new List<IntVec3>();

            // 策略1：首先尝试玩家基地附近的开放区域
            var baseCells = GetOpenAreaCellsNearBase(map, thingDef.Size);
            foreach (var cell in baseCells)
            {
                if (IsValidForSkyfallerDrop(map, cell, thingDef, allowThickRoof, allowThinRoof))
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
                if (IsValidForSkyfallerDrop(map, cell, thingDef, allowThickRoof, allowThinRoof))
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

                if (randomCell.InBounds(map) && IsValidForSkyfallerDrop(map, randomCell, thingDef, allowThickRoof, allowThinRoof))
                {
                    potentialCells.Add(randomCell);
                }
                if (potentialCells.Count > 5) break;
            }

            if (potentialCells.Count > 0)
            {
                return potentialCells.RandomElement();
            }

            WulaLog.Debug($"[QuestPart] No valid positions found for {thingDef.defName} after exhaustive search");
            return IntVec3.Invalid;
        }

        /// <summary>
        /// 基于Skyfaller实际行为的有效性检查
        /// </summary>
        private bool IsValidForSkyfallerDrop(Map map, IntVec3 cell, ThingDef thingDef, bool allowThickRoof, bool allowThinRoof)
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

                // 3. 检查厚岩顶 - 绝对不允许（除非明确允许）
                RoofDef roof = occupiedCell.GetRoof(map);
                if (roof != null && roof.isThickRoof)
                {
                    if (!allowThickRoof)
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
                    if (!allowThinRoof)
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
