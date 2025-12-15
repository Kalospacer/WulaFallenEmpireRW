using RimWorld;
using Verse;
using System.Collections.Generic;
using UnityEngine;
using HarmonyLib;
using Verse.AI;
using Verse.Sound;

namespace WulaFallenEmpire
{
    public partial class ThingComp_AreaTeleporter : ThingComp
    {
        public CompProperties_AreaTeleporter Props => (CompProperties_AreaTeleporter)props;
        
        // 跟踪范围内的Pawn
        private HashSet<Pawn> pawnsInRange = new HashSet<Pawn>();
        
        // 效果器缓存
        private Dictionary<Pawn, Effecter> effecters = new Dictionary<Pawn, Effecter>();

        // 网络相关：存储同一地图上的所有传送器
        private static Dictionary<Map, HashSet<ThingComp_AreaTeleporter>> teleporterNetworks = 
            new Dictionary<Map, HashSet<ThingComp_AreaTeleporter>>();

        // 硬编码的工作排除表
        private static readonly HashSet<JobDef> ExcludedJobs = new HashSet<JobDef>
        {
            JobDefOf.GotoWander
        };

        // 新增：开关状态
        private bool enabled = true;

        // 新增：初始化时设置默认状态
        public override void Initialize(CompProperties props)
        {
            base.Initialize(props);
            enabled = Props.defaultEnabled;
            RegisterToNetwork();
        }

