using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire.EventSystem.AI.Agent
{
    /// <summary>
    /// 游戏状态观察器 - 收集当前游戏状态用于 AI 决策
    /// </summary>
    public static class StateObserver
    {
        /// <summary>
        /// 捕获当前游戏状态快照
        /// </summary>
        public static GameStateSnapshot CaptureState()
        {
            var snapshot = new GameStateSnapshot();
            
            try
            {
                Map map = Find.CurrentMap ?? Find.Maps?.FirstOrDefault();
                if (map == null)
                {
                    WulaLog.Debug("[StateObserver] No map available");
                    return snapshot;
                }
                
                // 时间信息
                CaptureTimeInfo(snapshot);
                
                // 环境信息
                CaptureEnvironmentInfo(snapshot, map);
                
                // 殖民者信息
                CaptureColonistInfo(snapshot, map);
                
                // 资源统计
                CaptureResourceInfo(snapshot, map);
                
                // 建筑信息
                CaptureBuildingInfo(snapshot, map);
                
                // 威胁检测
                CaptureThreatInfo(snapshot, map);
                
                // 最近消息
                CaptureRecentMessages(snapshot);
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[StateObserver] Error capturing state: {ex.Message}");
            }
            
            return snapshot;
        }
        
        private static void CaptureTimeInfo(GameStateSnapshot snapshot)
        {
            try
            {
                var tickManager = Find.TickManager;
                if (tickManager == null) return;
                
                int ticksAbs = Find.TickManager.TicksAbs;
                int tile = Find.CurrentMap?.Tile ?? 0;
                float longitude = Find.WorldGrid.LongLatOf(tile).x;
                snapshot.Hour = GenDate.HourOfDay(ticksAbs, longitude);
                snapshot.DayOfQuadrum = GenDate.DayOfQuadrum(ticksAbs, longitude) + 1;
                snapshot.Year = GenDate.Year(ticksAbs, longitude);
                snapshot.Season = GenDate.Season(ticksAbs, Find.WorldGrid.LongLatOf(tile)).Label();
                snapshot.GameSpeedMultiplier = Find.TickManager.TickRateMultiplier;
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[StateObserver] Error capturing time: {ex.Message}");
            }
        }
        
        private static void CaptureEnvironmentInfo(GameStateSnapshot snapshot, Map map)
        {
            try
            {
                snapshot.BiomeName = map.Biome?.label ?? "Unknown";
                snapshot.OutdoorTemperature = map.mapTemperature?.OutdoorTemp ?? 20f;
                snapshot.Weather = map.weatherManager?.curWeather?.label ?? "Unknown";
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[StateObserver] Error capturing environment: {ex.Message}");
            }
        }
        
        private static void CaptureColonistInfo(GameStateSnapshot snapshot, Map map)
        {
            try
            {
                var colonists = map.mapPawns?.FreeColonists;
                if (colonists == null) return;
                
                foreach (var pawn in colonists)
                {
                    if (pawn == null || pawn.Dead) continue;
                    
                    var pawnSnapshot = new PawnSnapshot
                    {
                        Name = pawn.LabelShortCap,
                        HealthPercent = pawn.health?.summaryHealth?.SummaryHealthPercent ?? 1f,
                        MoodPercent = pawn.needs?.mood?.CurLevelPercentage ?? 0.5f,
                        IsDrafted = pawn.Drafted,
                        IsDowned = pawn.Downed,
                        CurrentJob = GetJobDescription(pawn),
                        X = pawn.Position.x,
                        Z = pawn.Position.z
                    };
                    
                    snapshot.Colonists.Add(pawnSnapshot);
                }
                
                // 囚犯
                var prisoners = map.mapPawns?.PrisonersOfColony;
                if (prisoners != null)
                {
                    foreach (var pawn in prisoners)
                    {
                        if (pawn == null || pawn.Dead) continue;
                        snapshot.Prisoners.Add(new PawnSnapshot
                        {
                            Name = pawn.LabelShortCap,
                            HealthPercent = pawn.health?.summaryHealth?.SummaryHealthPercent ?? 1f,
                            IsDowned = pawn.Downed
                        });
                    }
                }
                
                // 驯养动物
                var animals = map.mapPawns?.SpawnedColonyAnimals;
                if (animals != null)
                {
                    snapshot.Animals.AddRange(animals.Take(20).Select(a => new PawnSnapshot
                    {
                        Name = a.LabelShortCap,
                        HealthPercent = a.health?.summaryHealth?.SummaryHealthPercent ?? 1f
                    }));
                }
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[StateObserver] Error capturing colonists: {ex.Message}");
            }
        }
        
        private static string GetJobDescription(Pawn pawn)
        {
            try
            {
                if (pawn.Drafted) return "已征召";
                if (pawn.Downed) return "倒地";
                if (pawn.InMentalState) return $"精神崩溃: {pawn.MentalStateDef?.label ?? "未知"}";
                
                var job = pawn.CurJob;
                if (job == null) return "空闲";
                
                // 返回简短的工作描述
                string jobLabel = job.def?.reportString ?? job.def?.label ?? "工作中";
                
                // 清理一些常见的格式
                if (job.targetA.Thing != null)
                {
                    jobLabel = $"{job.def?.label}: {job.targetA.Thing.LabelShortCap}";
                }
                else
                {
                    jobLabel = job.def?.label ?? "工作中";
                }
                
                return jobLabel;
            }
            catch
            {
                return "未知";
            }
        }
        
        private static void CaptureResourceInfo(GameStateSnapshot snapshot, Map map)
        {
            try
            {
                var resourceCounter = map.resourceCounter;
                if (resourceCounter == null) return;
                
                foreach (var kvp in resourceCounter.AllCountedAmounts)
                {
                    if (kvp.Value > 0)
                    {
                        snapshot.Resources[kvp.Key.defName] = kvp.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[StateObserver] Error capturing resources: {ex.Message}");
            }
        }
        
        private static void CaptureBuildingInfo(GameStateSnapshot snapshot, Map map)
        {
            try
            {
                // 统计建筑
                snapshot.TotalBuildings = map.listerBuildings?.allBuildingsColonist?.Count ?? 0;
                
                // 统计蓝图
                snapshot.PendingBlueprints = map.listerThings?.ThingsInGroup(ThingRequestGroup.Blueprint)?.Count ?? 0;
                
                // 统计建造中的框架
                snapshot.ConstructionFrames = map.listerThings?.ThingsInGroup(ThingRequestGroup.BuildingFrame)?.Count ?? 0;
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[StateObserver] Error capturing buildings: {ex.Message}");
            }
        }
        
        private static void CaptureThreatInfo(GameStateSnapshot snapshot, Map map)
        {
            try
            {
                // 检测敌对 pawns
                var hostilePawns = map.mapPawns?.AllPawnsSpawned?
                    .Where(p => p != null && !p.Dead && p.HostileTo(Faction.OfPlayer))
                    .ToList();
                
                if (hostilePawns == null || hostilePawns.Count == 0) return;
                
                // 按派系分组
                var threatGroups = hostilePawns.GroupBy(p => p.Faction?.Name ?? "野生动物");
                
                foreach (var group in threatGroups)
                {
                    IntVec3 colonyCenter = map.IsPlayerHome ? map.mapPawns.FreeColonists.FirstOrDefault()?.Position ?? IntVec3.Zero : IntVec3.Zero;
                    float minDistance = group.Min(p => (p.Position - colonyCenter).LengthHorizontal);
                    
                    snapshot.Threats.Add(new ThreatSnapshot
                    {
                        Description = $"{group.Count()} 个 {group.Key}",
                        Count = group.Count(),
                        Distance = minDistance,
                        IsHostile = true
                    });
                }
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[StateObserver] Error capturing threats: {ex.Message}");
            }
        }
        
        private static void CaptureRecentMessages(GameStateSnapshot snapshot)
        {
            try
            {
                var messages = Find.Archive?.ArchivablesListForReading;
                if (messages == null) return;
                
                // 获取最近 5 条消息
                var allMessages = messages.OfType<Message>().ToList();
                var recentMessages = allMessages
                    .Skip(Math.Max(0, allMessages.Count - 5))
                    .Select(m => m.text?.ToString() ?? "")
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();
                
                snapshot.RecentMessages.AddRange(recentMessages);
            }
            catch (Exception ex)
            {
                WulaLog.Debug($"[StateObserver] Error capturing messages: {ex.Message}");
            }
        }
    }
}
