using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Sound;
using static UnityEngine.GraphicsBuffer;

namespace WulaFallenEmpire
{
    public class CompTrapLauncher : ThingComp
    {
        public CompProperties_TrapLauncher Props => (CompProperties_TrapLauncher)props;
        
        private int scanTickCounter = 0;
        private bool hasTriggered = false;
        private Pawn currentTarget = null;
        private int warmupCounter = 0;
        private bool isWarmingUp = false;
        private int burstCounter = 0;
        
        // 已检测过的目标（避免重复攻击）
        private HashSet<Pawn> detectedTargets = new HashSet<Pawn>();
        
        // 用于绘制检测范围
        private Material cachedRadiusMat;
        private Material RadiusMat
        {
            get
            {
                if (cachedRadiusMat == null)
                {
                    cachedRadiusMat = SolidColorMaterials.SimpleSolidColorMaterial(
                        new Color(1f, 0.2f, 0.2f, 0.1f));
                }
                return cachedRadiusMat;
            }
        }
        
        public override void CompTick()
        {
            base.CompTick();
            
            if (!parent.Spawned || hasTriggered)
                return;
                
            // 预热计数
            if (isWarmingUp)
            {
                warmupCounter++;
                if (warmupCounter >= Props.warmupTicks)
                {
                    LaunchProjectile();
                }
                return;
            }
            
            // 扫描计数
            scanTickCounter++;
            if (scanTickCounter >= Props.scanIntervalTicks)
            {
                scanTickCounter = 0;
                ScanForTargets();
            }
        }
        
        /// <summary>
        /// 扫描范围内的敌对目标
        /// </summary>
        private void ScanForTargets()
        {
            if (!parent.Spawned)
                return;
                
            Map map = parent.Map;
            IntVec3 center = parent.Position;
            
            // 获取范围内的所有Pawn
            List<Pawn> pawnsInRange = new List<Pawn>();
            
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, Props.detectionRadius, true))
            {
                if (!cell.InBounds(map))
                    continue;
                    
                // 检查视线（如果需要）
                if (Props.requireLineOfSight)
                {
                    if (!GenSight.LineOfSight(center, cell, map, skipFirstCell: true))
                        continue;
                }
                
                // 获取格子上的所有Pawn
                List<Thing> things = map.thingGrid.ThingsListAtFast(cell);
                foreach (Thing thing in things)
                {
                    if (thing is Pawn pawn && !detectedTargets.Contains(pawn))
                    {
                        // 检查是否敌对
                        if (IsValidTarget(pawn))
                        {
                            pawnsInRange.Add(pawn);
                        }
                    }
                }
            }
            
            // 如果没有目标，直接返回
            if (pawnsInRange.Count == 0)
                return;
                
            // 选择第一个目标
            currentTarget = pawnsInRange[0];
            detectedTargets.Add(currentTarget);
            