        // 新增：保存和加载开关状态
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref enabled, "teleporterEnabled", Props.defaultEnabled);
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            RegisterToNetwork();
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map);
            UnregisterFromNetwork();
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            UnregisterFromNetwork();
        }

        /// <summary>
        /// 检查是否满足科技需求
        /// </summary>
        public bool HasRequiredResearch
        {
            get
            {
                // 如果没有设置科技需求，或者不要求科技，则返回true
                if (Props.requiredResearch == null || !Props.requireResearchToUse)
                    return true;
                
                // 检查科技是否已完成
                return Props.requiredResearch.IsFinished;
            }
        }

        /// <summary>
        /// 检查是否应该显示传送器功能
        /// </summary>
        public bool ShouldDisplayFunctionality
        {
            get
            {
                // 如果拥有者是玩家，检查科技需求
                if (parent.Faction == Faction.OfPlayer)
                {
                    return HasRequiredResearch;
                }
                
                // 非玩家派系总是显示
                return true;
            }
        }

        /// <summary>
        /// 注册到网络
        /// </summary>
        private void RegisterToNetwork()
        {
            if (parent?.Map == null || !enabled || !ShouldDisplayFunctionality) return;

            var map = parent.Map;
            if (!teleporterNetworks.ContainsKey(map))
            {
                teleporterNetworks[map] = new HashSet<ThingComp_AreaTeleporter>();
            }

            teleporterNetworks[map].Add(this);
        }

        /// <summary>
        /// 从网络注销
        /// </summary>
        private void UnregisterFromNetwork()
        {
            if (parent?.Map == null) return;

            var map = parent.Map;
            if (teleporterNetworks.ContainsKey(map))
            {
                teleporterNetworks[map].Remove(this);
                if (teleporterNetworks[map].Count == 0)
                {
                    teleporterNetworks.Remove(map);
                }
            }
        }

        /// <summary>
        /// 获取同一地图上的所有传送器
        /// </summary>
        private HashSet<ThingComp_AreaTeleporter> GetNetworkTeleporters()
        {
            if (parent?.Map == null) return new HashSet<ThingComp_AreaTeleporter>();
            
            if (teleporterNetworks.TryGetValue(parent.Map, out var network))
            {
                return network;
            }
            return new HashSet<ThingComp_AreaTeleporter>();
        }

        /// <summary>
        /// 检查位置是否在传送网络的范围内
        /// </summary>
        private bool IsPositionInNetworkRange(IntVec3 position)
        {
            if (parent?.Map == null || !enabled || !ShouldDisplayFunctionality) return false;

            foreach (var teleporter in GetNetworkTeleporters())
            {
                if (teleporter.parent?.Spawned == true && 
                    teleporter.enabled && teleporter.ShouldDisplayFunctionality &&
                    position.DistanceTo(teleporter.parent.Position) <= teleporter.Props.teleportRadius)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 在传送网络范围内寻找安全的传送位置
        /// </summary>
        private IntVec3 FindSafePositionInNetwork(IntVec3 preferredPosition, Pawn pawn)
        {
            if (parent?.Map == null || !enabled || !ShouldDisplayFunctionality) return IntVec3.Invalid;

            var map = parent.Map;
            
            // 首先检查首选位置
            if (preferredPosition.InBounds(map) && CanTeleportTo(preferredPosition, map))
                return preferredPosition;

            // 在首选位置周围搜索
            for (int radius = 1; radius <= Props.maxPositionAdjustRadius; radius++)
            {
                foreach (var cell in GenRadial.RadialCellsAround(preferredPosition, radius, true))
                {
                    if (cell.InBounds(map) && CanTeleportTo(cell, map) && IsPositionInNetworkRange(cell))
                    {
                        return cell;
                    }
                }
            }

            // 在整个网络范围内搜索安全位置
            foreach (var teleporter in GetNetworkTeleporters())
            {
                if (teleporter.parent?.Spawned != true || !teleporter.enabled || !teleporter.ShouldDisplayFunctionality) 
                    continue;

                var teleporterPos = teleporter.parent.Position;
                var searchRadius = teleporter.Props.teleportRadius;

                // 在传送器周围搜索
                for (int radius = 0; radius <= searchRadius; radius++)
                {
                    foreach (var cell in GenRadial.RadialCellsAround(teleporterPos, radius, true))
                    {
                        if (cell.InBounds(map) && CanTeleportTo(cell, map))
                        {
                            return cell;
                        }
                    }
                }
            }

            return IntVec3.Invalid;
        }

        public override void CompTick()
        {
            base.CompTick();
            
            if (parent == null || !parent.Spawned || parent.Map == null || !enabled || !ShouldDisplayFunctionality)
                return;
                
            // 使用间隔检查优化性能
            if (Find.TickManager.TicksGame % Props.checkIntervalTicks != 0)
                return;
                
            UpdatePawnsInRange();
            ProcessPawnMovements();
        }

        private void UpdatePawnsInRange()
        {
            var newPawnsInRange = new HashSet<Pawn>();
            
            // 获取范围内的所有pawn
            foreach (var thing in parent.Map.listerThings.ThingsInGroup(ThingRequestGroup.Pawn))
            {
                if (thing is Pawn pawn && 
                    pawn.Position.DistanceTo(parent.Position) <= Props.teleportRadius &&
                    pawn.Spawned && !pawn.Dead && !pawn.Downed)
                {
                    newPawnsInRange.Add(pawn);
                }
            }
            
            // 处理新进入范围的pawn
            foreach (var pawn in newPawnsInRange)
            {
                if (!pawnsInRange.Contains(pawn) && ShouldAffectPawn(pawn))
                {
                    OnPawnEnterRange(pawn);
                }
            }
            
            // 处理离开范围的pawn
            var pawnsToRemove = new List<Pawn>();
            foreach (var pawn in pawnsInRange)
            {
                if (!newPawnsInRange.Contains(pawn))
                {
                    pawnsToRemove.Add(pawn);
                }
            }
            
            foreach (var pawn in pawnsToRemove)
            {
                OnPawnLeaveRange(pawn);
            }
            
            pawnsInRange = newPawnsInRange;
        }

        private void ProcessPawnMovements()
        {
            foreach (var pawn in pawnsInRange)
            {
                if (!ShouldAffectPawn(pawn))
                    continue;
                    
                if (IsPawnMoving(pawn))
                {
                    TryTeleportPawn(pawn);
                }
            }
        }

        /// <summary>
        /// 检查pawn是否应该受到传送效果影响
        /// </summary>
        private bool ShouldAffectPawn(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Destroyed)
                return false;

            // 种族类型检查 - 只允许人类和机械体
            if (!pawn.RaceProps.Humanlike && !pawn.RaceProps.IsMechanoid)
                return false;
                
            // 特定种族检查
            if (Props.allowedRaces != null && Props.allowedRaces.Count > 0)
            {
                if (!Props.allowedRaces.Contains(pawn.def))
                    return false;
            }
            
            // 排除种族检查
            if (Props.excludedRaces != null && Props.excludedRaces.Count > 0)
            {
                if (Props.excludedRaces.Contains(pawn.def))
                    return false;
            }
            
            // 派系关系检查 - 简化版，只检查是否同派系
            if (Props.onlyPawnsInSameFaction && parent.Faction != null && pawn.Faction != null)
            {
                if (parent.Faction != pawn.Faction)
                    return false;
            }
            
            // 囚犯和奴隶检查
            if (!Props.affectPrisoners && pawn.IsPrisoner)
                return false;
            if (!Props.affectSlaves && pawn.IsSlave)
                return false;
                
            return true;
        }

        /// <summary>
        /// 检查Pawn是否正在移动
        /// </summary>
        private bool IsPawnMoving(Pawn pawn)
        {
            if (pawn == null || pawn.jobs == null || pawn.jobs.curJob == null)
                return false;

            // 检查工作是否在排除表中
            if (IsJobExcluded(pawn.jobs.curJob.def))
                return false;
                
            // 检查是否正在执行移动任务
            if (pawn.jobs.curJob.def == JobDefOf.Goto || 
                pawn.jobs.curJob.def == JobDefOf.GotoWander ||
                pawn.jobs.curJob.def == JobDefOf.Follow)
            {
                return pawn.pather.Moving;
            }
            
            return false;
        }

        /// <summary>
        /// 检查工作是否在排除表中
        /// </summary>
        private bool IsJobExcluded(JobDef jobDef)
        {
            return ExcludedJobs.Contains(jobDef);
        }

        /// <summary>
        /// 尝试传送Pawn
        /// </summary>
        private void TryTeleportPawn(Pawn pawn)
        {
            try
            {
                if (pawn == null || pawn.Map == null || pawn.Destroyed)
                    return;

                // 检查当前工作是否在排除表中
                if (pawn.jobs?.curJob != null && IsJobExcluded(pawn.jobs.curJob.def))
                    return;
                    
                // 获取Pawn的目标位置
                LocalTargetInfo target = GetPawnMoveTarget(pawn);
                if (!target.IsValid)
                    return;
                    
                // 检查目标位置是否在传送网络范围内
                if (!IsPositionInNetworkRange(target.Cell))
                    return;
                    
                // 在网络范围内寻找安全的传送位置
                IntVec3 safeTarget = FindSafePositionInNetwork(target.Cell, pawn);
                if (!safeTarget.IsValid)
                    return;
                    
                // 执行传送
                PerformTeleport(pawn, safeTarget);
                
                // 记录日志
                WulaLog.Debug($"[AreaTeleporter] 传送 {pawn.LabelShort} 从 {pawn.Position} 到 {safeTarget} (网络传送)");
            }
            catch (System.Exception ex)
            {
                WulaLog.Debug($"传送Pawn时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取Pawn的移动目标
        /// </summary>
        private LocalTargetInfo GetPawnMoveTarget(Pawn pawn)
        {
            if (pawn.jobs?.curJob == null)
                return LocalTargetInfo.Invalid;

            // 检查工作是否在排除表中
            if (IsJobExcluded(pawn.jobs.curJob.def))
                return LocalTargetInfo.Invalid;
                
            // 尝试从当前工作中获取目标位置
            var job = pawn.jobs.curJob;
            
            // 对于Goto任务，目标通常是targetA
            if (job.targetA.IsValid)
                return job.targetA;
                
            // 对于其他移动任务，可能需要不同的逻辑
            if (job.def == JobDefOf.Follow && job.targetB.IsValid)
                return job.targetB;
                
            return LocalTargetInfo.Invalid;
        }

        /// <summary>
        /// 检查是否可以传送到指定位置
        /// </summary>
        private bool CanTeleportTo(IntVec3 cell, Map map)
        {
            if (!cell.InBounds(map))
                return false;

            // 检查是否可站立
            if (!cell.Standable(map))
                return false;

            // 检查是否有建筑阻挡
            Building edifice = cell.GetEdifice(map);
            if (edifice != null && edifice.def.surfaceType != SurfaceType.Item && 
                edifice.def.surfaceType != SurfaceType.Eat && !(edifice is Building_Door { Open: not false }))
            {
                return false;
            }

            // 检查是否有物品阻挡
            List<Thing> thingList = cell.GetThingList(map);
            for (int i = 0; i < thingList.Count; i++)
            {
                if (thingList[i].def.category == ThingCategory.Item)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 执行传送
        /// </summary>
        private void PerformTeleport(Pawn pawn, IntVec3 targetCell)
        {
            Map map = pawn.Map;
            
            // 创建入口特效
            if (Props.entryEffecter != null)
            {
                Effecter entryEffect = Props.entryEffecter.Spawn();
                entryEffect.Trigger(new TargetInfo(pawn.Position, map), new TargetInfo(pawn.Position, map));
                entryEffect.Cleanup();
            }
            
            // 创建出口特效
            if (Props.exitEffecter != null)
            {
                Effecter exitEffect = Props.exitEffecter.Spawn();
                exitEffect.Trigger(new TargetInfo(targetCell, map), new TargetInfo(targetCell, map));
                exitEffect.Cleanup();
            }
            
            // 播放音效
            if (Props.teleportSound != null)
            {
                Props.teleportSound.PlayOneShot(new TargetInfo(targetCell, map));
            }
            
            // 执行传送
            pawn.Position = targetCell;
            pawn.Notify_Teleported();
            pawn.jobs.StopAll();
            
            // 如果是玩家阵营，解除战争迷雾
            if ((pawn.Faction == Faction.OfPlayer || pawn.IsPlayerControlled) && pawn.Position.Fogged(map))
            {
                FloodFillerFog.FloodUnfog(pawn.Position, map);
            }
            
            // 传送后眩晕
            if (Props.stunTicks > 0)
            {
                pawn.stances.stunner.StunFor(Props.stunTicks, pawn, addBattleLog: false, showMote: false);
            }
            
            // 播放到达时的喧嚣效果
            if (Props.destClamorType != null)
            {
                GenClamor.DoClamor(pawn, targetCell, Props.destClamorRadius, Props.destClamorType);
            }
        }

        private void OnPawnEnterRange(Pawn pawn)
        {
            // Pawn进入范围时的处理
            if (Props.enterRangeEffecter != null)
            {
                Effecter effect = Props.enterRangeEffecter.Spawn();
                effect.Trigger(new TargetInfo(pawn.Position, pawn.Map), new TargetInfo(pawn.Position, pawn.Map));
                effect.Cleanup();
            }
        }

        private void OnPawnLeaveRange(Pawn pawn)
        {
            // Pawn离开范围时的处理
            if (Props.leaveRangeEffecter != null)
            {
                Effecter effect = Props.leaveRangeEffecter.Spawn();
                effect.Trigger(new TargetInfo(pawn.Position, pawn.Map), new TargetInfo(pawn.Position, pawn.Map));
                effect.Cleanup();
            }
        }

        /// <summary>
        /// 清理所有效果
        /// </summary>
        private void CleanupAllEffects()
        {
            pawnsInRange.Clear();
            
            foreach (var effecter in effecters.Values)
            {
                effecter.Cleanup();
            }
            effecters.Clear();
        }

        // 新增：切换开关状态
        private void ToggleEnabled()
        {
            bool oldEnabled = enabled;
            enabled = !enabled;
            
            if (oldEnabled != enabled)
            {
                if (enabled)
                {
                    RegisterToNetwork();
                    Messages.Message("WULA_TeleporterEnabled".Translate(parent.Label), parent, MessageTypeDefOf.PositiveEvent);
                }
                else
                {
                    UnregisterFromNetwork();
                    Messages.Message("WULA_TeleporterDisabled".Translate(parent.Label), parent, MessageTypeDefOf.NegativeEvent);
                }
                
                // 清理效果
                CleanupAllEffects();
            }
        }

        // 新增：获取Gizmos
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (var gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }

            // 只有满足科技需求时才显示开关按钮
            if (ShouldDisplayFunctionality && Props.canBeToggled)
            {
                yield return new Command_Toggle
                {
                    defaultLabel = enabled ? "WULA_TeleporterDisable".Translate() : "WULA_TeleporterEnable".Translate(),
                    defaultDesc = enabled ? "WULA_TeleporterDisableDesc".Translate() : "WULA_TeleporterEnableDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/Teleport"),
                    isActive = () => enabled,
                    toggleAction = ToggleEnabled
                };
            }
        }

        // 调试方法：显示传送范围
        public override void PostDraw()
        {
            base.PostDraw();
            
            // 只有满足科技需求且启用时才绘制范围
            if (!ShouldDisplayFunctionality || !enabled)
                return;
            
            if (Find.Selector.IsSelected(parent))
            {
                try
                {
                    // 绘制当前传送器范围
                    GenDraw.DrawRadiusRing(parent.Position, Props.teleportRadius, new Color(0.3f, 0.7f, 1, 0.3f));
                    
                    // 绘制网络范围（所有传送器的范围）
                    foreach (var teleporter in GetNetworkTeleporters())
                    {
                        if (teleporter != this && teleporter.parent.Spawned && teleporter.enabled && teleporter.ShouldDisplayFunctionality)
                        {
                            GenDraw.DrawRadiusRing(teleporter.parent.Position, teleporter.Props.teleportRadius, new Color(0.3f, 0.7f, 1, 0.3f));
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    WulaLog.Debug($"绘制传送范围环时出错: {ex.Message}");
                }
            }
        }

        // 获取当前范围内的pawn数量（用于显示）
        public int GetPawnsInRangeCount()
        {
            return pawnsInRange.Count;
        }
        
        // 获取范围内的pawn列表（用于调试）
        public IEnumerable<Pawn> GetPawnsInRange()
        {
            return pawnsInRange;
        }

        // 获取网络中的传送器数量
        public int GetNetworkTeleporterCount()
        {
            return GetNetworkTeleporters().Count;
        }
    }
}
