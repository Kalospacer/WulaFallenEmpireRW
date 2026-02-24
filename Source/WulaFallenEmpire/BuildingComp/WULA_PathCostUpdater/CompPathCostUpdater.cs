using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    public class CompPathCostUpdater : ThingComp
    {
        private CompProperties_PathCostUpdater Props => (CompProperties_PathCostUpdater)props;
        
        // 记录是否需要更新路径成本
        private bool needsPathUpdate = false;
        
        // 记录需要更新的区域
        private CellRect updateRect;
        
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            
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
            
            // 使用map.pathing来更新路径成本
            // 根据Pathing.cs的RecalculatePerceivedPathCostUnderThing方法，我们可以直接调用它
            map.pathing.RecalculatePerceivedPathCostUnderThing(parent);
            
            // 清理可达性缓存
            ClearReachabilityCache(map);
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
    }
    
    public class CompProperties_PathCostUpdater : CompProperties
    {
        // 是否启用自适应扩展（根据建筑大小决定更新区域）
        public bool adaptiveExpansion = true;
        
        public CompProperties_PathCostUpdater()
        {
            compClass = typeof(CompPathCostUpdater);
        }
    }
}
