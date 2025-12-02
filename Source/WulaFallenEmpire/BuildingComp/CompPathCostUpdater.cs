using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    public class CompPathCostUpdater : ThingComp
    {
        private CompProperties_PathCostUpdater Props => (CompProperties_PathCostUpdater)props;
        
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            
            if (!respawningAfterLoad && parent.Spawned)
            {
                // 建筑生成时更新路径
                UpdatePathCosts();
            }
        }
        
        /// <summary>
        /// 更新路径成本
        /// </summary>
        public void UpdatePathCosts()
        {
            if (!parent.Spawned)
                return;
                
            Map map = parent.Map;
            
            // 获取建筑占用的所有单元格
            CellRect occupiedRect = parent.OccupiedRect();
            
            // 根据组件属性决定扩展范围
            int expandBy = Props != null && Props.expandToAdjacent ? Props.expandDistance : 0;
            CellRect updateRect = occupiedRect.ExpandedBy(expandBy).ClipInsideMap(map);
            
            // 更新指定区域内的路径成本
            bool haveNotified = false;
            foreach (IntVec3 cell in updateRect)
            {
                if (cell.InBounds(map))
                {
                    // 使用 Pathing 的正确方法
                    map.pathing.RecalculatePerceivedPathCostAt(cell);
                }
            }
            
            // 清理可达性缓存
            map.reachability.ClearCache();
        }
        
        /// <summary>
        /// 强制立即更新路径（用于特殊事件）
        /// </summary>
        public void ForceImmediateUpdate()
        {
            UpdatePathCosts();
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
    }
    
    public class CompProperties_PathCostUpdater : CompProperties
    {
        // 是否扩展到相邻单元格
        public bool expandToAdjacent = true;
        
        // 扩展距离
        public int expandDistance = 1;
        
        public CompProperties_PathCostUpdater()
        {
            compClass = typeof(CompPathCostUpdater);
        }
    }
}
