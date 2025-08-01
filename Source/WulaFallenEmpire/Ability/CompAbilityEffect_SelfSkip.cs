using Verse;
using RimWorld;

namespace WulaFallenEmpire
{
    public class CompAbilityEffect_SelfSkip : CompAbilityEffect_Teleport
    {
        public override void Start(AbilityPawn p, LocalTargetInfo target)
        {
            // 强制将传送目标设置为施法者本人
            base.SetTarget(new LocalTargetInfo(this.parent.pawn));
            // 然后正常开始选择目的地
            base.SelectDestination();
        }
    }
}