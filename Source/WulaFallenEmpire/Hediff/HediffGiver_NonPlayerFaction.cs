using RimWorld;
using Verse;
using System.Collections.Generic;

namespace WulaFallenEmpire
{
    public class HediffGiver_NonPlayerFaction : HediffGiver
    {
        // 显式重新定义这些字段，确保 XML 解析器能找到它们
        public float mtbDays;
        public new HediffDef hediff;
        
        // 新增：目标派系列表
        public List<FactionDef> targetFactions;
        
        // 新增：是否排除玩家派系（默认为true）
        public bool excludePlayerFaction = true;
        
        // 新增：是否排除囚犯（默认为true）
        public bool excludePrisoners = true;

        // 新增：记录已经处理过的 pawn，避免重复处理
        private HashSet<Pawn> processedPawns = new HashSet<Pawn>();

        public override void OnIntervalPassed(Pawn pawn, Hediff cause)
        {
            // 检查 pawn 是否已经处理过
            if (processedPawns.Contains(pawn))
            {
                // 如果已经处理过，检查 hediff 是否还存在
                Hediff existingHediff = pawn.health?.hediffSet?.GetFirstHediffOfDef(this.hediff);
                if (existingHediff != null)
                {
                    // hediff 还存在，不需要再次处理
                    return;
                }
                else
                {
                    // hediff 被移除了，从记录中移除这个 pawn
                    processedPawns.Remove(pawn);
                }
            }

            // 检查派系条件
            if (ShouldHaveHediff(pawn))
            {
                // 检查是否已经有这个 hediff
                Hediff existing = pawn.health?.hediffSet?.GetFirstHediffOfDef(this.hediff);
                if (existing == null)
                {
                    // 给予 hediff
                    HealthUtility.AdjustSeverity(pawn, this.hediff, 1.0f);
                    // 标记为已处理
                    processedPawns.Add(pawn);
                    Log.Message($"Added hediff {this.hediff.defName} to pawn {pawn.Label}");
                }
            }
            else
            {
                // 移除 hediff
                if (RemoveHediffIfExists(pawn))
                {
                    // 如果成功移除了 hediff，也从记录中移除
                    processedPawns.Remove(pawn);
                }
            }
        }

        private bool ShouldHaveHediff(Pawn pawn)
        {
            // 检查派系是否存在
            if (pawn.Faction == null)
                return false;
                
            // 检查是否排除玩家派系
            if (excludePlayerFaction && pawn.Faction == Faction.OfPlayer)
                return false;
                
            // 检查是否排除囚犯
            if (excludePrisoners && pawn.IsPrisonerOfColony)
                return false;
            
            // 检查目标派系
            if (targetFactions != null && targetFactions.Count > 0)
            {
                // 如果指定了目标派系，只给这些派系添加 hediff
                return targetFactions.Contains(pawn.Faction.def);
            }
            else
            {
                // 如果没有指定目标派系，保持原来的行为：给所有非玩家派系添加
                return pawn.Faction != Faction.OfPlayer;
            }
        }

        private bool RemoveHediffIfExists(Pawn pawn)
        {
            Hediff existing = pawn.health?.hediffSet?.GetFirstHediffOfDef(this.hediff);
            if (existing != null)
            {
                pawn.health.RemoveHediff(existing);
                Log.Message($"Removed hediff {this.hediff.defName} from pawn {pawn.Label}");
                return true;
            }
            return false;
        }

        // 新增：在 pawn 死亡或被销毁时清理记录
        public override void Notify_PawnDied(Pawn pawn, DamageInfo? dinfo)
        {
            base.Notify_PawnDied(pawn, dinfo);
            processedPawns.Remove(pawn);
        }

        public override void Notify_PawnDespawned(Pawn pawn, Map map)
        {
            base.Notify_PawnDespawned(pawn, map);
            processedPawns.Remove(pawn);
        }
    }
}
