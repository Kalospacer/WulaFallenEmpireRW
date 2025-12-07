using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    public class Designator_TeleportArrival : Designator
    {
        private CompMapTeleporter teleporter;
        private Map targetMap;

        public override string Label => "WULA_SelectArrivalPoint".Translate();
        public override string Desc => "WULA_SelectArrivalPointDesc".Translate();

        public Designator_TeleportArrival(CompMapTeleporter teleporter, Map targetMap)
        {
            this.teleporter = teleporter;
            this.targetMap = targetMap;
            this.useMouseIcon = true;
            this.soundDragSustain = SoundDefOf.Designate_DragStandard;
            this.soundDragChanged = SoundDefOf.Designate_DragStandard_Changed;
            this.soundSucceeded = SoundDefOf.Designate_PlaceBuilding;
        }

        public override AcceptanceReport CanDesignateCell(IntVec3 loc)
        {
            if (!loc.InBounds(targetMap)) return false;
            
            // 检查半径区域是否有效
            float radius = teleporter.Props.radius;
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(loc, radius, true))
            {
                if (!cell.InBounds(targetMap)) return "WULA_OutOfBounds".Translate();
                
                // 检查地图边缘
                if (cell.InNoBuildEdgeArea(targetMap))
                {
                    return "WULA_InNoBuildArea".Translate();
                }
                
                // 检查迷雾
                if (cell.Fogged(targetMap))
                {
                    return "WULA_BlockedByFog".Translate();
                }
                
                // 检查是否有不可覆盖的建筑
                List<Thing> things = targetMap.thingGrid.ThingsListAt(cell);
                foreach (Thing t in things)
                {
                    if (t.def.category == ThingCategory.Building)
                    {
                        if (!t.def.destroyable)
                        {
                            return "WULA_BlockedByIndestructible".Translate(t.Label);
                        }
                    }
                }
                
                // 检查地形是否支持建造
                TerrainDef terrain = cell.GetTerrain(targetMap);
                if (terrain.passability == Traversability.Impassable && !terrain.IsWater)
                {
                     return "WULA_TerrainImpassable".Translate();
                }
            }

            return true;
        }

        public override void DesignateSingleCell(IntVec3 c)
        {
            teleporter.ConfirmArrival(c, targetMap);
            Find.DesignatorManager.Deselect();
        }

        public override void Selected()
        {
            GenDraw.DrawRadiusRing(UI.MouseCell(), teleporter.Props.radius);
        }
        
        public override void DrawMouseAttachments()
        {
            base.DrawMouseAttachments();
            GenDraw.DrawRadiusRing(UI.MouseCell(), teleporter.Props.radius);
        }
    }
}