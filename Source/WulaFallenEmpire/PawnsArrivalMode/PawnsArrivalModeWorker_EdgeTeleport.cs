using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace WulaFallenEmpire
{
    public class PawnsArrivalModeWorker_EdgeTeleport : PawnsArrivalModeWorker
    {
        public override void Arrive(List<Pawn> pawns, IncidentParms parms)
        {
            Map map = (Map)parms.target;
            
            // 如果没有指定生成中心，则使用EdgeDrop的查找方式
            if (!parms.spawnCenter.IsValid)
            {
                parms.spawnCenter = DropCellFinder.FindRaidDropCenterDistant(map);
            }
            
            // 为每个Pawn分配一个传送位置（在生成中心附近）
            foreach (Pawn pawn in pawns)
            {
                if (pawn == null || pawn.Dead || pawn.Destroyed)
                    continue;
                    
                // 找到可用的传送位置
                IntVec3 teleportPos = FindTeleportPosition(map, parms.spawnCenter);
                
                // 如果Pawn已经在其他地图，需要先将其移到当前地图
                if (pawn.Map != map)
                {
                    // 确保Pawn不在任何地图中
                    if (pawn.Spawned)
                    {
                        pawn.DeSpawn();
                    }
                    
                    // 将Pawn放入当前地图
                    GenSpawn.Spawn(pawn, teleportPos, map, parms.spawnRotation);
                }
                else
                {
                    // 如果已经在当前地图，直接移动位置
                    pawn.Position = teleportPos;
                    pawn.Notify_Teleported(true, false);
                }
                
                // 播放传送效果
                PlayTeleportEffect(pawn, teleportPos, map);
                
                // 确保Pawn有适当的状态
                EnsurePawnStateAfterTeleport(pawn);
            }
        }

        public override bool TryResolveRaidSpawnCenter(IncidentParms parms)
        {
            Map map = (Map)parms.target;
            
            // 与EdgeDrop相同的方式解析生成中心
            if (!parms.spawnCenter.IsValid)
            {
                parms.spawnCenter = DropCellFinder.FindRaidDropCenterDistant(map);
            }
            
            parms.spawnRotation = Rot4.Random;
            return true;
        }

        /// <summary>
        /// 找到可用的传送位置
        /// </summary>
        private IntVec3 FindTeleportPosition(Map map, IntVec3 center)
        {
            // 在中心点附近寻找可用的单元格
            // 我们使用与EdgeWalkInGroups类似的逻辑，但立即传送
            if (CellFinder.TryFindRandomCellNear(center, map, 10, 
                c => c.Standable(map) && 
                     !c.Fogged(map) && 
                     c.GetRoof(map) != RoofDefOf.RoofRockThick && // 排除厚岩顶
                     map.reachability.CanReachColony(c), 
                out IntVec3 result))
            {
                return result;
            }
            
            // 如果找不到合适的单元格，使用备选方案
            if (RCellFinder.TryFindRandomPawnEntryCell(out result, map, CellFinder.EdgeRoadChance_Hostile))
            {
                return result;
            }
            
            // 最后的手段：使用中心点
            return center;
        }

        /// <summary>
        /// 播放传送效果
        /// </summary>
        private void PlayTeleportEffect(Pawn pawn, IntVec3 pos, Map map)
        {
            try
            {
                // 播放Skip_ExitNoDelay效果
                EffecterDef teleportEffect = DefDatabase<EffecterDef>.GetNamed("Skip_ExitNoDelay");
                if (teleportEffect != null)
                {
                    Effecter effecter = teleportEffect.Spawn();
                    effecter.ticksLeft = 30; // 设置效果持续时间
                    effecter.Trigger(new TargetInfo(pos, map), new TargetInfo(pos, map));
                    effecter.Cleanup();
                }
                else
                {
                    // 如果找不到指定的效果，使用跳跃效果
                    EffecterDef jumpEffect = EffecterDefOf.Skip_Exit;
                    if (jumpEffect != null)
                    {
                        Effecter effecter = jumpEffect.Spawn();
                        effecter.ticksLeft = 30;
                        effecter.Trigger(new TargetInfo(pos, map), new TargetInfo(pos, map));
                        effecter.Cleanup();
                    }
                }
                
                // 可选：播放声音效果
                SoundDef sound = SoundDefOf.PsychicPulseGlobal;
                if (sound != null)
                {
                    sound.PlayOneShot(new TargetInfo(pos, map));
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning($"Failed to play teleport effect for {pawn?.LabelCap}: {ex}");
            }
        }

        /// <summary>
        /// 确保Pawn传送后的状态
        /// </summary>
        private void EnsurePawnStateAfterTeleport(Pawn pawn)
        {
            if (pawn == null)
                return;
                
            // 重置当前工作
            if (pawn.CurJob != null)
            {
                pawn.jobs.StopAll();
            }
            
            // 如果是殖民者或友军，设置为等待战斗状态
            if (pawn.Faction == Faction.OfPlayer || !pawn.HostileTo(Faction.OfPlayer))
            {
                pawn.jobs.StartJob(new Job(JobDefOf.Wait_Combat, 600, true), 
                    JobCondition.InterruptForced, null, false, true, null, null, false);
            }
            
            // 重置心理状态
            if (pawn.mindState != null)
            {
                pawn.mindState.enemyTarget = null;
                pawn.mindState.mentalStateHandler?.Reset();
            }
            
            // 如果是机械族，可能需要特殊处理
            if (pawn.RaceProps.IsMechanoid)
            {
                var comp = pawn.GetComp<CompAutonomousMech>();
                if (comp != null)
                {
                    // 如果是自主机械，确保进入适当状态
                    if (!pawn.Drafted && pawn.Faction == Faction.OfPlayer)
                    {
                        pawn.drafter.Drafted = false;
                    }
                }
            }
        }
    }
}
