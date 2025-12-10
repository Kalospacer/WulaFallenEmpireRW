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
        private List<IntVec3> relativeCells;

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
            
            // Cache relative cells from the group
            this.relativeCells = teleporter.GetRelativeGroupCells();
        }

        public override AcceptanceReport CanDesignateCell(IntVec3 loc)
        {
            if (!loc.InBounds(targetMap)) return false;
            
            // Check all cells in the group shape
            foreach (IntVec3 offset in relativeCells)
            {
                IntVec3 cell = loc + offset;
                
                if (!cell.InBounds(targetMap)) return "WULA_OutOfBounds".Translate();
                
                // Check map edge
                if (cell.InNoBuildEdgeArea(targetMap))
                {
                    return "WULA_InNoBuildArea".Translate();
                }
                
                // Check fog
                if (cell.Fogged(targetMap))
                {
                    return "WULA_BlockedByFog".Translate();
                }
                
                // Check for indestructible buildings
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
                
                // Check terrain passability
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
            IntVec3 center = UI.MouseCell();
            List<IntVec3> drawCells = new List<IntVec3>();
            foreach (var offset in relativeCells)
            {
                drawCells.Add(center + offset);
            }
            GenDraw.DrawFieldEdges(drawCells);
        }

        private void CacheThings()
        {
            thingsToTeleport.Clear();
            if (teleporter.parent == null || teleporter.parent.Map == null) return;

            sourceCenter = teleporter.parent.Position;
            Map sourceMap = teleporter.parent.Map;
            
            // Use the group cells directly from the teleporter
            List<IntVec3> groupCells = teleporter.GroupCells;
            
            foreach (IntVec3 cell in groupCells)
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
            // Add self (leader)
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
                        // Ignore drawing errors to prevent UI crash
                    }
                }
            }
        }
    }
}