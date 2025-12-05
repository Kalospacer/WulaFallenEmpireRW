using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    public class Designator_CallSkyfallerInArea : Designator
    {
        // 记录已经处理过的建筑（避免重复）
        private HashSet<Thing> processedBuildings = new HashSet<Thing>();
        
        // 组件类型过滤
        public bool includeBuildingSpawner = true;
        public bool includeSkyfallerCaller = true;
        
        public Designator_CallSkyfallerInArea()
        {
            defaultLabel = "WULA_Designator_CallInArea".Translate();
            defaultDesc = "WULA_Designator_CallInAreaDesc".Translate();
            icon = ContentFinder<Texture2D>.Get("Wula/UI/Designators/Designator_CallInArea");
            soundDragSustain = SoundDefOf.Designate_DragStandard;
            soundDragChanged = SoundDefOf.Designate_DragStandard_Changed;
            useMouseIcon = true;
            soundSucceeded = SoundDefOf.Designate_Claim;
            hotKey = KeyBindingDefOf.Misc12;
            tutorTag = "CallInArea";
        }

        public override DrawStyleCategoryDef DrawStyleCategory => DrawStyleCategoryDefOf.FilledRectangle;

        public override AcceptanceReport CanDesignateCell(IntVec3 c)
        {
            if (!c.InBounds(Map))
                return false;
            
            // 允许在迷雾格上选择（就像拆除和索赔设计器一样）
            if (c.Fogged(Map))
                return false;
            
            // 检查单元格内是否有符合条件的玩家建筑
            var things = Map.thingGrid.ThingsListAt(c);
            foreach (var thing in things)
            {
                if (thing.def.category == ThingCategory.Building &&
                    thing.Faction == Faction.OfPlayer &&
                    HasValidComponent(thing))
                {
                    return true;
                }
            }
            
            // 即使单元格内没有符合条件的建筑，也允许选择（这样用户可以拖动区域）
            return true;
        }
        
        // 检查建筑是否有有效的组件
        private bool HasValidComponent(Thing thing)
        {
            // 检查 Building Spawner 组件
            if (includeBuildingSpawner)
            {
                var buildingSpawner = thing.TryGetComp<CompBuildingSpawner>();
                if (buildingSpawner != null && buildingSpawner.CanCallBuilding)
                    return true;
            }
            
            // 检查 Skyfaller Caller 组件
            if (includeSkyfallerCaller)
            {
                var skyfallerCaller = thing.TryGetComp<CompSkyfallerCaller>();
                if (skyfallerCaller != null && skyfallerCaller.CanCallSkyfaller)
                    return true;
            }
            
            return false;
        }

        public override void DesignateSingleCell(IntVec3 c)
        {
            // 处理单个单元格内的所有建筑
            ProcessCell(c);
        }

        public override void DesignateMultiCell(IEnumerable<IntVec3> cells)
        {
            // 清除已处理的建筑记录
            processedBuildings.Clear();
            
            int totalBuildings = 0;
            int buildingSpawnerCount = 0;
            int skyfallerCallerCount = 0;
            
            // 处理所有选中的单元格
            foreach (var cell in cells)
            {
                if (cell.InBounds(Map))
                {
                    int cellCount = processedBuildings.Count;
                    ProcessCell(cell);
                    int newBuildings = processedBuildings.Count - cellCount;
                    
                    // 统计每个组件类型的调用数量
                    foreach (var building in processedBuildings)
                    {
                        if (building.Destroyed) continue;
                        
                        if (building.TryGetComp<CompBuildingSpawner>()?.calling == true)
                            buildingSpawnerCount++;
                        else if (building.TryGetComp<CompSkyfallerCaller>()?.calling == true)
                            skyfallerCallerCount++;
                    }
                    
                    totalBuildings += newBuildings;
                }
            }
            
            // 显示结果消息
            if (totalBuildings > 0)
            {
                string message = "WULA_AreaCallInitiated".Translate(totalBuildings);
                
                if (buildingSpawnerCount > 0 && skyfallerCallerCount > 0)
                {
                    message += "\n" + "WULA_BothComponentsCalled".Translate(
                        buildingSpawnerCount, skyfallerCallerCount);
                }
                else if (buildingSpawnerCount > 0)
                {
                    message += "\n" + "WULA_BuildingSpawnerCalled".Translate(buildingSpawnerCount);
                }
                else if (skyfallerCallerCount > 0)
                {
                    message += "\n" + "WULA_SkyfallerCallerCalled".Translate(skyfallerCallerCount);
                }
                
                Messages.Message(message, MessageTypeDefOf.PositiveEvent);
            }
            else
            {
                Messages.Message("WULA_NoCallableBuildingsInArea".Translate(), 
                    MessageTypeDefOf.NeutralEvent);
            }
        }

        private void ProcessCell(IntVec3 cell)
        {
            var things = Map.thingGrid.ThingsListAt(cell);
            
            foreach (var thing in things)
            {
                // 跳过非建筑
                if (thing.def.category != ThingCategory.Building)
                    continue;
                
                // 跳过非玩家建筑
                if (thing.Faction != Faction.OfPlayer)
                    continue;
                
                // 检查是否已经处理过（避免重复处理同一个建筑）
                if (processedBuildings.Contains(thing))
                    continue;
                
                // 标记为已处理
                processedBuildings.Add(thing);
                
                // 尝试调用两种组件（如果有且可以调用）
                bool anyCalled = false;
                
                // 1. 先尝试 Building Spawner
                if (includeBuildingSpawner)
                {
                    var buildingSpawner = thing.TryGetComp<CompBuildingSpawner>();
                    if (buildingSpawner != null && buildingSpawner.CanCallBuilding)
                    {
                        buildingSpawner.CallBuilding(false);
                        anyCalled = true;
                        
                        // 如果建筑被销毁，记录日志
                        if (thing.Destroyed)
                        {
                            Log.Message($"[Designator] Building destroyed after BuildingSpawner call at {cell}");
                        }
                    }
                }
                
                // 2. 尝试 Skyfaller Caller（如果建筑还存在）
                if (!thing.Destroyed && includeSkyfallerCaller)
                {
                    var skyfallerCaller = thing.TryGetComp<CompSkyfallerCaller>();
                    if (skyfallerCaller != null && skyfallerCaller.CanCallSkyfaller)
                    {
                        skyfallerCaller.CallSkyfaller(false);
                        anyCalled = true;
                    }
                }
                
                // 如果没有任何组件被调用，从处理列表中移除（防止重复尝试）
                if (!anyCalled)
                {
                    processedBuildings.Remove(thing);
                }
            }
        }

        public override AcceptanceReport CanDesignateThing(Thing t)
        {
            // 这里提供单个建筑的反向设计器支持（右键菜单）
            if (t.def.category != ThingCategory.Building)
                return false;
            
            if (t.Faction != Faction.OfPlayer)
                return false;
            
            return HasValidComponent(t);
        }

        public override void DesignateThing(Thing t)
        {
            // 用于反向设计器（右键菜单）
            processedBuildings.Add(t);
            
            // 尝试调用两种组件
            bool anyCalled = false;
            
            // 1. 先尝试 Building Spawner
            if (includeBuildingSpawner)
            {
                var buildingSpawner = t.TryGetComp<CompBuildingSpawner>();
                if (buildingSpawner != null && buildingSpawner.CanCallBuilding)
                {
                    buildingSpawner.CallBuilding(false);
                    anyCalled = true;
                }
            }
            
            // 2. 尝试 Skyfaller Caller（如果建筑还存在）
            if (!t.Destroyed && includeSkyfallerCaller)
            {
                var skyfallerCaller = t.TryGetComp<CompSkyfallerCaller>();
                if (skyfallerCaller != null && skyfallerCaller.CanCallSkyfaller)
                {
                    skyfallerCaller.CallSkyfaller(false);
                    anyCalled = true;
                }
            }
            
            if (!anyCalled)
            {
                Messages.Message("WULA_NoComponentCanCall".Translate(), 
                    t, MessageTypeDefOf.RejectInput);
            }
        }

        public override void SelectedUpdate()
        {
            // 参考Designator_Deconstruct，只绘制鼠标悬停方框
            GenUI.RenderMouseoverBracket();
            
            // 可以添加额外的视觉效果来显示哪些建筑将被影响
            if (Find.DesignatorManager.SelectedDesignator == this)
            {
                DrawAffectedBuildings();
            }
        }
        
        // 绘制受影响的建筑
        private void DrawAffectedBuildings()
        {
            if (Map == null) return;
            
            // 这里可以绘制高亮显示哪些建筑会被影响
            // 但由于性能考虑，只在特定条件下绘制
            if (DebugSettings.godMode)
            {
                foreach (var building in Map.listerBuildings.allBuildingsColonist)
                {
                    if (HasValidComponent(building))
                    {
                        GenDraw.DrawFieldEdges(new List<IntVec3> { building.Position }, 
                            building.Destroyed ? Color.red : Color.green);
                    }
                }
            }
        }
    }
}
