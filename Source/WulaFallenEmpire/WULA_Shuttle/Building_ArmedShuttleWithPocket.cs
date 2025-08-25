using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace WulaFallenEmpire
{
    [StaticConstructorOnStartup]
    public class Building_ArmedShuttleWithPocket : Building_ArmedShuttle, IThingHolder
    {
        #region 静态图标定义
        
        private static readonly Texture2D ViewPocketMapTex = ContentFinder<Texture2D>.Get("UI/Commands/ViewCave");
        private static readonly Texture2D EnterPocketMapTex = ContentFinder<Texture2D>.Get("UI/Commands/EnterCave");
        private static readonly Texture2D TeleportAndLoadTex = ContentFinder<Texture2D>.Get("UI/Commands/LoadTransporter");

        #endregion

        #region 口袋空间字段
        
        private Map pocketMap;
        private bool pocketMapGenerated;
        private IntVec2 pocketMapSize = new IntVec2(80, 80);
        private MapGeneratorDef mapGenerator;
        private ThingDef exitDef;
        public Building_PocketMapExit exit;
        
        private bool doTeleportAfterLoading = false;
        private bool wasLoading = false;

        #endregion

        #region 属性

        // We use the public properties from the base class: this.ShuttleComp and this.TransporterComp
        public Map PocketMap => pocketMap;
        public bool PocketMapExists
        {
            get
            {
                if (pocketMap != null && pocketMap.Parent?.HasMap == false)
                {
                    pocketMap = null;
                }
                return pocketMap != null;
            }
        }
        public bool PocketMapGenerated => pocketMapGenerated;

        #endregion

        #region 基础重写方法
        
        public override void ExposeData()
        {
            base.ExposeData();
            
            if (Scribe.mode == LoadSaveMode.Saving && pocketMap != null && pocketMap.Parent?.HasMap == false)
            {
                pocketMap = null;
            }
            
            Scribe_Deep.Look(ref pocketMap, "pocketMap");
            Scribe_Values.Look(ref pocketMapGenerated, "pocketMapGenerated", false);
            Scribe_Values.Look(ref pocketMapSize, "pocketMapSize", new IntVec2(80, 80));
            Scribe_Defs.Look(ref mapGenerator, "mapGenerator");
            Scribe_Defs.Look(ref exitDef, "exitDef");
            Scribe_References.Look(ref exit, "exit");
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            if (pocketMap != null && pocketMapGenerated)
            {
                try
                {
                    TransferAllFromPocketToMainMap();
                    PocketMapUtility.DestroyPocketMap(pocketMap);
                }
                catch (Exception ex)
                {
                    Log.Error($"[WULA] Error cleaning up pocket map on DeSpawn: {ex}");
                }
            }
            base.DeSpawn(mode);
        }
        
        protected override void Tick()
        {
            base.Tick();

            if (!Spawned) return;

            bool isLoading = this.TransporterComp.leftToLoad != null && this.TransporterComp.leftToLoad.Any(x => x.CountToTransfer > 0);
            if (wasLoading && !isLoading && doTeleportAfterLoading)
            {
                TeleportContentsToPocketDimension();
                doTeleportAfterLoading = false; 
            }
            wasLoading = isLoading;

            if (this.IsHashIntervalTick(2500) && pocketMapGenerated && exit != null)
            {
                UpdateExitPointTarget();
            }
        }

        public override string GetInspectString()
        {
            StringBuilder sb = new StringBuilder(base.GetInspectString());
            
            if (pocketMapGenerated)
            {
                sb.AppendLine("WULA.PocketSpace.Status".Translate() + ": " + "WULA.PocketSpace.Ready".Translate());
                if (pocketMap.mapPawns.AllPawnsSpawned.Any(p => p.IsColonist))
                {
                    int pawnCount = pocketMap.mapPawns.AllPawnsSpawned.Count(p => p.IsColonist);
                    sb.AppendLine("WULA.PocketSpace.PawnCount".Translate(pawnCount));
                }
            }
            else
            {
                sb.AppendLine("WULA.PocketSpace.Status".Translate() + ": " + "WULA.PocketSpace.NotGenerated".Translate());
            }
            
            return sb.ToString().TrimEndNewlines();
        }

        #endregion

        #region Gizmos

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (var baseGizmo in base.GetGizmos())
            {
                var command = baseGizmo as Command;
                if (command != null && (command.defaultLabel == "CommandLoadTransporter".Translate() || command.defaultLabel == "CommandLoadTransporter".Translate() + "..."))
                {
                    yield return CreateLoadGizmo(false);
                    if (PocketMapExists) // Only show teleport load if pocket map exists
                    {
                        yield return CreateLoadGizmo(true);
                    }
                }
                else
                {
                    yield return baseGizmo;
                }
            }
            
            if (pocketMapGenerated && PocketMapExists)
            {
                yield return new Command_Action
                {
                    defaultLabel = "WULA.ViewPocketSpace".Translate(),
                    defaultDesc = "WULA.ViewPocketSpaceDesc".Translate(),
                    icon = ViewPocketMapTex,
                    action = SwitchToPocketSpace
                };
            }
            else
            {
                yield return new Command_Action
                {
                    defaultLabel = "WULA.CreatePocketSpace".Translate(),
                    defaultDesc = "WULA.CreatePocketSpaceDesc".Translate(),
                    icon = EnterPocketMapTex,
                    action = CreatePocketMap
                };
            }
        }

        private Command_Action CreateLoadGizmo(bool teleport)
        {
            var command = new Command_Action();
            var originalLoadGizmo = this.TransporterComp.CompGetGizmosExtra().FirstOrDefault(g => g is Command && (((Command)g).defaultLabel == "CommandLoadTransporter".Translate() || ((Command)g).defaultLabel == "CommandLoadTransporter".Translate() + "...")) as Command;

            if (teleport)
            {
                command.defaultLabel = "WULA.LoadAndTeleport".Translate();
                command.defaultDesc = "WULA.LoadAndTeleportDesc".Translate();
                command.icon = TeleportAndLoadTex;
            }
            else
            {
                command.defaultLabel = "WULA.LoadIntoCargo".Translate();
                command.defaultDesc = "WULA.LoadIntoCargoDesc".Translate();
                command.icon = originalLoadGizmo?.icon ?? ContentFinder<Texture2D>.Get("UI/Commands/LoadTransporter");
            }

            if (originalLoadGizmo != null)
            {
                command.action = () =>
                {
                    doTeleportAfterLoading = teleport;
                    originalLoadGizmo.ProcessInput(null);
                };

                if (originalLoadGizmo.Disabled)
                {
                    command.Disable(originalLoadGizmo.disabledReason);
                }
            }
            else
            {
                command.Disable("Error: Could not find original load command.".Translate());
            }

            // This disabling logic is now redundant if we control visibility in GetGizmos,
            // but keeping it here for safety against direct calls.
            if (teleport && !PocketMapExists)
            {
                command.Disable("WULA.PocketSpace.NotGenerated".Translate());
            }

            return command;
        }

        #endregion

        #region 口袋空间核心方法
        
        public void TeleportContentsToPocketDimension()
        {
            if (!PocketMapExists || this.TransporterComp == null) return;

            var thingsToTeleport = this.TransporterComp.innerContainer.ToList();
            if (!thingsToTeleport.Any()) return;
            
            Log.Message($"[WULA] Teleporting {thingsToTeleport.Count} things to pocket dimension.");

            IntVec3 spawnCenter = exit?.Position ?? pocketMap.Center;
            
            this.TransporterComp.innerContainer.TryDropAll(spawnCenter, pocketMap, ThingPlaceMode.Near);
            
            Messages.Message("WULA.TeleportComplete".Translate(thingsToTeleport.Count), this, MessageTypeDefOf.PositiveEvent);
        }

        public void EnterPocketSpace(IEnumerable<Pawn> pawns)
        {
            if (!PocketMapExists)
            {
                Messages.Message("WULA.PocketSpace.NotGenerated".Translate(), this, MessageTypeDefOf.RejectInput);
                return;
            }
            
            if (pawns == null || !pawns.Any())
            {
                return;
            }

            foreach (Pawn pawn in pawns.ToList())
            {
                if (pawn != null && pawn.Spawned)
                {
                    TransferPawnToPocketSpace(pawn);
                }
            }
            
            Messages.Message("WULA.PocketSpace.TransferSuccess".Translate(pawns.Count()), MessageTypeDefOf.PositiveEvent);
            Current.Game.CurrentMap = pocketMap;
        }
        
        public void SwitchToPocketSpace()
        {
            if (!PocketMapExists)
            {
                if (!pocketMapGenerated)
                {
                    CreatePocketMap();
                }
                
                if (!PocketMapExists)
                {
                    Messages.Message("WULA.PocketSpace.CreationFailed".Translate(), this, MessageTypeDefOf.RejectInput);
                    return;
                }
            }

            Current.Game.CurrentMap = pocketMap;
            Find.CameraDriver.JumpToCurrentMapLoc(pocketMap.Center);
        }

        private void CreatePocketMap()
        {
            try
            {
                PocketMapUtility.currentlyGeneratingPortal = null;
                pocketMap = GeneratePocketMapInt();
                PocketMapUtility.currentlyGeneratingPortal = null;
                
                if (pocketMap != null)
                {
                    pocketMapGenerated = true;
                    PlaceExitInPocketMap();
                    Log.Message($"[WULA] Pocket map created successfully with size {pocketMap.Size}");
                    Messages.Message("WULA.PocketSpace.CreationSuccess".Translate(), this, MessageTypeDefOf.PositiveEvent);
                }
                else
                {
                    Log.Error("[WULA] Failed to create pocket map");
                    Messages.Message("WULA.PocketSpace.CreationFailed".Translate(), this, MessageTypeDefOf.RejectInput);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[WULA] Error creating pocket map: {ex}");
                PocketMapUtility.currentlyGeneratingPortal = null;
            }
        }

        protected virtual Map GeneratePocketMapInt()
        {
            if (mapGenerator == null)
            {
                mapGenerator = DefDatabase<MapGeneratorDef>.GetNamed("WULA_PocketSpace_Small", false) 
                             ?? DefDatabase<MapGeneratorDef>.GetNamed("AncientStockpile", false) 
                             ?? MapGeneratorDefOf.Base_Player;
            }
            
            IntVec3 mapSize = new IntVec3(pocketMapSize.x, 1, pocketMapSize.z);
            return PocketMapUtility.GeneratePocketMap(mapSize, mapGenerator, GetExtraGenSteps(), this.Map);
        }
        
        protected virtual IEnumerable<GenStepWithParams> GetExtraGenSteps()
        {
            return Enumerable.Empty<GenStepWithParams>();
        }

        private void PlaceExitInPocketMap()
        {
            if (pocketMap == null || exitDef == null) return;

            try
            {
                IntVec3 exitPos = CellFinder.RandomClosewalkCellNear(pocketMap.Center, pocketMap, 5, 
                        p => p.Standable(pocketMap) && !p.GetThingList(pocketMap).Any(t => t.def.category == ThingCategory.Building));

                if (exitPos.IsValid)
                {
                    Thing exitBuilding = ThingMaker.MakeThing(exitDef);
                    if (exitBuilding is Building_PocketMapExit exitPortal)
                    {
                        exitPortal.targetMap = this.Map;
                        exitPortal.targetPos = this.Position;
                        exitPortal.parentShuttle = this;
                        exit = exitPortal;
                    }
                    
                    GenPlace.TryPlaceThing(exitBuilding, exitPos, pocketMap, ThingPlaceMode.Direct);
                }
                else
                {
                    Log.Warning("[WULA] Could not find valid position for exit point in pocket map");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[WULA] Error creating exit point: {ex}");
            }
        }

        private bool TransferPawnToPocketSpace(Pawn pawn)
        {
            if (pawn == null || !pawn.Spawned || pocketMap == null) return false;

            try
            {
                IntVec3 spawnPos = CellFinder.RandomClosewalkCellNear(pocketMap.Center, pocketMap, 10, 
                    p => p.Standable(pocketMap) && !p.GetThingList(pocketMap).Any(t => t is Pawn));

                if (spawnPos.IsValid)
                {
                    pawn.DeSpawn();
                    GenPlace.TryPlaceThing(pawn, spawnPos, pocketMap, ThingPlaceMode.Near);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[WULA] Error transferring pawn {pawn?.LabelShort} to pocket space: {ex}");
            }
            
            return false;
        }

        private void TransferAllFromPocketToMainMap()
        {
            if (pocketMap == null || !Spawned) return;

            try
            {
                List<Thing> thingsToTransfer = new List<Thing>(pocketMap.listerThings.AllThings);
                foreach (Thing thing in thingsToTransfer)
                {
                    if (thing.def.category != ThingCategory.Mote && thing.def.category != ThingCategory.Filth)
                    {
                         if(this.TransporterComp.innerContainer.TryAddOrTransfer(thing))
                         {
                             //Success
                         }
                         else
                         {
                             thing.Destroy();
                         }
                    }
                }
                
                Log.Message($"[WULA] Transferred {thingsToTransfer.Count} things from pocket space to shuttle cargo.");
            }
            catch (Exception ex)
            {
                Log.Error($"[WULA] Error transferring from pocket map: {ex}");
            }
        }

        #endregion
        
        #region IThingHolder

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            if (this.TransporterComp != null)
            {
                outChildren.Add(this.TransporterComp);
            }
        }

        public ThingOwner GetDirectlyHeldThings()
        {
            return null;
        }
        
        #endregion

        #region 穿梭机移动更新
        
        public void UpdateExitPointTarget()
        {
            if (exit != null && exit.Spawned && exit.targetPos != this.Position)
            {
                exit.targetPos = this.Position;
            }
        }
        
        #endregion

        #region 启动与生成

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            
            var props = def.GetModExtension<PocketMapProperties>();
            if (props != null)
            {
                pocketMapSize = props.pocketMapSize;
                mapGenerator = props.mapGenerator;
                exitDef = props.exitDef;
            }
            else
            {
                pocketMapSize = new IntVec2(50, 50);
                mapGenerator = MapGeneratorDefOf.Base_Player;
                exitDef = ThingDef.Named("WULA_PocketMapExit");
            }
        }
        
        #endregion
    }

    public class PocketMapProperties : DefModExtension
    {
        public IntVec2 pocketMapSize = new IntVec2(50, 50);
        public MapGeneratorDef mapGenerator;
        public ThingDef exitDef;
    }
}