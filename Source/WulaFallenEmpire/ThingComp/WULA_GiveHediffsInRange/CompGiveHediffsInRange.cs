using RimWorld;
using Verse;
using System.Collections.Generic;
using UnityEngine;

namespace WulaFallenEmpire
{
    public class CompGiveHediffsInRange : ThingComp
    {
        private CompProperties_GiveHediffsInRange Props => (CompProperties_GiveHediffsInRange)props;
        
        // 跟踪受影响的pawn和他们的Hediff
        private Dictionary<Pawn, Hediff> affectedPawns = new Dictionary<Pawn, Hediff>();
        
        // 效果实例缓存
        private Dictionary<Pawn, Effecter> effecters = new Dictionary<Pawn, Effecter>();
        
        public override void CompTick()
        {
            base.CompTick();
            
            if (parent == null || !parent.Spawned || parent.Map == null)
                return;
                
            // 使用间隔检查优化性能
            if (Find.TickManager.TicksGame % Props.checkIntervalTicks != 0)
                return;
                
            UpdateAffectedPawns();
        }

        private void UpdateAffectedPawns()
        {
            if (Props.hediff == null) return;

            // 获取范围内的所有pawn
            List<Pawn> pawnsInRange = GetPawnsInRange();
            
            // 处理新进入范围的pawn
            foreach (var pawn in pawnsInRange)
            {
                if (!affectedPawns.ContainsKey(pawn) && ShouldAffectPawn(pawn))
                {
                    AddHediffToPawn(pawn);
                }
            }
            
            // 处理离开范围的pawn
            var pawnsToRemove = new List<Pawn>();
            foreach (var kvp in affectedPawns)
            {
                if (!pawnsInRange.Contains(kvp.Key) || !ShouldAffectPawn(kvp.Key))
                {
                    pawnsToRemove.Add(kvp.Key);
                }
                else
                {
                    // 更新持续时间的Hediff
                    UpdateHediffDuration(kvp.Key, kvp.Value);
                }
            }
            
            foreach (var pawn in pawnsToRemove)
            {
                RemoveHediffFromPawn(pawn);
            }
        }

        private List<Pawn> GetPawnsInRange()
        {
            var pawns = new List<Pawn>();
            if (parent?.Map == null) return pawns;

            var map = parent.Map;
            var center = parent.Position;
            
            // 使用网格搜索优化性能
            foreach (var thing in map.listerThings.ThingsInGroup(ThingRequestGroup.Pawn))
            {
                if (thing is Pawn pawn && 
                    pawn.Position.DistanceTo(center) <= Props.range &&
                    pawn.Spawned)
                {
                    pawns.Add(pawn);
                }
            }
            
            return pawns;
        }

        /// <summary>
        /// 检查pawn是否应该受到效果影响
        /// </summary>
        private bool ShouldAffectPawn(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Destroyed)
                return false;
                
            // 派系检查
            if (Props.onlyPawnsInSameFaction && pawn.Faction != parent.Faction)
                return false;
                
            // 种族类型检查
            if (Props.requireHumanlike && !pawn.RaceProps.Humanlike)
                return false;
            if (Props.requireAnimal && !pawn.RaceProps.Animal)
                return false;
            if (Props.requireMechanoid && !pawn.RaceProps.IsMechanoid)
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
            
            // 修复：安全的派系关系检查
            if (parent.Faction != null && pawn.Faction != null)
            {
                // 避免检查同一派系与自身的关系
                if (parent.Faction == pawn.Faction)
                {
                    // 同一派系，视为盟友
                    if (!Props.affectAllies)
                        return false;
                }
                else
                {
                    try
                    {
                        FactionRelationKind relation = parent.Faction.RelationKindWith(pawn.Faction);
                        
                        if (!Props.affectAllies && relation == FactionRelationKind.Ally)
                            return false;
                        if (!Props.affectEnemies && relation == FactionRelationKind.Hostile)
                            return false;
                        if (!Props.affectNeutrals && relation == FactionRelationKind.Neutral)
                            return false;
                    }
                    catch (System.Exception ex)
                    {
                        Log.Warning($"检查派系关系时出错: {ex.Message}");
                        // 出错时保守处理，不施加效果
                        return false;
                    }
                }
            }
            else
            {
                // 如果建筑或pawn没有派系，根据设置处理
                if (parent.Faction == null && pawn.Faction == null)
                {
                    // 两者都没有派系，视为中立
                    if (!Props.affectNeutrals)
                        return false;
                }
                else if (pawn.Faction == null)
                {
                    // pawn没有派系（野生动物等），视为中立
                    if (!Props.affectNeutrals)
                        return false;
                }
            }
            
            // 囚犯和奴隶检查
            if (!Props.affectPrisoners && pawn.IsPrisoner)
                return false;
            if (!Props.affectSlaves && pawn.IsSlave)
                return false;
                
            return true;
        }

