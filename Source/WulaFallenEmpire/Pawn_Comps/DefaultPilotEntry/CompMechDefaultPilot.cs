// CompMechDefaultPilot.cs
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    public class CompMechDefaultPilot : ThingComp
    {
        public CompProperties_MechDefaultPilot Props => (CompProperties_MechDefaultPilot)props;
        private bool defaultPilotsSpawned = false;
        
        public override void Initialize(CompProperties props)
        {
            base.Initialize(props);
        }
        
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            
            if (respawningAfterLoad)
                return;
                
            // 检查是否需要生成默认驾驶员
            TrySpawnDefaultPilots();
        }
        
        // 尝试生成默认驾驶员
        public void TrySpawnDefaultPilots()
        {
            if (defaultPilotsSpawned)
                return;
                
            var mech = parent as Pawn;
            if (mech == null)
                return;
                
            var mechFaction = mech.Faction;
            if (mechFaction == null)
                return;
                
            // 检查阵营条件
            bool isPlayerFaction = mechFaction == Faction.OfPlayer;
            if (isPlayerFaction && !Props.enableForPlayerFaction)
            {
                return;
            }
            
            if (!isPlayerFaction && !Props.enableForNonPlayerFaction)
            {
                return;
            }
            
            // 检查生成几率
            if (Rand.Value > Props.defaultPilotChance)
            {
                return;
            }
            
            // 获取驾驶员容器
            var pilotHolder = mech.TryGetComp<CompMechPilotHolder>();
            if (pilotHolder == null)
            {
                return;
            }
            
            // 检查是否需要生成
            if (Props.spawnOnlyIfNoPilot && pilotHolder.HasPilots)
            {
                return;
            }
            
            // 如果需要替换现有驾驶员，先移除所有
            if (Props.replaceExistingPilots && pilotHolder.HasPilots)
            {
                pilotHolder.RemoveAllPilots();
            }
            
            // 计算要生成的驾驶员数量
            int maxPilots = Props.maxDefaultPilots > 0 ? 
                Props.maxDefaultPilots : pilotHolder.Props.maxPilots;
            int pilotsToSpawn = maxPilots - pilotHolder.CurrentPilotCount;
            
            if (pilotsToSpawn <= 0)
            {
                return;
            }
            
            // 生成驾驶员
            int spawnedCount = 0;
            for (int i = 0; i < pilotsToSpawn; i++)
            {
                if (TrySpawnDefaultPilot(mech, pilotHolder))
                    spawnedCount++;
            }
            
            if (spawnedCount > 0)
            {
                defaultPilotsSpawned = true;
            }
        }
        
        // 尝试生成单个默认驾驶员
        private bool TrySpawnDefaultPilot(Pawn mech, CompMechPilotHolder pilotHolder)
        {
            // 选择驾驶员类型
            var pilotKind = Props.SelectRandomPilotKind();
            if (pilotKind == null)
            {
                Log.Warning($"[DD] No valid pilot kind found");
                return false;
            }
            
            // 创建驾驶员生成请求
            PawnGenerationRequest request = new PawnGenerationRequest(
                kind: pilotKind,
                faction: mech.Faction,
                context: PawnGenerationContext.NonPlayer,
                tile: mech.Map?.Tile ?? -1,
                forceGenerateNewPawn: true,
                allowDead: false,
                allowDowned: false,
                canGeneratePawnRelations: false,
                mustBeCapableOfViolence: false,
                colonistRelationChanceFactor: 0f,
                forceAddFreeWarmLayerIfNeeded: false,
                allowGay: true,
                allowFood: true,
                allowAddictions: true
            );
            
            try
            {
                // 生成驾驶员
                Pawn pilot = PawnGenerator.GeneratePawn(request);
                
                // 设置驾驶员名字
                if (pilot.Name == null || pilot.Name is NameSingle)
                {
                    pilot.Name = PawnBioAndNameGenerator.GeneratePawnName(pilot, NameStyle.Numeric);
                }
                
                // 添加到机甲
                if (pilotHolder.CanAddPilot(pilot))
                {
                    pilotHolder.AddPilot(pilot);
                    
                    return true;
                }
                else
                {
                    Log.Warning($"[DD] Cannot add pilot {pilot.LabelShortCap} to mech");
                    // 清理生成的pawn
                    pilot.Destroy();
                    return false;
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"[DD] Error generating default pilot: {ex}");
                return false;
            }
        }
        
        // 开发者命令：强制生成默认驾驶员
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }
            
            if (DebugSettings.ShowDevGizmos)
            {
                var mech = parent as Pawn;
                if (mech != null && mech.Faction == Faction.OfPlayer)
                {
                    yield return new Command_Action
                    {
                        defaultLabel = "DEV: Spawn Default Pilots",
                        defaultDesc = "Force spawn default pilots for this mech",
                        action = () =>
                        {
                            defaultPilotsSpawned = false;
                            TrySpawnDefaultPilots();
                            Messages.Message("Default pilots spawned", parent, MessageTypeDefOf.NeutralEvent);
                        }
                    };
                    
                    yield return new Command_Action
                    {
                        defaultLabel = "DEV: Clear Default Pilots",
                        defaultDesc = "Remove default pilots and allow respawning",
                        action = () =>
                        {
                            defaultPilotsSpawned = false;
                            var pilotHolder = mech.TryGetComp<CompMechPilotHolder>();
                            if (pilotHolder != null)
                            {
                                pilotHolder.RemoveAllPilots();
                            }
                            Messages.Message("Default pilots cleared", parent, MessageTypeDefOf.NeutralEvent);
                        }
                    };
                }
            }
        }
        
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref defaultPilotsSpawned, "defaultPilotsSpawned", false);
        }
        
        // 当机甲阵营改变时重新检查
        public void Notify_FactionChanged()
        {
            // 重置标记，允许在新阵营下生成驾驶员
            defaultPilotsSpawned = false;
            TrySpawnDefaultPilots();
        }
        
        // 当驾驶员被移除时，如果全部移除，允许重新生成
        public void Notify_PilotRemoved()
        {
            var pilotHolder = parent.TryGetComp<CompMechPilotHolder>();
            if (pilotHolder != null && !pilotHolder.HasPilots)
            {
                // 如果所有驾驶员都被移除了，重置标记
                defaultPilotsSpawned = false;
            }
        }
    }
}
