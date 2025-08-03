using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace WulaFallenEmpire
{
    // NOTE: The PsychicRitualDef_Wula class has been removed as it's no longer needed.
    // We are now using a DefModExtension for filtering, which is a much cleaner approach.

    /// <summary>
    /// Custom CompProperties for our ritual spot, with a tag.
    /// </summary>
    public class CompProperties_WulaRitualSpot : CompProperties
    {
        public string ritualTag;

        public CompProperties_WulaRitualSpot()
        {
            this.compClass = typeof(CompWulaRitualSpot);
        }
    }

    /// <summary>
    /// The core component for the custom ritual spot. Generates its own gizmos
    /// by filtering for rituals with a matching tag via a DefModExtension.
    /// </summary>
    public class CompWulaRitualSpot : ThingComp
    {
        public CompProperties_WulaRitualSpot Props => (CompProperties_WulaRitualSpot)this.props;

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }

            // Find all rituals that have our custom mod extension and a matching tag
            foreach (PsychicRitualDef ritualDef in DefDatabase<PsychicRitualDef>.AllDefsListForReading)
            {
                var extension = ritualDef.GetModExtension<RitualTagExtension>();
                if (extension != null && extension.ritualTag == this.Props.ritualTag)
                {
                    Command_Action command_Action = new Command_Action();
                    command_Action.defaultLabel = ritualDef.LabelCap;
                    command_Action.defaultDesc = ritualDef.description;
                    command_Action.icon = ritualDef.uiIcon;
                    command_Action.action = delegate
                    {
                        // Mimic vanilla initialization
                        TargetInfo target = new TargetInfo(this.parent);
                        PsychicRitualRoleAssignments assignments = ritualDef.BuildRoleAssignments(target);
                        PsychicRitualCandidatePool candidatePool = ritualDef.FindCandidatePool();
                        ritualDef.InitializeCast(this.parent.Map);
                        Find.WindowStack.Add(new Dialog_BeginPsychicRitual(ritualDef, candidatePool, assignments, this.parent.Map));
                    };

                    // Corrected check for cooldown and other requirements
                    AcceptanceReport acceptanceReport = Find.PsychicRitualManager.CanInvoke(ritualDef, this.parent.Map);
                    if (!acceptanceReport.Accepted)
                    {
                        command_Action.Disable(acceptanceReport.Reason.CapitalizeFirst());
                    }
                    
                    yield return command_Action;
                }
            }
        }
    }
}