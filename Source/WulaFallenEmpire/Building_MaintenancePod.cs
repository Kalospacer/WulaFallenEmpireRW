using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class CompProperties_MaintenanceCycle : CompProperties_BiosculpterPod_BaseCycle
    {
        public HediffDef hediffToRemove;

        public CompProperties_MaintenanceCycle()
        {
            compClass = typeof(CompMaintenanceCycle);
        }
    }

    public class CompMaintenanceCycle : CompBiosculpterPod_Cycle
    {
        public new CompProperties_MaintenanceCycle Props => (CompProperties_MaintenanceCycle)props;

        public override void CycleCompleted(Pawn pawn)
        {
            if (pawn == null)
            {
                return;
            }

            Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(Props.hediffToRemove);
            if (hediff != null)
            {
                hediff.Severity = 0f;
                Messages.Message("WULA_MaintenanceCycleComplete".Translate(pawn.Named("PAWN")), pawn, MessageTypeDefOf.PositiveEvent);
            }
        }
    }
}