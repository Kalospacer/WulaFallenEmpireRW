using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    /// <summary>
    /// 13x13小型口袋空间生成器
    /// 创建一个简单的13x13空间，边缘是墙，中间是空地，适合作为穿梭机内部空间
    /// </summary>
    public class GenStep_WulaPocketSpaceSmall : GenStep
    {
        public override int SeedPart => 928735; // 不同于AncientStockpile的种子

        public override void Generate(Map map, GenStepParams parms)
        {
            try
            {
                Log.Message($"[WULA] Generating WULA pocket space, map size: {map.Size}");
                
                // 获取地图边界
                IntVec3 mapSize = map.Size;
                
                // 生成外围岩石墙壁
                GenerateWalls(map);
                
                // 生成内部地板
                GenerateFloor(map);
                
                Log.Message("[WULA] WULA pocket space generation completed");
                
                // 添加预制件生成
                // 注意：这里需要根据实际的PrefabDef名称进行加载
                // 暂时使用一个示例PrefabDef名称，实际使用时应替换
                PrefabDef customPrefabDef = DefDatabase<PrefabDef>.GetNamed("YourCustomPrefabDefName", false);
                if (customPrefabDef != null)
                {
                    GeneratePrefab(map, customPrefabDef);
                    Log.Message($"[WULA] Generated custom prefab: {customPrefabDef.defName}");
                }
                else
                {
                    Log.Warning("[WULA] Custom prefab 'YourCustomPrefabDefName' not found. Skipping prefab generation.");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[WULA] Error generating WULA pocket space: {ex}");
            }
        }

        /// <summary>
        /// 生成外围墙壁
        /// </summary>
        private void GenerateWalls(Map map)
        {
            IntVec3 mapSize = map.Size;
            
            // 获取地形和物品定义
            TerrainDef roughTerrain = DefDatabase<TerrainDef>.GetNamed("Granite_Rough", false) ?? 
                                    DefDatabase<TerrainDef>.GetNamed("Granite_Smooth", false) ??
                                    DefDatabase<TerrainDef>.GetNamed("Sandstone_Rough", false);
            
            ThingDef rockWallDef = DefDatabase<ThingDef>.GetNamed("Wall_Rock", false) ??
                                 DefDatabase<ThingDef>.GetNamed("Wall", false);
            
            // 遍历地图边缘，放置WulaWall
            for (int x = 0; x < mapSize.x; x++)
            {
                for (int z = 0; z < mapSize.z; z++)
                {
                    // 如果是边缘位置，放置WulaWall
                    if (x == 0 || x == mapSize.x - 1 || z == 0 || z == mapSize.z - 1)
                    {
                        IntVec3 pos = new IntVec3(x, 0, z);
                        
                        // 设置地形为岩石基础
                        if (roughTerrain != null)
                        {
                            map.terrainGrid.SetTerrain(pos, roughTerrain);
                        }
                        
                        // 放置WulaWall
                        ThingDef wallDef = DefDatabase<ThingDef>.GetNamed("WulaWall", false);
                        if (wallDef != null)
                        {
                            Thing wall = ThingMaker.MakeThing(wallDef);
                            wall.SetFaction(null);
                            GenPlace.TryPlaceThing(wall, pos, map, ThingPlaceMode.Direct);
                        }
                        else if (rockWallDef != null)
                        {
                            // 如果WulaWall不存在，使用原版岩石墙作为备选
                            Thing wall = ThingMaker.MakeThing(rockWallDef);
                            wall.SetFaction(null);
                            GenPlace.TryPlaceThing(wall, pos, map, ThingPlaceMode.Direct);
                            Log.Warning("[WULA] WulaWall not found, using fallback wall");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 生成内部地板
        /// </summary>
        private void GenerateFloor(Map map)
        {
            IntVec3 mapSize = map.Size;
            
            // 为内部区域设置WulaFloor
            TerrainDef floorDef = DefDatabase<TerrainDef>.GetNamed("WulaFloor", false);
            TerrainDef fallbackFloor = floorDef ?? 
                                     DefDatabase<TerrainDef>.GetNamed("Steel", false) ??
                                     DefDatabase<TerrainDef>.GetNamed("MetalTile", false) ??
                                     DefDatabase<TerrainDef>.GetNamed("Concrete", false);
            
            if (floorDef == null)
            {
                Log.Warning("[WULA] WulaFloor not found, using fallback floor");
            }
            
            // 清理内部区域并设置正确的地板
            for (int x = 1; x < mapSize.x - 1; x++)
            {
                for (int z = 1; z < mapSize.z - 1; z++)
                {
                    IntVec3 pos = new IntVec3(x, 0, z);
                    
                    // 清理该位置的所有岩石和阻挡物
                    ClearCellAndSetFloor(map, pos, fallbackFloor);
                }
            }
            
            Log.Message($"[WULA] Set floor for internal area ({mapSize.x-2}x{mapSize.z-2}) to {(floorDef?.defName ?? fallbackFloor?.defName)}");
        }
        
        /// <summary>
        /// 清理单元格并设置地板
        /// </summary>
        private void ClearCellAndSetFloor(Map map, IntVec3 pos, TerrainDef floorDef)
        {
            if (!pos.InBounds(map)) return;
            
            try
            {
                // 获取该位置的所有物品
                List<Thing> thingsAtPos = pos.GetThingList(map).ToList(); // 创建副本避免修改时出错
                
                // 清理所有建筑物和岩石（强力清理，确保地板可以放置）
                foreach (Thing thing in thingsAtPos)
                {
                    bool shouldRemove = false;
                    
                    // 检查是否为建筑物
                    if (thing.def.category == ThingCategory.Building)
                    {
                        // 如果是自然岩石
                        if (thing.def.building?.isNaturalRock == true)
                        {
                            shouldRemove = true;
                        }
                        // 或者是岩石相关的建筑
                        else if (thing.def.defName.Contains("Rock") ||
                                thing.def.defName.Contains("Slate") ||
                                thing.def.defName.Contains("Granite") ||
                                thing.def.defName.Contains("Sandstone") ||
                                thing.def.defName.Contains("Limestone") ||
                                thing.def.defName.Contains("Marble") ||
                                thing.def.defName.Contains("Quartzite") ||
                                thing.def.defName.Contains("Jade"))
                        {
                            shouldRemove = true;
                        }
                        // 或者是其他阻挡的建筑物（除了我们的乌拉墙）
                        else if (!thing.def.defName.Contains("Wula") && thing.def.Fillage == FillCategory.Full)
                        {
                            shouldRemove = true;
                        }
                    }
                    
                    if (shouldRemove)
                    {
                        if (Prefs.DevMode) // 只在开发模式下输出详细日志
                        {
                            Log.Message($"[WULA] Removing {thing.def.defName} at {pos} to make space for floor");
                        }
                        thing.Destroy(DestroyMode.Vanish);
                    }
                }
                
                // 在清理后稍微延迟，再检查一次（确保彻底清理）
                thingsAtPos = pos.GetThingList(map).ToList();
                foreach (Thing thing in thingsAtPos)
                {
                    if (thing.def.category == ThingCategory.Building && thing.def.Fillage == FillCategory.Full)
                    {
                        Log.Warning($"[WULA] Force removing remaining building {thing.def.defName} at {pos}");
                        thing.Destroy(DestroyMode.Vanish);
                    }
                }
                
                // 设置地板地形
                if (floorDef != null)
                {
                    map.terrainGrid.SetTerrain(pos, floorDef);
                    if (Prefs.DevMode)
                    {
                        Log.Message($"[WULA] Set terrain at {pos} to {floorDef.defName}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[WULA] Error clearing cell at {pos}: {ex}");
            }
        }

        /// <summary>
        /// 生成预制件
        /// </summary>
        private void GeneratePrefab(Map map, PrefabDef prefabDef)
        {
            if (prefabDef == null)
            {
                Log.Error("[WULA] PrefabDef is null, cannot generate prefab.");
                return;
            }

            // 获取预制件的中心点，将其放置在口袋空间的中心
            IntVec3 mapCenter = map.Center;
            IntVec3 prefabOrigin = mapCenter - new IntVec3(prefabDef.size.x / 2, 0, prefabDef.size.z / 2);

            // 生成物品
            foreach (var thingData in prefabDef.GetThings())
            {
                IntVec3 thingPos = prefabOrigin + thingData.cell;
                if (thingPos.InBounds(map))
                {
                    Thing thing = ThingMaker.MakeThing(thingData.data.def, thingData.data.stuff);
                    if (thing != null)
                    {
                        // PrefabThingData 不包含 factionDef，派系通常在生成时由上下文决定
                        // thing.SetFaction(thingData.data.factionDef != null ? Faction.OfPlayerSilentFail : null);
                        GenPlace.TryPlaceThing(thing, thingPos, map, ThingPlaceMode.Direct);
                    }
                }
            }

            // 生成地形
            foreach (var terrainData in prefabDef.GetTerrain())
            {
                IntVec3 terrainPos = prefabOrigin + terrainData.cell;
                if (terrainPos.InBounds(map))
                {
                    map.terrainGrid.SetTerrain(terrainPos, terrainData.data.def);
                }
            }

            // 递归生成子预制件（如果存在）
            foreach (var subPrefabData in prefabDef.GetPrefabs())
            {
                // 这里需要递归调用GeneratePrefab，但为了简化，暂时只处理顶层
                // 实际项目中，可能需要更复杂的逻辑来处理子预制件的位置和旋转
                Log.Warning($"[WULA] Sub-prefabs are not fully supported in this simple generator: {subPrefabData.data.def.defName}");
            }
        }
    }
}