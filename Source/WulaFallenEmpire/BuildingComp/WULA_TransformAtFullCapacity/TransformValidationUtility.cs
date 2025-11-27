using RimWorld;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Verse;
using System.Linq;

namespace WulaFallenEmpire
{
    public static class TransformValidationUtility
    {
        /// <summary>
        /// 检查位置是否可以放置目标建筑（通用版本）
        /// </summary>
        public static bool CanPlaceBuildingAt(ThingDef buildingDef, IntVec3 center, Map map, Faction faction, out string failReason)
        {
            return CanPlaceBuildingAt(buildingDef, center, map, faction, null, out failReason);
        }

        /// <summary>
        /// 检查位置是否可以放置目标建筑（带忽略物体版本）
        /// </summary>
        public static bool CanPlaceBuildingAt(ThingDef buildingDef, IntVec3 center, Map map, Faction faction, Thing thingToIgnore, out string failReason)
        {
            failReason = null;

            // 1. 检查建筑定义
            if (buildingDef == null)
            {
                failReason = "目标建筑定义为空";
                return false;
            }

            // 2. 检查建筑尺寸是否为奇数（根据您的需求）
            if (!IsOddSizedBuilding(buildingDef))
            {
                failReason = $"建筑 {buildingDef.LabelCap} 的尺寸不是奇数，不符合放置要求";
                return false;
            }

            // 3. 检查地图边界
            if (!IsWithinMapBounds(buildingDef, center, map, out failReason))
                return false;

            // 4. 检查地形affordance
            if (!HasValidTerrainAffordance(buildingDef, center, map, out failReason))
                return false;

            // 5. 检查其他建筑挤占（排除要忽略的物体）
            if (!IsAreaClearOfBlockingBuildings(buildingDef, center, map, thingToIgnore, out failReason))
                return false;

            // 6. 检查特殊放置条件（如不可放置在水上等）
            if (!MeetsSpecialPlacementConditions(buildingDef, center, map, out failReason))
                return false;

            // 7. 使用RimWorld原生的放置检查（最终验证）
            if (!GenConstruct.CanPlaceBlueprintAt(buildingDef, center, Rot4.North, map, false, null, null, null))
            {
                failReason = "该位置不符合建筑放置要求";
                return false;
            }

            return true;
        }

        /// <summary>
        /// 检查建筑尺寸是否为奇数
        /// </summary>
        public static bool IsOddSizedBuilding(ThingDef buildingDef)
        {
            return buildingDef.Size.x % 2 == 1 && buildingDef.Size.z % 2 == 1;
        }

        /// <summary>
        /// 检查地图边界
        /// </summary>
        private static bool IsWithinMapBounds(ThingDef buildingDef, IntVec3 center, Map map, out string failReason)
        {
            CellRect occupiedRect = GenAdj.OccupiedRect(center, Rot4.North, buildingDef.Size);
            
            if (!occupiedRect.InBounds(map))
            {
                failReason = "建筑超出地图边界";
                return false;
            }

            failReason = null;
            return true;
        }

        /// <summary>
        /// 检查地形affordance
        /// </summary>
        private static bool HasValidTerrainAffordance(ThingDef buildingDef, IntVec3 center, Map map, out string failReason)
        {
            TerrainAffordanceDef requiredAffordance = buildingDef.terrainAffordanceNeeded;
            if (requiredAffordance == null)
            {
                // 如果没有指定affordance要求，则跳过检查
                failReason = null;
                return true;
            }

            CellRect occupiedRect = GenAdj.OccupiedRect(center, Rot4.North, buildingDef.Size);
            
            foreach (IntVec3 cell in occupiedRect)
            {
                if (!cell.InBounds(map))
                    continue;

                TerrainDef terrain = map.terrainGrid.TerrainAt(cell);
                if (terrain == null || !terrain.affordances.Contains(requiredAffordance))
                {
                    failReason = $"地形 {terrain?.LabelCap ?? "未知"} 不支持放置 {buildingDef.LabelCap}";
                    return false;
                }
            }

            failReason = null;
            return true;
        }

        /// <summary>
        /// 检查区域是否被其他建筑阻挡（排除指定物体）
        /// </summary>
        private static bool IsAreaClearOfBlockingBuildings(ThingDef buildingDef, IntVec3 center, Map map, Thing thingToIgnore, out string failReason)
        {
            CellRect occupiedRect = GenAdj.OccupiedRect(center, Rot4.North, buildingDef.Size);
            List<Thing> blockingThings = new List<Thing>();

            foreach (IntVec3 cell in occupiedRect)
            {
                if (!cell.InBounds(map))
                    continue;

                // 检查该单元格上的所有建筑
                List<Thing> thingList = map.thingGrid.ThingsListAt(cell);
                foreach (Thing thing in thingList)
                {
                    // 跳过要忽略的物体（如被转换的Pawn本身）
                    if (thing == thingToIgnore)
                        continue;

                    if (thing.def.category == ThingCategory.Building || thing.def.category == ThingCategory.Pawn)
                    {
                        // 忽略蓝图和框架
                        if (thing.def.IsBlueprint || thing.def.IsFrame)
                            continue;

                        // 忽略被转换的Pawn本身
                        if (thing == thingToIgnore)
                            continue;

                        blockingThings.Add(thing);
                    }
                }
            }

            if (blockingThings.Count > 0)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("以下物体阻挡了建筑放置:");
                
                foreach (Thing thing in blockingThings)
                {
                    sb.AppendLine($"  - {thing.LabelCap}");
                }
                
                failReason = sb.ToString().TrimEnd();
                return false;
            }

            failReason = null;
            return true;
        }

        /// <summary>
        /// 检查特殊放置条件
        /// </summary>
        private static bool MeetsSpecialPlacementConditions(ThingDef buildingDef, IntVec3 center, Map map, out string failReason)
        {
            CellRect occupiedRect = GenAdj.OccupiedRect(center, Rot4.North, buildingDef.Size);

            // 检查是否在水体上
            foreach (IntVec3 cell in occupiedRect)
            {
                if (!cell.InBounds(map))
                    continue;

                TerrainDef terrain = map.terrainGrid.TerrainAt(cell);
                if (terrain != null && (terrain.IsWater || terrain.defName.Contains("Water")))
                {
                    failReason = "无法在水体上放置建筑";
                    return false;
                }

                // 检查是否在不可建造的地形上
                if (terrain != null && !terrain.BuildableByPlayer)
                {
                    failReason = $"地形 {terrain.LabelCap} 不可建造";
                    return false;
                }
            }

            failReason = null;
            return true;
        }

        /// <summary>
        /// 获取建筑占用的所有单元格
        /// </summary>
        public static List<IntVec3> GetOccupiedCells(ThingDef buildingDef, IntVec3 center)
        {
            return GenAdj.OccupiedRect(center, Rot4.North, buildingDef.Size).Cells.ToList();
        }
    }
}
