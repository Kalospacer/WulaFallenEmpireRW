using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace WulaFallenEmpire
{
    public class CompMapTeleporter : ThingComp
    {
        public CompProperties_MapTeleporter Props => (CompProperties_MapTeleporter)props;

        private bool isWarmingUp = false;
        private int warmupTicksLeft = 0;
        private GlobalTargetInfo target;
        private WULA_TeleportLandingMarker activeMarker;

        // Group caching
        private List<CompMapTeleporter> cachedGroupMembers;
        private int lastGroupCheckTick = -1;
        
        // Cells caching
        private List<IntVec3> cachedGroupCells;
        private int lastGroupCellsCheckTick = -1;

        public CellRect TeleportRect => CellRect.CenteredOn(parent.Position, Props.areaSize.x, Props.areaSize.z);
        
        private List<CompMapTeleporter> GroupMembers
        {
            get
            {
                if (lastGroupCheckTick == Find.TickManager.TicksGame && cachedGroupMembers != null)
                {
                    return cachedGroupMembers;
                }

                lastGroupCheckTick = Find.TickManager.TicksGame;
                cachedGroupMembers = new List<CompMapTeleporter>();
                var openSet = new Queue<CompMapTeleporter>();
                var closedSet = new HashSet<CompMapTeleporter>();

                openSet.Enqueue(this);
                closedSet.Add(this);

                while (openSet.Count > 0)
                {
                    var currentComp = openSet.Dequeue();
                    cachedGroupMembers.Add(currentComp);

                    var potentialNeighbors = parent.Map.listerThings.ThingsOfDef(parent.def);
                    foreach (var potentialNeighbor in potentialNeighbors)
                    {
                        var neighborComp = potentialNeighbor.TryGetComp<CompMapTeleporter>();
                        if (neighborComp == null || closedSet.Contains(neighborComp)) continue;

                        if (currentComp.TeleportRect.ExpandedBy(1).Overlaps(neighborComp.TeleportRect))
                        {
                            closedSet.Add(neighborComp);
                            openSet.Enqueue(neighborComp);
                        }
                    }
                }
                // Sort by ID to ensure consistent leader
                cachedGroupMembers.SortBy(c => c.parent.thingIDNumber);
                return cachedGroupMembers;
            }
        }

        public List<IntVec3> GroupCells
        {
            get
            {
                if (lastGroupCellsCheckTick == Find.TickManager.TicksGame && cachedGroupCells != null)
                {
                    return cachedGroupCells;
                }

                lastGroupCellsCheckTick = Find.TickManager.TicksGame;
                HashSet<IntVec3> cells = new HashSet<IntVec3>();
                foreach (var member in GroupMembers)
                {
                    foreach (var cell in member.TeleportRect)
                    {
                        if (cell.InBounds(parent.Map))
                        {
                            cells.Add(cell);
                        }
                    }
                }
                cachedGroupCells = cells.ToList();
                return cachedGroupCells;
            }
        }

        public List<IntVec3> GetRelativeGroupCells()
        {
            var cells = GroupCells;
            var center = parent.Position;
            return cells.Select(c => c - center).ToList();
        }

        private CompMapTeleporter Leader
        {
            get
            {
                var members = GroupMembers;
                if (members.Count == 0) return this;
                return members[0];
            }
        }
        
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref isWarmingUp, "isWarmingUp", false);
            Scribe_Values.Look(ref warmupTicksLeft, "warmupTicksLeft", 0);
            Scribe_TargetInfo.Look(ref target, "target");
            Scribe_References.Look(ref activeMarker, "activeMarker");
        }

        public override void PostDrawExtraSelectionOverlays()
        {
            base.PostDrawExtraSelectionOverlays();
            
            // Draw the combined field edges
            GenDraw.DrawFieldEdges(GroupCells, Color.cyan);

            var leader = Leader;
            if (leader != null)
            {
                // Mark the leader clearly
                Vector3 center = leader.parent.TrueCenter();
                GenDraw.DrawLineBetween(center + new Vector3(-1f, 0, -1f), center + new Vector3(1f, 0, 1f), SimpleColor.Yellow);
                GenDraw.DrawLineBetween(center + new Vector3(-1f, 0, 1f), center + new Vector3(1f, 0, -1f), SimpleColor.Yellow);
                GenDraw.DrawCircleOutline(center, 1.5f, SimpleColor.Yellow);

                // Draw lines from members to leader
                foreach (var member in GroupMembers)
                {
                    if (member != leader)
                    {
                        GenDraw.DrawLineBetween(leader.parent.TrueCenter(), member.parent.TrueCenter(), SimpleColor.Cyan);
                    }
                }
            }
        }

        public override void CompTick()
        {
            base.CompTick();
            if (Leader == this && isWarmingUp)
            {
                warmupTicksLeft--;
                if (warmupTicksLeft % 60 == 0)
                {
                    Log.Message($"[WULA] Teleport warmup: {warmupTicksLeft} ticks left.");
                    foreach (var member in GroupMembers)
                    {
                        Props.warmupEffecter?.Spawn(member.parent, member.parent.Map).Cleanup();
                    }
                }

                if (warmupTicksLeft <= 0)
                {
                    Log.Message("[WULA] Warmup finished. Attempting teleport...");
                    TryTeleport();
                    isWarmingUp = false;
                }
            }
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (var gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }

            if (parent.Faction != Faction.OfPlayer)
                yield break;

            // Only the leader provides the gizmos
            if (Leader != this)
                yield break;

            if (isWarmingUp)
            {
                yield return new Command_Action
                {
                    defaultLabel = "WULA_CancelTeleport".Translate(),
                    defaultDesc = "WULA_CancelTeleportDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("UI/Designators/Cancel"),
                    action = CancelTeleport
                };
            }
            else if (activeMarker != null && !activeMarker.Destroyed)
            {
                yield return new Command_Action
                {
                    defaultLabel = "WULA_CancelTeleport".Translate(),
                    defaultDesc = "WULA_CancelTeleportDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("UI/Designators/Cancel"),
                    action = CancelTeleport
                };
            }
            else
            {
                string reason = GetDisabledReason();
                Command_Action teleportCmd = new Command_Action
                {
                    defaultLabel = "WULA_InitiateTeleport".Translate(),
                    defaultDesc = GetDescription(),
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/LaunchShip"),
                    action = StartTargeting,
                    disabledReason = reason
                };
                
                if (!string.IsNullOrEmpty(reason))
                {
                    teleportCmd.Disable(reason);
                }
                
                yield return teleportCmd;
            }
        }

        private string GetDisabledReason()
        {
            if (Props.requiredResearch != null && !Props.requiredResearch.IsFinished)
            {
                return "WULA_MissingResearch".Translate(Props.requiredResearch.label);
            }
            
            return null;
        }

        private string GetDescription()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("WULA_InitiateTeleportDesc".Translate());
            
            if (Props.requiredResearch != null)
            {
                sb.AppendLine().Append("WULA_RequiresResearch".Translate(Props.requiredResearch.label));
            }
            
            return sb.ToString();
        }

        private void StartTargeting()
        {
            CameraJumper.TryJump(CameraJumper.GetWorldTarget(parent));
            Find.WorldSelector.ClearSelection();
            Find.WorldTargeter.BeginTargeting(ChoseWorldTarget, true, null, true, null, null);
        }

        private bool ChoseWorldTarget(GlobalTargetInfo targetInfo)
        {
            if (!targetInfo.IsValid) return false;
            
            this.target = targetInfo;
            
            MapParent mapParent = Find.WorldObjects.MapParentAt(targetInfo.Tile);
            
            if (mapParent == null)
            {
                SettleUtility.AddNewHome(targetInfo.Tile, Faction.OfPlayer);
                mapParent = Find.WorldObjects.MapParentAt(targetInfo.Tile);
            }

            if (mapParent != null)
            {
                if (!mapParent.HasMap)
                {
                    IntVec3 mapSize = Find.World.info.initialMapSize;
                    GetOrGenerateMapUtility.GetOrGenerateMap(targetInfo.Tile, mapSize, null);
                }

                if (mapParent.HasMap)
                {
                    CameraJumper.TryJump(mapParent.Map.Center, mapParent.Map);
                    
                    if (activeMarker != null && !activeMarker.Destroyed)
                    {
                        activeMarker.Destroy();
                    }

                    activeMarker = (WULA_TeleportLandingMarker)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("WULA_TeleportLandingMarker"));
                    activeMarker.sourceThing = parent;
                    GenSpawn.Spawn(activeMarker, mapParent.Map.Center, mapParent.Map);
                    
                    Find.Selector.ClearSelection();
                    Find.Selector.Select(activeMarker);
                    Find.DesignatorManager.Select(new Designator_TeleportArrival(this, mapParent.Map, activeMarker));
                    
                    return true;
                }
            }
            
            Messages.Message("WULA_TeleportFailed_MapGeneration".Translate(), MessageTypeDefOf.RejectInput);
            return false;
        }

        public void ConfirmArrival(IntVec3 cell, Map map)
        {
            this.target = new GlobalTargetInfo(cell, map);
            StartWarmup();
        }

        private void StartWarmup()
        {
            isWarmingUp = true;
            warmupTicksLeft = Props.warmupTicks;
            Props.warmupSound?.PlayOneShot(parent);
            Messages.Message("WULA_TeleportWarmupStarted".Translate(), parent, MessageTypeDefOf.NeutralEvent);
        }

        private void CancelTeleport()
        {
            isWarmingUp = false;
            warmupTicksLeft = 0;
            
            if (activeMarker != null && !activeMarker.Destroyed)
            {
                activeMarker.Destroy();
                activeMarker = null;
            }
            
            Messages.Message("WULA_TeleportCancelled".Translate(), parent, MessageTypeDefOf.NeutralEvent);
        }

        private void TryTeleport()
        {
            Log.Message($"[WULA] TryTeleport called. Target valid: {target.IsValid}, Tile: {target.Tile}, Cell: {target.Cell}");

            if (!target.IsValid)
            {
                Messages.Message("WULA_TeleportFailed_InvalidTarget".Translate(), parent, MessageTypeDefOf.RejectInput);
                CancelTeleport();
                return;
            }

            Map targetMap = target.Map;
            IntVec3 targetCell = target.Cell;

            if (targetMap == null)
            {
                Log.Message($"[WULA] Target map is null. Generating map for tile {target.Tile}...");
                targetMap = GetOrGenerateTargetMap(target.Tile);
                if (targetMap == null)
                {
                    Messages.Message("WULA_TeleportFailed_NoMap".Translate(), parent, MessageTypeDefOf.RejectInput);
                    CancelTeleport();
                    return;
                }
                targetCell = targetMap.Center;
            }

            Log.Message($"[WULA] Teleporting to map {targetMap.Index}, cell {targetCell}");
            TeleportContents(targetMap, targetCell);
        }

        private Map GetOrGenerateTargetMap(int tile)
        {
            MapParent mapParent = Find.WorldObjects.MapParentAt(tile);

            if (mapParent == null)
            {
                SettleUtility.AddNewHome(tile, Faction.OfPlayer);
                mapParent = Find.WorldObjects.MapParentAt(tile);
            }

            if (mapParent != null)
            {
                if (!mapParent.HasMap)
                {
                    IntVec3 mapSize = Find.World.info.initialMapSize;
                    return GetOrGenerateMapUtility.GetOrGenerateMap(tile, mapSize, null);
                }
                return mapParent.Map;
            }

            return null;
        }

        private struct ThingToTeleport
        {
            public Thing thing;
            public IntVec3 relativePos;
        }

        private void TeleportContents(Map targetMap, IntVec3 targetCenter)
        {
            Map sourceMap = parent.Map;
            List<IntVec3> cells = GroupCells;
            IntVec3 center = parent.Position;
            
            List<ThingToTeleport> thingsToTeleport = new List<ThingToTeleport>();
            List<Pair<IntVec3, TerrainDef>> terrainToTeleport = new List<Pair<IntVec3, TerrainDef>>();

            Log.Message($"[WULA] Collecting data from {cells.Count} cells in group");

            // 1. 收集数据
            HashSet<Thing> collectedThings = new HashSet<Thing>();
            foreach (IntVec3 cell in cells)
            {
                if (!cell.InBounds(sourceMap)) continue;

                terrainToTeleport.Add(new Pair<IntVec3, TerrainDef>(cell - center, cell.GetTerrain(sourceMap)));

                List<Thing> thingList = sourceMap.thingGrid.ThingsListAt(cell);
                for (int i = thingList.Count - 1; i >= 0; i--)
                {
                    Thing t = thingList[i];
                    if (!collectedThings.Contains(t) &&
                        (t.def.category == ThingCategory.Item || 
                         t.def.category == ThingCategory.Pawn || 
                         t.def.category == ThingCategory.Building))
                    {
                        if (!t.def.destroyable) continue;
                        
                        collectedThings.Add(t);
                        thingsToTeleport.Add(new ThingToTeleport { thing = t, relativePos = t.Position - center });
                    }
                }
            }
            
            // 2. 准备传送 (PreSwapMap)
            foreach (var data in thingsToTeleport) data.thing.PreSwapMap();

            // 3. 从源地图移除 (DeSpawn)
            foreach (var data in thingsToTeleport)
            {
                if (data.thing.Spawned) data.thing.DeSpawn(DestroyMode.WillReplace);
            }

            // 4. 修改地形
            foreach (var pair in terrainToTeleport)
            {
                IntVec3 newPos = targetCenter + pair.First;
                newPos = newPos.ClampInsideMap(targetMap);

                List<Thing> targetThings = targetMap.thingGrid.ThingsListAt(newPos);
                for (int i = targetThings.Count - 1; i >= 0; i--)
                {
                    if (targetThings[i].def.destroyable) targetThings[i].Destroy();
                }

                if (pair.Second != null)
                {
                    targetMap.terrainGrid.SetTerrain(newPos, pair.Second);
                    sourceMap.terrainGrid.SetTerrain(center + pair.First, TerrainDefOf.Soil);
                }
            }

            // 5. 放置到新地图 (Spawn)
            foreach (var data in thingsToTeleport)
            {
                if (data.thing.Destroyed) continue;
                IntVec3 newPos = targetCenter + data.relativePos;
                newPos = newPos.ClampInsideMap(targetMap);
                GenSpawn.Spawn(data.thing, newPos, targetMap, data.thing.Rotation);
            }

            // 6. 传送后处理 (PostSwapMap)
            foreach (var data in thingsToTeleport)
            {
                if (!data.thing.Destroyed) data.thing.PostSwapMap();
            }

            // 7. 完成
            CameraJumper.TryJump(targetCenter, targetMap);
            Props.teleportSound?.PlayOneShot(new TargetInfo(targetCenter, targetMap, false));
            Messages.Message("WULA_TeleportSuccessful".Translate(), new TargetInfo(targetCenter, targetMap, false), MessageTypeDefOf.PositiveEvent);
        }
    }
}