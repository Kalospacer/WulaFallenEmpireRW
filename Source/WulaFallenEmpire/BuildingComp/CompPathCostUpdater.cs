using System.Collections.Generic;
using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class CompPathCostUpdater : ThingComp
    {
        private CompProperties_PathCostUpdater Props => (CompProperties_PathCostUpdater)props;
        
        // 记录是否需要更新路径成本
        private bool needsPathUpdate = false;
        
        // 记录需要更新的区域
        private CellRect updateRect;
        
        public override void Initialize(CompProperties props)
        {
            base.Initialize(props);
            
            if (parent.Spawned)
            {
                MarkForPathUpdate();
            }
        }
        
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            
            // 如果组件在生成后添加，也需要标记更新
            if (!respawningAfterLoad)
            {
                MarkForPathUpdate();
            }
        }
        
        public override void CompTick()
        {
            base.CompTick();
            
            // 每帧检查是否需要更新路径
            if (needsPathUpdate && parent.Spawned)
            {
                UpdatePathCosts();
                needsPathUpdate = false;
            }
        }
        
        /// <summary>
        /// 标记需要更新路径成本
        /// </summary>
        public void MarkForPathUpdate()
        {
            if (!parent.Spawned)
                return;
                
            needsPathUpdate = true;
            
            // 计算需要更新的区域
            updateRect = parent.OccupiedRect();
            
            // 根据建筑大小决定扩展区域
            int expandBy = CalculateExpandDistance();
            updateRect = updateRect.ExpandedBy(expandBy);
            updateRect = updateRect.ClipInsideMap(parent.Map);
        }
        
        /// <summary>
        /// 根据建筑大小计算需要扩展的距离
        /// </summary>
        private int CalculateExpandDistance()
        {
            if (Props == null || !Props.adaptiveExpansion)
                return 1;
                
            // 根据建筑尺寸决定扩展距离
            int size = parent.def.size.x * parent.def.size.z;
            
            if (size <= 1)      // 1x1 建筑
                return 1;
            else if (size <= 4) // 2x2 或 1x4 建筑
                return 2;
            else if (size <= 9) // 3x3 建筑
                return 3;
            else                // 大型建筑
                return 4;
        }
        
        /// <summary>
        /// 更新路径成本
        /// </summary>
        public void UpdatePathCosts()
        {
            if (!parent.Spawned)
                return;
                
            Map map = parent.Map;
            
            // 1. 更新路径网格
            RecalculatePathGrid(map);
            
            // 2. 更新区域网格（如果需要）
            UpdateRegionGrid(map);
            
            // 3. 清理可达性缓存
            ClearReachabilityCache(map);
        }
        
        /// <summary>
        /// 重新计算路径网格
        /// </summary>
        private void RecalculatePathGrid(Map map)
        {
            // 使用局部变量跟踪是否已通知过变化
            bool haveNotified = false;
            
            // 更新指定区域内的路径成本
            foreach (IntVec3 cell in updateRect)
            {
                if (cell.InBounds(map))
                {
                    // 调用PathGrid的正确方法（根据您提供的PathGrid代码）
                    map.pathGrid.RecalculatePerceivedPathCostAt(cell, ref haveNotified);
                    
                    // 如果是可通行变化较大的建筑，需要更新相邻单元格
                    if (parent.def.passability == Traversability.Impassable ||
                        parent.def.passability == Traversability.PassThroughOnly)
                    {
                        foreach (IntVec3 adjacent in GenAdj.AdjacentCells)
                        {
                            IntVec3 adjCell = cell + adjacent;
                            if (adjCell.InBounds(map))
                            {
                                map.pathGrid.RecalculatePerceivedPathCostAt(adjCell, ref haveNotified);
                            }
                        }
                    }
                }
            }
        }
        
        /// <summary>
        /// 更新区域网格
        /// </summary>
        private void UpdateRegionGrid(Map map)
        {
            if (Props.updateRegions && parent.def.AffectsRegions)
            {
                // 标记区域网格需要更新
                map.regionAndRoomUpdater.Enabled = true;
                map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
            }
            else
            {
                // 只更新受影响区域
                foreach (IntVec3 cell in updateRect)
                {
                    if (cell.InBounds(map))
                    {
                        // 根据PathGrid代码，使用Notify_WalkabilityChanged方法
                        map.regionDirtyer.Notify_WalkabilityChanged(cell, map.pathGrid.WalkableFast(cell));
                    }
                }
            }
        }
        
        /// <summary>
        /// 清理可达性缓存
        /// </summary>
        private void ClearReachabilityCache(Map map)
        {
            // 清理整个地图的可达性缓存
            map.reachability.ClearCache();
        }
        
        /// <summary>
        /// 强制立即更新路径（用于特殊事件）
        /// </summary>
        public void ForceImmediateUpdate()
        {
            if (parent.Spawned)
            {
                UpdatePathCosts();
                needsPathUpdate = false;
            }
        }
        
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (var gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }
            
            if (DebugSettings.godMode)
            {
                yield return new Command_Action
                {
                    defaultLabel = "DEBUG: Update Path Costs",
                    defaultDesc = "Force update path costs for this building",
                    action = ForceImmediateUpdate
                };
            }
        }
        
        // 移除PostDeSpawn方法，因为不需要处理销毁
    }
    
    public class CompProperties_PathCostUpdater : CompProperties
    {
        // 是否启用自适应扩展（根据建筑大小决定更新区域）
        public bool adaptiveExpansion = true;
        
        // 是否重建区域网格（性能消耗较大）
        public bool updateRegions = false;
        
        // 固定扩展距离（如果adaptiveExpansion为false）
        public int fixedExpansionDistance = 1;
        
        public CompProperties_PathCostUpdater()
        {
            compClass = typeof(CompPathCostUpdater);
        }
    }
}
