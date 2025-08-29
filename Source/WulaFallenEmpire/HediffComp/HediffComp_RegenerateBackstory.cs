using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace WulaFallenEmpire
{
    // HediffCompProperties，用于在XML中配置组件的属性
    public class HediffCompProperties_RegenerateBackstory : HediffCompProperties
    {
        public List<string> spawnCategories; // 存储背景故事类别的列表
        public bool regenerateChildhood = false; // 控制是否重新生成幼年时期故事，默认为false

        public HediffCompProperties_RegenerateBackstory()
        {
            this.compClass = typeof(HediffComp_RegenerateBackstory);
        }
    }

    // HediffComp的实际逻辑实现
    public class HediffComp_RegenerateBackstory : HediffComp
    {
        private HediffCompProperties_RegenerateBackstory Props => (HediffCompProperties_RegenerateBackstory)this.props;

        public override void CompPostMake()
        {
            base.CompPostMake();
            // 在Hediff被添加到Pawn时立即触发背景故事重新生成
            // 使用一个延迟操作，确保Pawn完全初始化后再修改其故事
            // 否则可能导致Pawn.story为null
            LongEventHandler.QueueLongEvent(RegenerateBackstory, "RegeneratingBackstory", false, null);
        }

        private void RegenerateBackstory()
        {
            Pawn pawn = this.parent.pawn;
            if (pawn == null || pawn.story == null)
            {
                Log.Warning($"[WulaFallenEmpire] HediffComp_RegenerateBackstory: Pawn or Pawn.story is null for hediff {this.parent.def.defName}. Cannot regenerate backstory.");
                return;
            }

            // 获取指定的背景故事类别
            List<string> categories = new List<string>();
            if (Props.spawnCategories != null && Props.spawnCategories.Any())
            {
                categories = Props.spawnCategories;
            }
            else
            {
                Log.Warning($"[WulaFallenEmpire] HediffComp_RegenerateBackstory: No spawnCategories specified for hediff {this.parent.def.defName}. Using all available categories.");
                categories = DefDatabase<BackstoryDef>.AllDefs.SelectMany(bs => bs.spawnCategories).Distinct().ToList(); // 如果没有指定类别，则使用所有类别
            }

            // 尝试重新生成背景故事
            BackstoryDef newChildhood = null;
            BackstoryDef newAdulthood = null;

            // 根据 regenerateChildhood 的值决定是否重新生成幼年时期故事
            if (Props.regenerateChildhood)
            {
                // 筛选符合类别的幼年背景故事
                List<BackstoryDef> availableChildhoodBackstories = DefDatabase<BackstoryDef>.AllDefsListForReading
                    .Where(bs => bs.slot == BackstorySlot.Childhood && bs.spawnCategories.Any(cat => categories.Contains(cat)))
                    .ToList();
                
                // 随机选择幼年背景故事
                if (availableChildhoodBackstories.Any())
                {
                    newChildhood = availableChildhoodBackstories.RandomElement();
                }
                else
                {
                    Log.Warning($"[WulaFallenEmpire] HediffComp_RegenerateBackstory: No childhood backstories found for categories: {string.Join(", ", Props.spawnCategories ?? new List<string>())}.");
                }
            } else
            {
                 // 如果 regenerateChildhood 为 false，则保留原有的幼年时期故事
                newChildhood = pawn.story.Childhood;
            }


            // 筛选符合类别的成年背景故事
            List<BackstoryDef> availableAdulthoodBackstories = DefDatabase<BackstoryDef>.AllDefsListForReading
                .Where(bs => bs.slot == BackstorySlot.Adulthood && bs.spawnCategories.Any(cat => categories.Contains(cat)))
                .ToList();

            // 随机选择成年背景故事
            if (availableAdulthoodBackstories.Any())
            {
                newAdulthood = availableAdulthoodBackstories.RandomElement();
            }
            else
            {
                Log.Warning($"[WulaFallenEmpire] HediffComp_RegenerateBackstory: No adulthood backstories found for categories: {string.Join(", ", Props.spawnCategories ?? new List<string>())}.");
            }

            // 应用新的背景故事
            if (newChildhood != null || newAdulthood != null)
            {
                pawn.story.Childhood = newChildhood;
                pawn.story.Adulthood = newAdulthood;
                Log.Message($"[WulaFallenEmpire] Regenerated backstory for {pawn.NameShortColored}: Childhood='{newChildhood?.title ?? "None"}', Adulthood='{newAdulthood?.title ?? "None"}'.");
            }
            else
            {
                Log.Warning($"[WulaFallenEmpire] HediffComp_RegenerateBackstory: Failed to find any suitable backstories for {pawn.NameShortColored} with categories: {string.Join(", ", Props.spawnCategories ?? new List<string>())}.");
            }

            // 删除当前的Hediff
            pawn.health.RemoveHediff(this.parent);
            Log.Message($"[WulaFallenEmpire] Removed hediff {this.parent.def.defName} from {pawn.NameShortColored} after backstory regeneration.");
        }
    }
}