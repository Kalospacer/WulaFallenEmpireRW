using RimWorld;
using Verse;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace WulaFallenEmpire
{
    public class CompBuildingEnergyLance : ThingComp
    {
        public CompProperties_BuildingEnergyLance Props => (CompProperties_BuildingEnergyLance)props;
        
        // 状态管理
        private EnergyLanceState currentState = EnergyLanceState.Idle;
        private int nextUpdateTick = 0;
        private int noTargetTicks = 0;
        
        // 目标管理
        private Pawn currentTarget = null;
        private IntVec3 lastTargetPosition = IntVec3.Invalid;
        
        // EnergyLance实例
        private GuidedEnergyLance activeLance = null;
        
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            
            if (!respawningAfterLoad)
            {
                // 新生成时立即开始搜索目标
                nextUpdateTick = Find.TickManager.TicksGame;
                currentState = EnergyLanceState.Searching;
                
                Log.Message($"[BuildingEnergyLance] Building spawned, starting target search");
            }
        }
        
        private void UpdateState()
        {
            switch (currentState)
            {
                case EnergyLanceState.Idle:
                    // 空闲状态，等待下一次更新
                    break;
                    
                case EnergyLanceState.Searching:
                    SearchForTarget();
                    break;
                    
                case EnergyLanceState.Tracking:
                    TrackTarget();
                    break;
                    
                case EnergyLanceState.NoTarget:
                    HandleNoTarget();
                    break;
            }
        }
        
        private void SearchForTarget()
        {
            Map map = parent.Map;
            if (map == null) return;
            
            // 获取范围内的所有有效目标
            var potentialTargets = new List<Pawn>();
            
            foreach (var pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (IsValidTarget(pawn) && IsInRange(pawn.Position))
                {
                    potentialTargets.Add(pawn);
                }
            }
            
            if (potentialTargets.Count > 0)
            {
                // 选择第一个目标（可以改为其他选择逻辑）
                currentTarget = potentialTargets[0];
                lastTargetPosition = currentTarget.Position;
                
                // 创建EnergyLance
                CreateEnergyLance();
                
                currentState = EnergyLanceState.Tracking;
                noTargetTicks = 0;
                
                Log.Message($"[BuildingEnergyLance] Locked target: {currentTarget.Label}, position: {currentTarget.Position}");
            }
            else
            {
                // 没有找到目标
                currentState = EnergyLanceState.NoTarget;
                Log.Message($"[BuildingEnergyLance] No targets found in range");
            }
        }
        
        private void TrackTarget()
        {
            if (currentTarget == null || !currentTarget.Spawned)
            {
                // 目标丢失，重新搜索
                currentState = EnergyLanceState.Searching;
                currentTarget = null;
                return;
            }
            
            // 检查目标是否仍然有效
            if (!IsValidTarget(currentTarget) || !IsInRange(currentTarget.Position))
            {
                // 目标无效或超出范围，寻找最近的敌人
                FindNearestTarget();
                return;
            }
            
            // 更新目标位置
            lastTargetPosition = currentTarget.Position;
            
            // 向EnergyLance发送目标位置
            if (activeLance != null && !activeLance.Destroyed)
            {
                activeLance.UpdateTarget(lastTargetPosition);
                noTargetTicks = 0;
                
                Log.Message($"[BuildingEnergyLance] Updated target position: {lastTargetPosition}");
            }
            else
            {
                // EnergyLance丢失，重新创建
                CreateEnergyLance();
            }
        }
        
        private void FindNearestTarget()
        {
            Map map = parent.Map;
            if (map == null) return;
            
            Pawn nearestTarget = null;
            float nearestDistance = float.MaxValue;
            
            // 获取当前EnergyLance位置
            IntVec3 searchCenter = (activeLance != null && !activeLance.Destroyed) ? 
                activeLance.Position : parent.Position;
            
            foreach (var pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (IsValidTarget(pawn) && IsInRange(pawn.Position))
                {
                    float distance = Vector3.Distance(searchCenter.ToVector3(), pawn.Position.ToVector3());
                    if (distance < nearestDistance)
                    {
                        nearestDistance = distance;
                        nearestTarget = pawn;
                    }
                }
            }
            
            if (nearestTarget != null)
            {
                currentTarget = nearestTarget;
                lastTargetPosition = currentTarget.Position;
                
                // 更新EnergyLance目标
                if (activeLance != null && !activeLance.Destroyed)
                {
                    activeLance.UpdateTarget(lastTargetPosition);
                }
                
                Log.Message($"[BuildingEnergyLance] Switched to nearest target: {currentTarget.Label}, distance: {nearestDistance}");
            }
            else
            {
                // 没有找到替代目标
                currentState = EnergyLanceState.NoTarget;
                currentTarget = null;
                Log.Message($"[BuildingEnergyLance] No alternative targets found");
            }
        }
        
        private void HandleNoTarget()
        {
            noTargetTicks++;
            
            // 向EnergyLance发送空位置
            if (activeLance != null && !activeLance.Destroyed)
            {
                activeLance.UpdateTarget(IntVec3.Invalid);
            }
            
            // 检查是否应该销毁EnergyLance
            if (noTargetTicks >= Props.maxNoTargetTicks)
            {
                if (activeLance != null && !activeLance.Destroyed)
                {
                    activeLance.Destroy();
                    activeLance = null;
                }
                
                // 回到搜索状态
                currentState = EnergyLanceState.Searching;
                noTargetTicks = 0;
                
                Log.Message($"[BuildingEnergyLance] EnergyLance destroyed due to no targets");
            }
            else if (noTargetTicks % 60 == 0) // 每60刻检查一次是否有新目标
            {
                SearchForTarget();
            }
        }
        
        private void CreateEnergyLance()
        {
            if (Props.energyLanceDef == null)
            {
                Log.Error($"[BuildingEnergyLance] No energyLanceDef configured");
                return;
            }
            
            try
            {
                // 销毁现有的EnergyLance
                if (activeLance != null && !activeLance.Destroyed)
                {
                    activeLance.Destroy();
                }
                
                // 创建新的EnergyLance
                IntVec3 spawnPos = GetLanceSpawnPosition();
                activeLance = (GuidedEnergyLance)GenSpawn.Spawn(Props.energyLanceDef, spawnPos, parent.Map);
                
                // 初始化EnergyLance
                activeLance.duration = Props.energyLanceDuration;
                activeLance.instigator = parent;
                activeLance.controlBuilding = this.parent;
                activeLance.firesPerTick = Props.firesPerTick;
                
                // 如果有当前目标，设置初始位置
                if (currentTarget != null)
                {
                    activeLance.UpdateTarget(currentTarget.Position);
                }
                
                activeLance.StartStrike();
                
                Log.Message($"[BuildingEnergyLance] Created EnergyLance at {spawnPos}");
            }
            catch (System.Exception ex)
            {
                Log.Error($"[BuildingEnergyLance] Error creating EnergyLance: {ex}");
            }
        }
        
        private IntVec3 GetLanceSpawnPosition()
        {
            // 在建筑周围寻找一个合适的生成位置
            Map map = parent.Map;
            IntVec3 center = parent.Position;
            
            // 优先选择建筑上方的位置
            if (center.InBounds(map) && center.Walkable(map))
            {
                return center;
            }
            
            // 如果建筑位置不可用，在周围寻找
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, 2f, true))
            {
                if (cell.InBounds(map) && cell.Walkable(map))
                {
                    return cell;
                }
            }
            
            // 如果都不可用，返回建筑位置
            return center;
        }
        
        private bool IsValidTarget(Pawn pawn)
        {
            if (pawn == null || pawn.Downed || pawn.Dead || !pawn.Spawned)
                return false;
            
            // 检查目标类型
            if (Props.targetEnemies && pawn.HostileTo(parent.Faction))
                return true;
                
            if (Props.targetNeutrals && !pawn.HostileTo(parent.Faction) && pawn.Faction != parent.Faction)
                return true;
                
            if (Props.targetAnimals && pawn.RaceProps.Animal)
                return true;
                
            return false;
        }
        
        private bool IsInRange(IntVec3 position)
        {
            float distance = Vector3.Distance(parent.Position.ToVector3(), position.ToVector3());
            return distance <= Props.radius;
        }
        
        public override void CompTick()
        {
            base.CompTick();
            
            int currentTick = Find.TickManager.TicksGame;
            
            // 检查是否需要更新状态
            if (currentTick >= nextUpdateTick)
            {
                UpdateState();
                nextUpdateTick = currentTick + Props.updateIntervalTicks;
            }
            
            // 检查EnergyLance状态
            if (activeLance != null && activeLance.Destroyed)
            {
                activeLance = null;
                if (currentState == EnergyLanceState.Tracking)
                {
                    currentState = EnergyLanceState.Searching;
                }
            }
        }
        
        // 外部调用：当EnergyLance需要目标时调用
        public bool TryGetCurrentTarget(out IntVec3 targetPos)
        {
            if (currentTarget != null && IsValidTarget(currentTarget) && IsInRange(currentTarget.Position))
            {
                targetPos = currentTarget.Position;
                return true;
            }
            
            targetPos = IntVec3.Invalid;
            return false;
        }
        
        public override void PostExposeData()
        {
            base.PostExposeData();
            
            Scribe_Values.Look(ref currentState, "currentState", EnergyLanceState.Idle);
            Scribe_Values.Look(ref nextUpdateTick, "nextUpdateTick", 0);
            Scribe_Values.Look(ref noTargetTicks, "noTargetTicks", 0);
            Scribe_References.Look(ref currentTarget, "currentTarget");
            Scribe_Values.Look(ref lastTargetPosition, "lastTargetPosition", IntVec3.Invalid);
            Scribe_References.Look(ref activeLance, "activeLance");
        }
    }
    
    public enum EnergyLanceState
    {
        Idle,
        Searching,
        Tracking,
        NoTarget
    }
}
