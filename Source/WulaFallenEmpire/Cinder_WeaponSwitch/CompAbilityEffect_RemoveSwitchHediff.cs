using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class CompAbilityEffect_RemoveSwitchHediff : CompAbilityEffect
    {
        public new CompProperties_AbilityRemoveHediff Props => (CompProperties_AbilityRemoveHediff)props;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            Hediff firstHediffOfDef = parent.pawn.health.hediffSet.GetFirstHediffOfDef(Props.hediffDef);
            if (firstHediffOfDef != null)
            {
                parent.pawn.health.RemoveHediff(firstHediffOfDef);
            }
        }

        public override bool ShouldHideGizmo
        {
            get
            {
                // 如果父级Pawn没有这个Hediff，就隐藏“移除”按钮
                if (!parent.pawn?.health.hediffSet.HasHediff(Props.hediffDef) ?? true)
                {
                    return true;
                }
                return base.ShouldHideGizmo;
            }
        }
    }
}