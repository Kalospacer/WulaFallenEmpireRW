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
        
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref isWarmingUp, "isWarmingUp", false);
            Scribe_Values.Look(ref warmupTicksLeft, "warmupTicksLeft", 0);
            Scribe_TargetInfo.Look(ref target, "target");
        }

        public override void CompTick()
        {
            base.CompTick();
            if (isWarmingUp)
            {
                warmupTicksLeft--;
                if (warmupTicksLeft <= 0)
                {
                    TryTeleport();
                    isWarmingUp = false;
                }
                else if (warmupTicksLeft % 60 == 0)
                {
                    Props.warmupEffecter?.Spawn(parent, parent.Map).Cleanup();
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
            if (mapParent != null && mapParent.HasMap)
            {
                CameraJumper.TryJump(mapParent.Map.Center, mapParent.Map);
                Find.DesignatorManager.Select(new Designator_TeleportArrival(this, mapParent.Map));
                return true;
            }
            
            StartWarmup();
            return true;
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
            Messages.Message("WULA_TeleportCancelled".Translate(), parent, MessageTypeDefOf.NeutralEvent);
        }

        private void TryTeleport()
        {
            if (!target.IsValid)
            {
                CancelTeleport();
                return;
            }

            Map targetMap = target.Map;
            IntVec3 targetCell = target.Cell;

            if (targetMap == null)
            {
                targetMap = GetOrGenerateTargetMap(target.Tile);
                if (targetMap == null)
                {
                    Log.Error("Failed to get or generate target map.");
                    CancelTeleport();
                    return;
                }
                targetCell = targetMap.Center;
            }

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

        private struct CellData
        {
            public IntVec3 relativePos;
            public TerrainDef terrain;
            public List<Thing> things;
        }

        private void TeleportContents(Map targetMap, IntVec3 targetCenter)
        {
            IEnumerable<IntVec3> cells = GenRadial.RadialCellsAround(parent.Position, Props.radius, true);
            List<CellData> dataToTeleport = new List<CellData>();
            IntVec3 center = parent.Position;

            // 1. 收集数据
            foreach (IntVec3 cell in cells)
            {
                if (!cell.InBounds(parent.Map)) continue;

                CellData data = new CellData
                {
                    relativePos = cell - center,
                    terrain = cell.GetTerrain(parent.Map),
                    things = new List<Thing>()
                };

                List<Thing> thingList = parent.Map.thingGrid.ThingsListAt(cell);
                for (int i = thingList.Count - 1; i >= 0; i--)
                {
                    Thing t = thingList[i];
                    if (t.def.category == ThingCategory.Item || 
                        t.def.category == ThingCategory.Pawn || 
                        t.def.category == ThingCategory.Building)
                    {
                        if (t != parent && !t.def.destroyable) continue;
                        if (t != parent) data.things.Add(t);
                    }
                }
                dataToTeleport.Add(data);
            }
            
            // 2. 执行传送
            foreach (CellData data in dataToTeleport)
            {
                IntVec3 newPos = targetCenter + data.relativePos;
                newPos = newPos.ClampInsideMap(targetMap);

                // 2.1 传送地形
                if (data.terrain != null)
                {
                    List<Thing> targetThings = targetMap.thingGrid.ThingsListAt(newPos);
                    for (int i = targetThings.Count - 1; i >= 0; i--)
                    {
                        if (targetThings[i].def.destroyable) targetThings[i].Destroy();
                    }

                    targetMap.terrainGrid.SetTerrain(newPos, data.terrain);
                    parent.Map.terrainGrid.SetTerrain(center + data.relativePos, TerrainDefOf.Soil);
                }

                // 2.2 传送物体
                foreach (Thing t in data.things)
                {
                    if (t.Destroyed) continue;

                    if (t.Spawned) t.DeSpawn();
                    GenSpawn.Spawn(t, newPos, targetMap, t.Rotation);
                    
                    if (t is Pawn p)
                    {
                        p.jobs.StopAll();
                    }
                }
            }

            // 3. 传送自身
            if (parent.Spawned) parent.DeSpawn();
            GenSpawn.Spawn(parent, targetCenter, targetMap, parent.Rotation);

            // 4. 完成
            CameraJumper.TryJump(targetCenter, targetMap);
            Props.teleportSound?.PlayOneShot(new TargetInfo(targetCenter, targetMap, false));
            Messages.Message("WULA_TeleportSuccessful".Translate(), new TargetInfo(targetCenter, targetMap, false), MessageTypeDefOf.PositiveEvent);
        }
    }
}