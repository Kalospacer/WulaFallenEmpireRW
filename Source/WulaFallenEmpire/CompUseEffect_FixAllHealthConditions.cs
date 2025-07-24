using Verse;
using RimWorld;
using System.Collections.Generic; // Added for List

namespace WulaFallenEmpire
{
    public class CompUseEffect_FixAllHealthConditions : CompUseEffect
    {
        public override AcceptanceReport CanBeUsedBy(Pawn p)
        {
            // 检查是否有可修复的健康状况。
            // 注意：这里的 'p' 是尝试使用物品的小人（如果直接使用）或被施用物品的小人（如果通过配方施用）。
            // 种族检查将在 DoEffect 中进行，以允许任何种族的小人尝试使用（但只有乌拉族会生效）。
            if (!HealthUtility.TryGetWorstHealthCondition(p, out var _, out var _))
            {
                return "AbilityCannotCastNoHealableInjury".Translate(p.Named("PAWN")).Resolve().StripTags() ?? "";
            }
            return true;
        }

        public override void DoEffect(Pawn usedBy)
        {
            base.DoEffect(usedBy);

            // 检查被施用物品的小人是否是乌拉族。
            if (usedBy.def.defName != "WulaSpecies")
            {
                if (PawnUtility.ShouldSendNotificationAbout(usedBy))
                {
                    Messages.Message("WULA_MechSerumHealerNotWula".Translate(usedBy.Named("PAWN")).Resolve().StripTags() ?? "", usedBy, MessageTypeDefOf.NegativeEvent);
                }
                return; // 如果不是乌拉族，则不执行修复。
            }

            int fixedCount = 0;
            // List<Hediff> fixedHediffs = new List<Hediff>(); // 此行不再需要，因为我们只计数

            // 持续修复最严重的健康状况，直到没有更多可修复的状况。
            // HealthUtility.FixWorstHealthCondition 如果没有修复任何状况，会返回 null。
            while (HealthUtility.TryGetWorstHealthCondition(usedBy, out var hediffToFix, out var partToFix))
            {
                int initialHediffCount = usedBy.health.hediffSet.hediffs.Count;
                
                TaggedString currentFixedMessage = HealthUtility.FixWorstHealthCondition(usedBy);
                
                // 如果返回了消息，或者 Hediff 数量减少，或者特定的 Hediff 不再存在，则表示已修复。
                if (currentFixedMessage != null || usedBy.health.hediffSet.hediffs.Count < initialHediffCount || !usedBy.health.hediffSet.hediffs.Contains(hediffToFix))
                {
                    fixedCount++;
                }
                else
                {
                    // 如果 FixWorstHealthCondition 返回 null 且未检测到变化，
                    // 则表示此方法无法再修复更多状况。跳出循环。
                    break;
                }
            }

            if (fixedCount > 0 && PawnUtility.ShouldSendNotificationAbout(usedBy))
            {
                Messages.Message("WULA_MechSerumHealerAllFixed".Translate(usedBy.Named("PAWN")), usedBy, MessageTypeDefOf.PositiveEvent);
            }
            else if (fixedCount == 0 && PawnUtility.ShouldSendNotificationAbout(usedBy))
            {
                Messages.Message("WULA_MechSerumHealerNoConditionsFixed".Translate(usedBy.Named("PAWN")), usedBy, MessageTypeDefOf.NegativeEvent);
            }
        }
    }
}
