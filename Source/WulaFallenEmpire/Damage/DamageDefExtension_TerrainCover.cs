using System.Collections.Generic;
using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    // 地形覆盖配置的 ModExtension
    public class DamageDefExtension_TerrainCover : DefModExtension
    {
        // 要生成的地形定义
        public TerrainDef terrainToSpawn;
        
        // 生成概率（0-1）
        public float terrainChance = 1f;
        
        // 检查特定单元格是否允许生成地形
        public bool CanAffectCell(IntVec3 cell, Map map, out string reason)
        {
            reason = null;
            
            if (!cell.InBounds(map))
            {
                reason = "Cell out of bounds";
                return false;
            }
            
            // 检查地形类型
            TerrainDef currentTerrain = cell.GetTerrain(map);
            
            // 检查是否可以构建地形
            if (!GenConstruct.CanBuildOnTerrain(terrainToSpawn, cell, map, Rot4.North))
            {
                reason = "Cannot build on terrain";
                return false;
            }
            
            return true;
        }
    }
}
