using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    public class Designator_CallSkyfallerInArea : Designator
    {
        private readonly new Texture2D icon;
        
        // 记录已经处理过的建筑（避免重复）
        private HashSet<Thing> processedBuildings = new HashSet<Thing>();
        
        public Designator_CallSkyfallerInArea()
        {
            defaultLabel = "WULA_Designator_CallSkyfallerInArea".Translate();
            defaultDesc = "WULA_Designator_CallSkyfallerInAreaDesc".Translate();
            icon = ContentFinder<Texture2D>.Get("Wula/UI/Designators/WULA_AreaSkyfaller");
            soundDragSustain = SoundDefOf.Designate_DragStandard;
            soundDragChanged = SoundDefOf.Designate_DragStandard_Changed;
            useMouseIcon = true;
            soundSucceeded = SoundDefOf.Designate_Claim;
            hotKey = KeyBindingDefOf.Misc12;
            tutorTag = "CallSkyfallerInArea";
        }

        public override DrawStyleCategoryDef DrawStyleCategory => DrawStyleCategoryDefOf.FilledRectangle;

        public override AcceptanceReport CanDesignateCell(IntVec3 c)
        {
            if (!c.InBounds(Map))
                return false;
            
            // 允许在迷雾格上选择（就像拆除和索赔设计器一样）
            if (c.Fogged(Map))
                return false;
            
            // 只要单元格内有玩家建筑，就允许选择
            var things = Map.thingGrid.ThingsListAt(c);
            foreach (var thing in things)
            {
                if (thing.def.category == ThingCategory.Building &&
                    thing.Faction == Faction.OfPlayer &&
                    thing.TryGetComp<CompSkyfallerCaller>() != null)
                {
                    return true;
                }
            }
            
            // 即使单元格内没有符合条件的建筑，也允许选择（这样用户可以拖动区域）
            return true;
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
            
            // 处理所有选中的单元格
            foreach (var cell in cells)
            {
                if (cell.InBounds(Map))
                {
                    // 统计该单元格处理的建筑数量
                    int cellCount = processedBuildings.Count;
                    ProcessCell(cell);
                    int newBuildings = processedBuildings.Count - cellCount;
                    totalBuildings += newBuildings;
                }
            }
            
            // 计算成功和失败的数量
            // 这里需要跟踪每个建筑的调用结果
            // 由于我们直接调用CallSkyfaller，需要知道哪些失败了
            // 简化处理：在ProcessCell中统计
            
            // 显示简单的结果消息
            if (totalBuildings > 0)
            {
                Messages.Message("WULA_AreaCallInitiated".Translate(totalBuildings), 
                    MessageTypeDefOf.PositiveEvent);
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
                
                // 获取空投组件
                var comp = thing.TryGetComp<CompSkyfallerCaller>();
                if (comp == null)
                    continue;
                
                // 尝试呼叫空投
                if (comp.CanCallSkyfaller)
                {
                    comp.CallSkyfaller(false);
                    processedBuildings.Add(thing);
                }
                // 即使不能呼叫，也添加到已处理列表，避免重复尝试
                else
                {
                    processedBuildings.Add(thing);
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
            
            var comp = t.TryGetComp<CompSkyfallerCaller>();
            if (comp == null)
                return false;
            
            if (!comp.CanCallSkyfaller)
                return GetFailureReason(t, comp);
            
            return true;
        }

        private string GetFailureReason(Thing building, CompSkyfallerCaller comp)
        {
            if (!comp.HasRequiredFlyOver && comp.Props.requireFlyOver)
                return "WULA_NoBuildingDropperFlyOver".Translate();
            
            if (!comp.CheckRoofConditions)
            {
                var roof = building.Position.GetRoof(building.Map);
                if (roof?.isThickRoof == true)
                    return "WULA_ThickRoofBlocking".Translate();
                else
                    return "WULA_RoofBlocking".Translate();
            }
            
            if (!comp.HasEnoughMaterials())
                return "WULA_InsufficientMaterials".Translate();
            
            if (comp.used)
                return "WULA_AlreadyUsed".Translate();
            
            if (comp.calling)
                return "WULA_AlreadyCalling".Translate();
            
            return "WULA_CannotCallSkyfaller".Translate();
        }

        public override void DesignateThing(Thing t)
        {
            // 用于反向设计器（右键菜单）
            var comp = t.TryGetComp<CompSkyfallerCaller>();
            if (comp != null && comp.CanCallSkyfaller)
            {
                comp.CallSkyfaller(false);
            }
        }

        public override void SelectedUpdate()
        {
            // 参考Designator_Deconstruct，只绘制鼠标悬停方框
            GenUI.RenderMouseoverBracket();
        }
    }

    
}
