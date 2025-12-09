using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;
using System.Linq;

namespace WulaFallenEmpire
{
    public class CompPhaseCombatTower : ThingComp
    {
        // 组件状态枚举
        public enum TowerState
        {
            Idle,           // 空闲（未激活）
            Warmup,         // 启动期
            Exploding,      // 爆炸阶段
            SpawningPawns,  // 生成Pawn阶段
            Finished        // 完成
        }
        
        private CompProperties_PhaseCombatTower Props => (CompProperties_PhaseCombatTower)props;
        
        // 状态变量
        private TowerState currentState = TowerState.Idle;
        private int ticksInCurrentState = 0;
        private int currentExplosionIndex = 0;
        private int pawnsSpawned = 0;
        private int nextSpawnTick = 0;
        
        // 缓存
        private List<PawnKindDef> cachedPawnKindDefs = null;
        
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            
            if (!respawningAfterLoad)
            {
                // 初次生成时开始启动期
                StartWarmup();
            }
        }
        
        // 开始启动期
        private void StartWarmup()
        {
            currentState = TowerState.Warmup;
            ticksInCurrentState = 0;
            currentExplosionIndex = 0;
            pawnsSpawned = 0;
        }
        
        // 开始爆炸阶段
        private void StartExplosionPhase()
        {
            currentState = TowerState.Exploding;
            ticksInCurrentState = 0;
            currentExplosionIndex = 0;
            
            if (Props.explosions.Count == 0)
            {
                StartSpawningPhase();
            }
        }
        
        // 开始生成Pawn阶段
        private void StartSpawningPhase()
        {
            currentState = TowerState.SpawningPawns;
            ticksInCurrentState = 0;
            pawnsSpawned = 0;
            
            // 初始化Pawn种类缓存
            if (cachedPawnKindDefs == null)
            {
                cachedPawnKindDefs = new List<PawnKindDef>();
                foreach (string pawnKindName in Props.pawnKindDefs)
                {
                    PawnKindDef pawnKindDef = DefDatabase<PawnKindDef>.GetNamedSilentFail(pawnKindName);
                    if (pawnKindDef != null)
                    {
                        cachedPawnKindDefs.Add(pawnKindDef);
                    }
                    else
                    {
                        Log.Error($"PhaseCombatTower: 找不到PawnKindDef '{pawnKindName}'");
                    }
                }
            }
            
            if (cachedPawnKindDefs.Count > 0 && Props.spawnCount > 0)
            {
                nextSpawnTick = Find.TickManager.TicksGame + Props.spawnIntervalTicks;
            }
            else
            {
                currentState = TowerState.Finished;
            }
        }
        
        public override void CompTick()
        {
            base.CompTick();
            
            if (parent.Map == null || parent.Destroyed)
                return;
            
            switch (currentState)
            {
                case TowerState.Warmup:
                    TickWarmup();
                    break;
                case TowerState.Exploding:
                    TickExploding();
                    break;
                case TowerState.SpawningPawns:
                    TickSpawningPawns();
                    break;
            }
        }
        
        private void TickWarmup()
        {
            ticksInCurrentState++;
            
            // 启动期结束
            if (ticksInCurrentState >= Props.warmupTicks)
            {
                StartExplosionPhase();
            }
        }
        
        private void TickExploding()
        {
            ticksInCurrentState++;
            
            // 检查是否需要执行下一次爆炸
            if (currentExplosionIndex < Props.explosions.Count)
            {
                // 第一次爆炸或冷却期已过
                if (currentExplosionIndex == 0 || 
                    ticksInCurrentState >= Props.explosionCooldownTicks * currentExplosionIndex)
                {
                    ExecuteExplosion(currentExplosionIndex);
                    currentExplosionIndex++;
                    
                    // 如果这是最后一次爆炸，开始生成Pawn阶段
                    if (currentExplosionIndex >= Props.explosions.Count)
                    {
                        StartSpawningPhase();
                    }
                }
            }
        }
        
        private void TickSpawningPawns()
        {
            if (Find.TickManager.TicksGame >= nextSpawnTick && pawnsSpawned < Props.spawnCount)
            {
                SpawnPawn();
                pawnsSpawned++;
                
                if (pawnsSpawned >= Props.spawnCount)
                {
                    currentState = TowerState.Finished;
                }
                else
                {
                    nextSpawnTick = Find.TickManager.TicksGame + Props.spawnIntervalTicks;
                }
            }
        }
        
