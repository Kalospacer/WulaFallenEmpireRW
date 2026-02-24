using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class CompProperties_AbilityRequiresNonHostility : CompProperties_AbilityEffect
    {
        public FactionDef factionDef;
        
        public CompProperties_AbilityRequiresNonHostility()
        {
            compClass = typeof(CompAbilityEffect_RequiresNonHostility);
        }
    }

    public class CompAbilityEffect_RequiresNonHostility : CompAbilityEffect
    {
        public new CompProperties_AbilityRequiresNonHostility Props => (CompProperties_AbilityRequiresNonHostility)props;
        
        public override bool GizmoDisabled(out string reason)
        {
            Pawn pawn = parent.pawn;
            if (pawn == null || pawn.Faction == null || Props.factionDef == null)
            {
                reason = null;
                return false;
            }
            
            // 查找指定派系
            Faction targetFaction = Find.FactionManager.FirstFactionOfDef(Props.factionDef);
            if (targetFaction == null)
            {
                reason = null;
                return false;
            }
            
            // 检查是否敌对
            if (pawn.Faction.HostileTo(targetFaction))
            {
                reason = "WULA_AbilityRequiresNonHostility".Translate(Props.factionDef);
                return true;
            }
            
            reason = null;
            return false;
        }
    }
}
