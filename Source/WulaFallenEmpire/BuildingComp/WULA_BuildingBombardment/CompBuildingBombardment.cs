using RimWorld;
using Verse;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace WulaFallenEmpire
{
    public class CompBuildingBombardment : ThingComp
    {
        public CompProperties_BuildingBombardment Props => (CompProperties_BuildingBombardment)props;
        
        // 轰炸状态
        private BuildingBombardmentState currentState = BuildingBombardmentState.Idle;
        private int nextBurstTick = 0;
        private int currentBurstCount = 0;
        private int nextInnerBurstTick = 0;
        private List<LocalTargetInfo> currentTargets = new List<LocalTargetInfo>();
        
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            
            if (!respawningAfterLoad)
            {
                // 新生成时立即开始第一轮轰炸
                StartNextBurst();
            }
        }
        
        private void StartNextBurst()
        {
            currentState = BuildingBombardmentState.Targeting;
            currentBurstCount = 0;
            currentTargets.Clear();
            
            // 选择目标
            SelectTargets();
            
            if (currentTargets.Count > 0)
            {
                currentState = BuildingBombardmentState.Bursting;
                nextInnerBurstTick = Find.TickManager.TicksGame;
                WulaLog.Debug($"[BuildingBombardment] Starting burst with {currentTargets.Count} targets");
            }
            else
            {
                // 没有找到目标，等待下一轮
                currentState = BuildingBombardmentState.Idle;
                nextBurstTick = Find.TickManager.TicksGame + Props.burstIntervalTicks;
                WulaLog.Debug($"[BuildingBombardment] No targets found, waiting for next burst");
            }
        }
        
        private void SelectTargets()
        {
            Map map = parent.Map;
            if (map == null) return;
            
            // 获取范围内的所有pawn
            var potentialTargets = new List<Pawn>();
            
            foreach (var pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (IsValidTarget(pawn) && IsInRange(pawn.Position))
                {
                    potentialTargets.Add(pawn);
                }
            }
            
            // 随机选择目标，最多burstCount个
            int targetCount = Mathf.Min(Props.burstCount, potentialTargets.Count);
            currentTargets = potentialTargets
                .InRandomOrder()
                .Take(targetCount)
                .Select(p => new LocalTargetInfo(p))
                .ToList();
        }
        
        private bool IsValidTarget(Pawn pawn)
        {
            if (pawn == null || pawn.Downed || pawn.Dead) return false;
            
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
        
        private void UpdateBursting()
        {
            if (Find.TickManager.TicksGame < nextInnerBurstTick)
                return;
                
            if (currentBurstCount >= currentTargets.Count)
            {
                // 当前组发射完毕
                currentState = BuildingBombardmentState.Idle;
                nextBurstTick = Find.TickManager.TicksGame + Props.burstIntervalTicks;
                WulaLog.Debug($"[BuildingBombardment] Burst completed, waiting for next burst");
                return;
            }
            
            // 发射当前目标
            var target = currentTargets[currentBurstCount];
            LaunchBombardment(target);
            currentBurstCount++;
            
            // 设置下一个组内发射时间
            if (currentBurstCount < currentTargets.Count)
            {
                nextInnerBurstTick = Find.TickManager.TicksGame + Props.innerBurstIntervalTicks;
            }
            
            WulaLog.Debug($"[BuildingBombardment] Launched bombardment {currentBurstCount}/{currentTargets.Count}");
        }
        
        private void LaunchBombardment(LocalTargetInfo target)
        {
            try
            {
                // 应用随机偏移
                IntVec3 targetCell = ApplyRandomOffset(target.Cell);
                
                if (Props.skyfallerDef != null)
                {
                    // 使用 Skyfaller
                    Skyfaller skyfaller = SkyfallerMaker.MakeSkyfaller(Props.skyfallerDef);
                    GenSpawn.Spawn(skyfaller, targetCell, parent.Map);
                }
                else if (Props.projectileDef != null)
                {
                    // 使用抛射体作为备用
                    LaunchProjectileAt(targetCell);
                }
            }
            catch (System.Exception ex)
            {
                WulaLog.Debug($"[BuildingBombardment] Error launching bombardment: {ex}");
            }
        }
        
        private IntVec3 ApplyRandomOffset(IntVec3 originalCell)
        {
            if (Props.randomOffset <= 0f)
                return originalCell;
                
            // 在随机偏移范围内选择一个位置
            float offsetX = Rand.Range(-Props.randomOffset, Props.randomOffset);
            float offsetZ = Rand.Range(-Props.randomOffset, Props.randomOffset);
            
            IntVec3 offsetCell = new IntVec3(
                Mathf.RoundToInt(originalCell.x + offsetX),
                originalCell.y,
                Mathf.RoundToInt(originalCell.z + offsetZ)
            );
            
            // 确保位置有效
            if (offsetCell.InBounds(parent.Map))
                return offsetCell;
            else
                return originalCell;
        }
        
        private void LaunchProjectileAt(IntVec3 targetCell)
        {
            // 从建筑位置发射抛射体
            Vector3 spawnPos = parent.Position.ToVector3Shifted();
            
            Projectile projectile = (Projectile)GenSpawn.Spawn(Props.projectileDef, parent.Position, parent.Map);
            if (projectile != null)
            {
                projectile.Launch(
                    parent,
                    spawnPos,
                    new LocalTargetInfo(targetCell),
                    new LocalTargetInfo(targetCell),
                    ProjectileHitFlags.All,
                    false
                );
            }
        }
        
        public override void CompTick()
        {
            base.CompTick();
            
            switch (currentState)
            {
                case BuildingBombardmentState.Idle:
                    if (Find.TickManager.TicksGame >= nextBurstTick)
                    {
                        StartNextBurst();
                    }
                    break;
                    
                case BuildingBombardmentState.Bursting:
                    UpdateBursting();
                    break;
            }
        }
        
        public override void PostExposeData()
        {
            base.PostExposeData();
            
            Scribe_Values.Look(ref currentState, "currentState", BuildingBombardmentState.Idle);
            Scribe_Values.Look(ref nextBurstTick, "nextBurstTick", 0);
            Scribe_Values.Look(ref currentBurstCount, "currentBurstCount", 0);
            Scribe_Values.Look(ref nextInnerBurstTick, "nextInnerBurstTick", 0);
            Scribe_Collections.Look(ref currentTargets, "currentTargets", LookMode.LocalTargetInfo);
        }
    }
    
    public enum BuildingBombardmentState
    {
        Idle,
        Targeting,
        Bursting
    }
}