        private void AddHediffToPawn(Pawn pawn)
        {
            try
            {
                // 检查pawn是否已经有这个Hediff
                Hediff existingHediff = pawn.health.hediffSet.GetFirstHediffOfDef(Props.hediff);
                if (existingHediff != null)
                {
                    // 如果已经有相同的Hediff，更新它而不是创建新的
                    affectedPawns[pawn] = existingHediff;
                    UpdateHediffDuration(pawn, existingHediff);
                    return;
                }

                // 添加Hediff
                Hediff newHediff = HediffMaker.MakeHediff(Props.hediff, pawn);
                newHediff.Severity = Props.initialSeverity;
                
                // 设置持续时间（如果有）
                if (Props.hediffDurationTicks > 0)
                {
                    var comp = newHediff.TryGetComp<HediffComp_Disappears>();
                    if (comp != null)
                    {
                        comp.ticksToDisappear = Props.hediffDurationTicks;
                    }
                }
                
                pawn.health.AddHediff(newHediff);
                affectedPawns[pawn] = newHediff;
                
                // 创建视觉效果
                CreateEffectForPawn(pawn);
                
                // 记录日志（可选，调试时使用）
                // Log.Message($"给予 {pawn.LabelShort} ({pawn.def.defName}) Hediff: {Props.hediff.defName}");
            }
            catch (System.Exception ex)
            {
                Log.Warning($"无法给 {pawn.LabelShort} 添加Hediff: {ex.Message}");
            }
        }

        private void RemoveHediffFromPawn(Pawn pawn)
        {
            if (affectedPawns.TryGetValue(pawn, out var hediff))
            {
                try
                {
                    // 安全地移除Hediff
                    if (pawn.health.hediffSet.hediffs.Contains(hediff))
                    {
                        pawn.health.RemoveHediff(hediff);
                    }
                }
                catch (System.Exception ex)
                {
                    Log.Warning($"移除 {pawn.LabelShort} 的Hediff时出错: {ex.Message}");
                }
                
                affectedPawns.Remove(pawn);
            }
            
            // 移除视觉效果
            RemoveEffectForPawn(pawn);
        }

        private void UpdateHediffDuration(Pawn pawn, Hediff hediff)
        {
            if (Props.hediffDurationTicks > 0)
            {
                var comp = hediff.TryGetComp<HediffComp_Disappears>();
                if (comp != null)
                {
                    // 重置持续时间，让效果持续
                    comp.ticksToDisappear = Props.hediffDurationTicks;
                }
            }
        }

        private void CreateEffectForPawn(Pawn pawn)
        {
            if (Props.mote != null)
            {
                try
                {
                    // 创建持续的视觉效果
                    var effecterDef = DefDatabase<EffecterDef>.GetNamedSilentFail(Props.mote.defName + "_Effecter");
                    if (effecterDef != null)
                    {
                        var effecter = effecterDef.Spawn();
                        effecters[pawn] = effecter;
                    }
                    else
                    {
                        // 如果没有对应的EffecterDef，直接创建Mote
                        MoteMaker.MakeStaticMote(pawn.Position, pawn.Map, Props.mote, 1f);
                    }
                }
                catch (System.Exception ex)
                {
                    Log.Warning($"创建视觉效果时出错: {ex.Message}");
                }
            }
        }

        private void RemoveEffectForPawn(Pawn pawn)
        {
            if (effecters.TryGetValue(pawn, out var effecter))
            {
                try
                {
                    effecter.Cleanup();
                }
                catch (System.Exception ex)
                {
                    Log.Warning($"清理视觉效果时出错: {ex.Message}");
                }
                effecters.Remove(pawn);
            }
        }

        public override void CompTickRare()
        {
            base.CompTickRare();
            
            // 更新视觉效果
            var effectersToRemove = new List<Pawn>();
            foreach (var kvp in effecters)
            {
                if (kvp.Key != null && kvp.Key.Spawned && affectedPawns.ContainsKey(kvp.Key))
                {
                    try
                    {
                        kvp.Value.EffectTick(kvp.Key, kvp.Key);
                    }
                    catch (System.Exception ex)
                    {
                        Log.Warning($"更新视觉效果时出错: {ex.Message}");
                        effectersToRemove.Add(kvp.Key);
                    }
                }
                else
                {
                    effectersToRemove.Add(kvp.Key);
                }
            }
            
            // 清理无效的效果器
            foreach (var pawn in effectersToRemove)
            {
                RemoveEffectForPawn(pawn);
            }
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map);
            CleanupAllEffects();
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            CleanupAllEffects();
        }

        /// <summary>
        /// 清理所有效果
        /// </summary>
        private void CleanupAllEffects()
        {
            // 移除所有Hediff
            var pawnsToRemove = new List<Pawn>(affectedPawns.Keys);
            foreach (var pawn in pawnsToRemove)
            {
                RemoveHediffFromPawn(pawn);
            }
            
            affectedPawns.Clear();
            
            // 清理所有视觉效果
            var effectersToRemove = new List<Pawn>(effecters.Keys);
            foreach (var pawn in effectersToRemove)
            {
                RemoveEffectForPawn(pawn);
            }
            
            effecters.Clear();
        }

        // 调试方法：显示影响范围
        public override void PostDraw()
        {
            base.PostDraw();
            
            if (Find.Selector.IsSelected(parent))
            {
                try
                {
                    GenDraw.DrawRadiusRing(parent.Position, Props.range, Color.green);
                }
                catch (System.Exception ex)
                {
                    Log.Warning($"绘制范围环时出错: {ex.Message}");
                }
            }
        }

        // 获取当前受影响的pawn数量（用于显示）
        public int GetAffectedPawnCount()
        {
            return affectedPawns.Count;
        }
        
        // 获取受影响的pawn列表（用于调试）
        public IEnumerable<Pawn> GetAffectedPawns()
        {
            return affectedPawns.Keys;
        }
    }
}
