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
            GenDraw.DrawFieldEdges(CellRect.CenteredOn(parent.Position, Props.areaSize.x, Props.areaSize.z).Cells.ToList());
        }

        public override void CompTick()
        {
            base.CompTick();
            if (isWarmingUp)
            {
                warmupTicksLeft--;
                if (warmupTicksLeft % 60 == 0)
                {
                    Log.Message($"[WULA] Teleport warmup: {warmupTicksLeft} ticks left.");
                    Props.warmupEffecter?.Spawn(parent, parent.Map).Cleanup();
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

        private void TeleportContents(Map targetMap, IntVec3 targetCenter)
        {
            CellRect rect = CellRect.CenteredOn(parent.Position, Props.areaSize.x, Props.areaSize.z);
            IntVec3 center = parent.Position;
            
            List<Thing> thingsToTeleport = new List<Thing>();
            List<Pair<IntVec3, TerrainDef>> terrainToTeleport = new List<Pair<IntVec3, TerrainDef>>();

            Log.Message($"[WULA] Collecting data from {rect.Area} cells around {center} with size {Props.areaSize}");

            // 1. 收集数据
            HashSet<Thing> collectedThings = new HashSet<Thing>();
            foreach (IntVec3 cell in rect)
            {
                if (!cell.InBounds(parent.Map)) continue;

                terrainToTeleport.Add(new Pair<IntVec3, TerrainDef>(cell - center, cell.GetTerrain(parent.Map)));

                List<Thing> thingList = parent.Map.thingGrid.ThingsListAt(cell);
                for (int i = thingList.Count - 1; i >= 0; i--)
                {
                    Thing t = thingList[i];
                    if (t != parent && !collectedThings.Contains(t) &&
                        (t.def.category == ThingCategory.Item || 
                         t.def.category == ThingCategory.Pawn || 
                         t.def.category == ThingCategory.Building))
                    {
                        if (!t.def.destroyable) continue;
                        
                        collectedThings.Add(t);
                        thingsToTeleport.Add(t);
                    }
                }
            }
            
            // 2. 准备传送 (PreSwapMap)
            foreach (Thing t in thingsToTeleport) t.PreSwapMap();
            parent.PreSwapMap();

            // 3. 从源地图移除 (DeSpawn)
            foreach (Thing t in thingsToTeleport)
            {
                if (t.Spawned) t.DeSpawn(DestroyMode.WillReplace);
            }
            if (parent.Spawned) parent.DeSpawn(DestroyMode.WillReplace);

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
                    parent.Map.terrainGrid.SetTerrain(center + pair.First, TerrainDefOf.Soil);
                }
            }

            // 5. 放置到新地图 (Spawn)
            foreach (Thing t in thingsToTeleport)
            {
                if (t.Destroyed) continue;
                IntVec3 relativePos = t.Position - center;
                IntVec3 newPos = targetCenter + relativePos;
                newPos = newPos.ClampInsideMap(targetMap);
                GenSpawn.Spawn(t, newPos, targetMap, t.Rotation);
            }
            GenSpawn.Spawn(parent, targetCenter, targetMap, parent.Rotation);

            // 6. 传送后处理 (PostSwapMap)
            foreach (Thing t in thingsToTeleport)
            {
                if (!t.Destroyed) t.PostSwapMap();
            }
            parent.PostSwapMap();

            // 7. 完成
            CameraJumper.TryJump(targetCenter, targetMap);
            Props.teleportSound?.PlayOneShot(new TargetInfo(targetCenter, targetMap, false));
            Messages.Message("WULA_TeleportSuccessful".Translate(), new TargetInfo(targetCenter, targetMap, false), MessageTypeDefOf.PositiveEvent);
        }
    }
}