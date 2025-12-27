using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace WulaFallenEmpire.EventSystem.AI.Tools
{
    /// <summary>
    /// 采矿指令工具 - 在指定位置或区域添加采矿标记
    /// </summary>
    public class Tool_DesignateMine : AITool
    {
        public override string Name => "designate_mine";
        
        public override string Description => 
            "在指定坐标添加采矿标记。可以指定单个格子或矩形区域。只能标记可采矿的岩石。";
        
        public override string UsageSchema => 
            "<designate_mine><x>整数X坐标</x><z>整数Z坐标</z><radius>可选，整数半径，默认0表示单格</radius></designate_mine>";
        
        public override string Execute(string args)
        {
            try
            {
                var argsDict = ParseXmlArgs(args);
                
                // 解析坐标
                if (!argsDict.TryGetValue("x", out string xStr) || !int.TryParse(xStr, out int x))
                {
                    return "Error: 缺少有效的 x 坐标";
                }
                if (!argsDict.TryGetValue("z", out string zStr) || !int.TryParse(zStr, out int z))
                {
                    return "Error: 缺少有效的 z 坐标";
                }
                
                int radius = 0;
                if (argsDict.TryGetValue("radius", out string radiusStr))
                {
                    int.TryParse(radiusStr, out radius);
                }
                radius = Math.Max(0, Math.Min(10, radius)); // 限制半径 0-10
                
                // 获取地图
                Map map = Find.CurrentMap;
                if (map == null)
                {
                    return "Error: 没有活动的地图";
                }
                
                IntVec3 center = new IntVec3(x, 0, z);
                if (!center.InBounds(map))
                {
                    return $"Error: 坐标 ({x}, {z}) 超出地图边界";
                }
                
                // 收集要标记的格子
                List<IntVec3> cellsToMark = new List<IntVec3>();
                
                if (radius == 0)
                {
                    cellsToMark.Add(center);
                }
                else
                {
                    // 矩形区域
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        for (int dz = -radius; dz <= radius; dz++)
                        {
                            IntVec3 cell = new IntVec3(x + dx, 0, z + dz);
                            if (cell.InBounds(map))
                            {
                                cellsToMark.Add(cell);
                            }
                        }
                    }
                }
                
                int successCount = 0;
                int alreadyMarkedCount = 0;
                int notMineableCount = 0;
                
                foreach (var cell in cellsToMark)
                {
                    // 检查是否已有采矿标记
                    if (map.designationManager.DesignationAt(cell, DesignationDefOf.Mine) != null)
                    {
                        alreadyMarkedCount++;
                        continue;
                    }
                    
                    // 检查是否可采矿
                    Mineable mineable = cell.GetFirstMineable(map);
                    if (mineable == null)
                    {
                        notMineableCount++;
                        continue;
                    }
                    
                    // 添加采矿标记
                    map.designationManager.AddDesignation(new Designation(cell, DesignationDefOf.Mine));
                    successCount++;
                }
                
                // 生成结果报告
                if (successCount > 0)
                {
                    string result = $"Success: 已标记 {successCount} 个格子进行采矿";
                    if (alreadyMarkedCount > 0)
                    {
                        result += $"，{alreadyMarkedCount} 个已有标记";
                    }
                    if (notMineableCount > 0)
                    {
                        result += $"，{notMineableCount} 个不可采矿";
                    }
                    
                    Messages.Message($"AI: 标记了 {successCount} 处采矿", MessageTypeDefOf.NeutralEvent);
                    return result;
                }
                else if (alreadyMarkedCount > 0)
                {
                    return $"Info: 该区域 {alreadyMarkedCount} 个格子已有采矿标记";
                }
                else
                {
                    return $"Error: 坐标 ({x}, {z}) 附近没有可采矿的岩石";
                }
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[Tool_DesignateMine] Error: {ex}");
                return $"Error: 采矿指令失败 - {ex.Message}";
            }
        }
    }
}
