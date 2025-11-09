using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class CompProperties_AbilityResearchPrereq : CompProperties_AbilityEffect
    {
        public ResearchProjectDef requiredResearch;

        public CompProperties_AbilityResearchPrereq()
        {
            compClass = typeof(CompAbilityEffect_ResearchPrereq);
        }
    }

    public class CompAbilityEffect_ResearchPrereq : CompAbilityEffect
    {
        public new CompProperties_AbilityResearchPrereq Props => (CompProperties_AbilityResearchPrereq)props;

        public override bool GizmoDisabled(out string reason)
        {
            if (Props.requiredResearch != null && !Props.requiredResearch.IsFinished)
            {
                reason = "WULA_ResearchRequired".Translate(Props.requiredResearch.LabelCap);
                return true;
            }

            reason = null;
            return false;
        }
    }
}