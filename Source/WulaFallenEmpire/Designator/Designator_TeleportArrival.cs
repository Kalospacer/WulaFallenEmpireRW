using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    public class Designator_TeleportArrival : Designator
    {
        private CompMapTeleporter teleporter;
        private Map targetMap;
        private WULA_TeleportLandingMarker marker;
        private List<Thing> thingsToTeleport = new List<Thing>();
        private IntVec3 sourceCenter;

        public override string Label => "WULA_SelectArrivalPoint".Translate();
        public override string Desc => "WULA_SelectArrivalPointDesc".Translate();

        public Designator_TeleportArrival(CompMapTeleporter teleporter, Map targetMap, WULA_TeleportLandingMarker marker = null)
        {
            this.teleporter = teleporter;
            this.targetMap = targetMap;
            this.marker = marker;
            this.useMouseIcon = true;
            this.soundDragSustain = SoundDefOf.Designate_DragStandard;
            this.soundDragChanged = SoundDefOf.Designate_DragStandard_Changed;
            this.soundSucceeded = SoundDefOf.Designate_PlaceBuilding;
        }

        public override AcceptanceReport CanDesignateCell(IntVec3 loc)
        {
            if (!loc.InBounds(targetMap)) return false;
            
            // 检查区域是否有效
            CellRect rect = CellRect.CenteredOn(loc, teleporter.Props.areaSize.x, teleporter.Props.areaSize.z);
            foreach (IntVec3 cell in rect)
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
            if (marker != null)
            {
                marker.Position = c;
                Find.DesignatorManager.Deselect();
                Find.Selector.ClearSelection();
                Find.Selector.Select(marker);
            }
            else
            {
                teleporter.ConfirmArrival(c, targetMap);
                Find.DesignatorManager.Deselect();
            }
        }

        public override void Selected()
        {
            CacheThings();
            DrawRect();
        }

        public override void SelectedUpdate()
        {
            DrawRect();
            DrawGhosts();
        }
        
        public override void DrawMouseAttachments()
        {
            base.DrawMouseAttachments();
            DrawRect();
        }

        private void DrawRect()
        {
            GenDraw.DrawFieldEdges(CellRect.CenteredOn(UI.MouseCell(), teleporter.Props.areaSize.x, teleporter.Props.areaSize.z).Cells.ToList());
        }

        private void CacheThings()
        {
            thingsToTeleport.Clear();
            if (teleporter.parent == null || teleporter.parent.Map == null) return;

            sourceCenter = teleporter.parent.Position;
            Map sourceMap = teleporter.parent.Map;
            CellRect rect = CellRect.CenteredOn(sourceCenter, teleporter.Props.areaSize.x, teleporter.Props.areaSize.z);
            
            foreach (IntVec3 cell in rect)
            {
                if (!cell.InBounds(sourceMap)) continue;
                foreach (Thing t in sourceMap.thingGrid.ThingsListAt(cell))
                {
                    if (t.def.category == ThingCategory.Building || t.def.category == ThingCategory.Item || t.def.category == ThingCategory.Pawn)
                    {
                        if (t != teleporter.parent) thingsToTeleport.Add(t);
                    }
                }
            }
            // 添加自身
            thingsToTeleport.Add(teleporter.parent);
        }

        private void DrawGhosts()
        {
            IntVec3 mouseCell = UI.MouseCell();
            if (!mouseCell.InBounds(targetMap)) return;

            foreach (Thing t in thingsToTeleport)
            {
                if (t == null || t.Destroyed) continue;
                if (t.Graphic == null) continue;
                
                IntVec3 relativePos = t.Position - sourceCenter;
                IntVec3 drawPos = mouseCell + relativePos;
                
                if (drawPos.InBounds(targetMap))
                {
                    try
                    {
                        GhostUtility.GhostGraphicFor(t.Graphic, t.def, Color.white).DrawFromDef(drawPos.ToVector3ShiftedWithAltitude(AltitudeLayer.Blueprint), t.Rotation, t.def);
                    }
                    catch
                    {
                        // 忽略绘制错误，防止UI崩溃
                    }
                }
            }
        }
    }
}