            // 触发陷阱
            TriggerTrap();
        }
        
        /// <summary>
        /// 检查是否为有效目标
        /// </summary>
        private bool IsValidTarget(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Downed)
                return false;
                
            if (Props.ignoreNonHostilePawns)
            {
                // 检查是否为敌对派系
                if (pawn.Faction == null || !pawn.Faction.HostileTo(Faction.OfPlayer))
                    return false;
                    
                // 检查是否为自然敌对生物
                if (!pawn.Faction.IsPlayer && !pawn.Faction.HostileTo(Faction.OfPlayer))
                {
                    // 检查是否为敌对性生物（如人形生物巢穴的敌人）
                    if (!pawn.RaceProps.Humanlike && !pawn.def.race.Animal)
                        return false;
                }
            }
            
            // 检查是否为机械体（如果设定）
            // 这里可以根据需要添加更多过滤条件
            
            return true;
        }
        
        /// <summary>
        /// 触发陷阱
        /// </summary>
        private void TriggerTrap()
        {
            if (hasTriggered)
                return;
                
            // 播放触发音效
            if (Props.triggerSound != null)
            {
                Props.triggerSound.PlayOneShot(new TargetInfo(parent.Position, parent.Map));
            }
            
            // 播放触发特效
            if (Props.triggerEffect != null)
            {
                Effecter effecter = Props.triggerEffect.Spawn();
                effecter.Trigger(parent, parent);
                effecter.Cleanup();
            }
            
            // 发送消息
            if (currentTarget != null)
            {
                Messages.Message("WULA_TrapLauncherTriggered".Translate(
                    parent.Label, 
                    currentTarget.LabelShort
                ), parent, MessageTypeDefOf.ThreatBig);
            }
            
            // 开始预热
            isWarmingUp = true;
            warmupCounter = 0;
            burstCounter = 0;
        }
        
        /// <summary>
        /// 发射抛射体
        /// </summary>
        private void LaunchProjectile()
        {
            if (currentTarget == null || !currentTarget.Spawned)
            {
                // 如果目标无效，尝试寻找新目标
                if (Props.canRetarget)
                {
                    currentTarget = FindNewTarget();
                    if (currentTarget == null)
                    {
                        // 没有新目标，取消发射
                        isWarmingUp = false;
                        return;
                    }
                }
                else
                {
                    // 直接自毁
                    SelfDestruct();
                    return;
                }
            }
            
            // 发射抛射体
            for (int i = 0; i < Props.burstCount; i++)
            {
                if (burstCounter >= Props.maxTargets)
                    break;
                    
                // 创建抛射体
                Projectile projectile = (Projectile)GenSpawn.Spawn(
                    Props.projectileDef, 
                    parent.Position, 
                    parent.Map
                );
                
                // 发射
                projectile.Launch(parent, parent.DrawPos, currentTarget, currentTarget, ProjectileHitFlags.IntendedTarget, false);
                
                // 连发延迟
                if (i < Props.burstCount - 1 && Props.burstDelay > 0)
                {
                    // 使用简单的延迟实现
                    // 在实际游戏中，可能需要更复杂的实现
                    // 这里我们简化处理
                }
            }
            
            // 播放发射音效
            if (Props.launchSound != null)
            {
                Props.launchSound.PlayOneShot(new TargetInfo(parent.Position, parent.Map));
            }
            
            // 播放发射特效
            if (Props.launchEffect != null)
            {
                Effecter effecter = Props.launchEffect.Spawn();
                effecter.Trigger(parent, parent);
                effecter.Cleanup();
            }
            
            // 检查是否需要继续攻击
            if (burstCounter >= Props.maxTargets || !Props.canRetarget)
            {
                // 自毁
                SelfDestruct();
            }
            else
            {
                // 寻找新目标
                currentTarget = FindNewTarget();
                if (currentTarget == null)
                {
                    SelfDestruct();
                }
                else
                {
                    // 重置预热，准备下一次发射
                    warmupCounter = 0;
                    burstCounter = 0;
                }
            }
        }
        
        /// <summary>
        /// 寻找新目标
        /// </summary>
        private Pawn FindNewTarget()
        {
            Map map = parent.Map;
            IntVec3 center = parent.Position;
            
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, Props.detectionRadius, true))
            {
                if (!cell.InBounds(map))
                    continue;
                    
                if (Props.requireLineOfSight && !GenSight.LineOfSight(center, cell, map, skipFirstCell: true))
                    continue;
                    
                List<Thing> things = map.thingGrid.ThingsListAtFast(cell);
                foreach (Thing thing in things)
                {
                    if (thing is Pawn pawn && !detectedTargets.Contains(pawn) && IsValidTarget(pawn))
                    {
                        detectedTargets.Add(pawn);
                        return pawn;
                    }
                    
                    // 如果目标建筑
                    if (Props.targetBuildings && thing is Building building && 
                        building.Faction != null && building.Faction.HostileTo(Faction.OfPlayer))
                    {
                        // 注意：这里需要处理建筑目标，但抛射体可能需要调整
                        // 为了简化，这里只处理Pawn
                    }
                }
            }
            
            return null;
        }
        
        /// <summary>
        /// 自毁
        /// </summary>
        private void SelfDestruct()
        {
            if (hasTriggered)
                return;
                
            hasTriggered = true;
            
            // 播放自毁音效
            if (Props.selfDestructSound != null)
            {
                Props.selfDestructSound.PlayOneShot(new TargetInfo(parent.Position, parent.Map));
            }
            
            // 播放自毁特效
            if (Props.selfDestructEffect != null)
            {
                Effecter effecter = Props.selfDestructEffect.Spawn();
                effecter.Trigger(parent, parent);
                effecter.Cleanup();
            }
            
            // 销毁建筑
            parent.Destroy(DestroyMode.Vanish);
        }
        
        /// <summary>
        /// 绘制检测范围（仅在选定和调试模式下）
        /// </summary>
        public override void PostDrawExtraSelectionOverlays()
        {
            base.PostDrawExtraSelectionOverlays();
            
            if (Props.showDetectionRadius && Props.detectionRadius > 0)
            {
                GenDraw.DrawRadiusRing(parent.Position, Props.detectionRadius, Color.red);
            }
        }
        
        /// <summary>
        /// 获取Gizmo按钮
        /// </summary>
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }
            
            if (!hasTriggered && DebugSettings.ShowDevGizmos)
            {
                // 调试：手动触发
                Command_Action debugTrigger = new Command_Action();
                debugTrigger.defaultLabel = "DEV: Trigger Trap";
                debugTrigger.action = delegate
                {
                    currentTarget = FindClosestHostilePawn();
                    if (currentTarget != null)
                    {
                        TriggerTrap();
                    }
                    else
                    {
                        Messages.Message("WULA_TrapLauncherNoTargetFound".Translate(), 
                            parent, MessageTypeDefOf.RejectInput);
                    }
                };
                yield return debugTrigger;
                
                // 调试：立即自毁
                Command_Action debugDestruct = new Command_Action();
                debugDestruct.defaultLabel = "DEV: Self-Destruct";
                debugDestruct.action = delegate
                {
                    SelfDestruct();
                };
                yield return debugDestruct;
            }
        }
        
        /// <summary>
        /// 查找最近的敌对Pawn（调试用）
        /// </summary>
        private Pawn FindClosestHostilePawn()
        {
            if (!parent.Spawned)
                return null;
                
            Pawn closestPawn = null;
            float closestDist = float.MaxValue;
            
            foreach (Pawn pawn in parent.Map.mapPawns.AllPawnsSpawned)
            {
                if (IsValidTarget(pawn))
                {
                    float dist = pawn.Position.DistanceTo(parent.Position);
                    if (dist <= Props.detectionRadius && dist < closestDist)
                    {
                        closestDist = dist;
                        closestPawn = pawn;
                    }
                }
            }
            
            return closestPawn;
        }
        
        public override void PostExposeData()
        {
            base.PostExposeData();
            
            Scribe_Values.Look(ref scanTickCounter, "scanTickCounter", 0);
            Scribe_Values.Look(ref hasTriggered, "hasTriggered", false);
            Scribe_Values.Look(ref isWarmingUp, "isWarmingUp", false);
            Scribe_Values.Look(ref warmupCounter, "warmupCounter", 0);
            Scribe_Values.Look(ref burstCounter, "burstCounter", 0);
            Scribe_References.Look(ref currentTarget, "currentTarget");
            Scribe_Collections.Look(ref detectedTargets, "detectedTargets", LookMode.Reference);
            
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (detectedTargets == null)
                {
                    detectedTargets = new HashSet<Pawn>();
                }
                
                // 移除无效的目标引用
                detectedTargets.RemoveWhere(pawn => pawn == null || pawn.Destroyed);
                
                // 如果已经在预热但目标无效，尝试恢复或自毁
                if (isWarmingUp && (currentTarget == null || !currentTarget.Spawned))
                {
                    if (Props.canRetarget)
                    {
                        currentTarget = FindNewTarget();
                        if (currentTarget == null)
                        {
                            SelfDestruct();
                        }
                    }
                    else
                    {
                        SelfDestruct();
                    }
                }
            }
        }
    }
}