        // 执行爆炸 - 使用标准爆炸方法，支持气体释放
        private void ExecuteExplosion(int explosionIndex)
        {
            if (explosionIndex < 0 || explosionIndex >= Props.explosions.Count)
                return;
            
            var explosionData = Props.explosions[explosionIndex];
            
            // 使用RimWorld标准爆炸方法
            if (parent.Map != null)
            {
                // 调用GenExplosion.DoExplosion方法，包含气体参数
                GenExplosion.DoExplosion(
                    center: parent.Position,                    // 爆炸中心
                    map: parent.Map,                           // 地图
                    radius: explosionData.radius,              // 爆炸半径
                    damType: explosionData.damageDef ?? DamageDefOf.Bomb, // 伤害类型
                    instigator: parent,                        // 爆炸者
                    damAmount: explosionData.damageAmount,     // 伤害值
                    armorPenetration: explosionData.armorPenetration, // 穿甲系数
                    explosionSound: explosionData.explosionSound, // 爆炸声音
                    weapon: null,                              // 武器（可选）
                    projectile: null,                          // 抛射物（可选）
                    intendedTarget: null,                      // 预定目标（可选）
                    
                    // 爆炸前生成物
                    preExplosionSpawnThingDef: explosionData.preExplosionSpawnThingDef,
                    preExplosionSpawnChance: explosionData.preExplosionSpawnChance,
                    preExplosionSpawnThingCount: explosionData.preExplosionSpawnThingCount,
                    
                    // 爆炸后生成物
                    postExplosionSpawnThingDef: explosionData.postExplosionSpawnThingDef,
                    postExplosionSpawnChance: explosionData.postExplosionSpawnChance,
                    postExplosionSpawnThingCount: explosionData.postExplosionSpawnThingCount,
                    
                    // 气体释放参数
                    postExplosionGasType: explosionData.postExplosionGasType, // 气体类型
                    postExplosionGasRadiusOverride: explosionData.postExplosionGasRadiusOverride, // 气体半径
                    postExplosionGasAmount: explosionData.postExplosionGasAmount, // 气体数量
                    
                    // 其他参数
                    applyDamageToExplosionCellsNeighbors: explosionData.applyDamageToExplosionCellsNeighbors,
                    chanceToStartFire: explosionData.chanceToStartFire,
                    damageFalloff: explosionData.damageFalloff,
                    direction: explosionData.direction,
                    ignoredThings: null,
                    affectedAngle: explosionData.affectedAngle,
                    doVisualEffects: true, // 总是显示视觉效果
                    propagationSpeed: explosionData.propagationSpeed,
                    excludeRadius: explosionData.excludeRadius,
                    doSoundEffects: explosionData.explosionSound != null,
                    screenShakeFactor: explosionData.screenShakeFactor
                );
            }
        }
        
        // 生成Pawn
        private void SpawnPawn()
        {
            if (cachedPawnKindDefs == null || cachedPawnKindDefs.Count == 0)
                return;
            
            // 随机选择Pawn种类
            PawnKindDef pawnKindDef = cachedPawnKindDefs.RandomElement();
            
            // 寻找合适的生成位置
            IntVec3 spawnPosition = FindSpawnPosition();
            if (!spawnPosition.IsValid)
                return;
            
            // 生成Pawn
            PawnGenerationRequest request = new PawnGenerationRequest(
                pawnKindDef,
                faction: parent.Faction,
                forceGenerateNewPawn: true,
                canGeneratePawnRelations: false,
                fixedBiologicalAge: 0,
                fixedChronologicalAge: 0
            );
            
            Pawn pawn = PawnGenerator.GeneratePawn(request);
            
            // 生成Pawn到地图
            GenSpawn.Spawn(pawn, spawnPosition, parent.Map);
            
            // 添加生成效果
            FleckMaker.ThrowDustPuff(spawnPosition, parent.Map, 2f);
        }
        
        // 寻找生成位置
        private IntVec3 FindSpawnPosition()
        {
            Map map = parent.Map;
            
            // 尝试在建筑周围寻找合适的空单元格
            for (int radius = 1; radius <= 5; radius++)
            {
                CellRect rect = CellRect.CenteredOn(parent.Position, radius);
                List<IntVec3> validCells = new List<IntVec3>();
                
                foreach (IntVec3 cell in rect)
                {
                    if (cell.InBounds(map) && 
                        cell.Walkable(map) && 
                        !cell.Fogged(map) &&
                        map.thingGrid.ThingsAt(cell).Count() == 0)
                    {
                        validCells.Add(cell);
                    }
                }
                
                if (validCells.Count > 0)
                {
                    return validCells.RandomElement();
                }
            }
            
            // 如果找不到合适位置，使用建筑位置（可能会重叠）
            return parent.Position;
        }
        
        // 获取当前状态描述（用于UI显示）
        public string GetStatusDescription()
        {
            switch (currentState)
            {
                case TowerState.Warmup:
                    float progress = (float)ticksInCurrentState / Props.warmupTicks;
                    return $"启动中: {Mathf.RoundToInt(progress * 100)}%";
                    
                case TowerState.Exploding:
                    return $"爆炸阶段: {currentExplosionIndex + 1}/{Props.explosions.Count}";
                    
                case TowerState.SpawningPawns:
                    return $"生成单位: {pawnsSpawned}/{Props.spawnCount}";
                    
                case TowerState.Finished:
                    return "已完成";
                    
                default:
                    return "待机";
            }
        }
        
        // 保存和加载状态
        public override void PostExposeData()
        {
            base.PostExposeData();
            
            Scribe_Values.Look(ref currentState, "currentState", TowerState.Idle);
            Scribe_Values.Look(ref ticksInCurrentState, "ticksInCurrentState", 0);
            Scribe_Values.Look(ref currentExplosionIndex, "currentExplosionIndex", 0);
            Scribe_Values.Look(ref pawnsSpawned, "pawnsSpawned", 0);
            Scribe_Values.Look(ref nextSpawnTick, "nextSpawnTick", 0);
        }
        
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            // 添加调试Gizmo（开发模式下）
            if (DebugSettings.godMode)
            {
                yield return new Command_Action
                {
                    defaultLabel = "强制启动",
                    action = () => StartWarmup(),
                    icon = TexCommand.ForbidOff
                };
                
                yield return new Command_Action
                {
                    defaultLabel = "跳至爆炸阶段",
                    action = () => StartExplosionPhase(),
                    icon = TexCommand.Attack
                };
                
                yield return new Command_Action
                {
                    defaultLabel = "跳至生成阶段",
                    action = () => StartSpawningPhase(),
                    icon = TexCommand.Attack
                };
            }
        }
    }
}
