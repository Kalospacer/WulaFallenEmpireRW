using RimWorld;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    public class MentalBreakWorker_BrokenPersonality : MentalBreakWorker
    {
        public override bool TryStart(Pawn pawn, string reason, bool causedByMood)
        {
            // 先尝试启动精神状态
            if (base.TryStart(pawn, reason, causedByMood))
            {
                // 成功启动后，执行附加逻辑
                var extension = def.mentalState.GetModExtension<MentalStateDefExtension_BrokenPersonality>();
                if (extension != null && extension.traitToAdd != null && !pawn.story.traits.HasTrait(extension.traitToAdd))
                {
                    pawn.story.traits.GainTrait(new Trait(extension.traitToAdd));
                }

                return true;
            }
            return false;
        }
    }
}
