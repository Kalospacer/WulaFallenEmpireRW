using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class CompAbilityEffect_GiveSwitchHediff : CompAbilityEffect
    {
        public new CompProperties_AbilityGiveHediff Props => (CompProperties_AbilityGiveHediff)props;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            if (Props.hediffDef != null)
            {
                parent.pawn.health.AddHediff(Props.hediffDef);
            }
        }

        public override bool ShouldHideGizmo
        {
            get
            {
                // 如果父级Pawn已经有了这个Hediff，就隐藏“给予”按钮
                if (parent.pawn?.health.hediffSet.HasHediff(Props.hediffDef) ?? false)
                {
                    return true;
                }
                return base.ShouldHideGizmo;
            }
        }
    }
}