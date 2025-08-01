using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace WulaFallenEmpire
{
    public class PsychicRitualToil_AddHediff : PsychicRitualToil
    {
        public PsychicRitualRoleDef targetRole;
        public HediffDef hediff;

        private static List<Pawn> tmpTargetPawns = new List<Pawn>(4);

        public PsychicRitualToil_AddHediff()
        {
        }

        public PsychicRitualToil_AddHediff(PsychicRitualRoleDef targetRole, HediffDef hediff)
        {
            this.targetRole = targetRole;
            this.hediff = hediff;
        }

        public override void Start(PsychicRitual psychicRitual, PsychicRitualGraph graph)
        {
            tmpTargetPawns.Clear();
            tmpTargetPawns.AddRange(psychicRitual.assignments.AssignedPawns(targetRole));
            foreach (Pawn tmpTargetPawn in tmpTargetPawns)
            {
                ApplyOutcome(psychicRitual, tmpTargetPawn);
            }
        }

        private void ApplyOutcome(PsychicRitual psychicRitual, Pawn pawn)
        {
            if (hediff != null)
            {
                pawn.health.AddHediff(hediff);
            }

            if (PawnUtility.ShouldSendNotificationAbout(pawn))
            {
                Find.LetterStack.ReceiveLetter("PsychicRitualCompleteLabel".Translate(psychicRitual.def.label), ((PsychicRitualDef_AddHediff)psychicRitual.def).outcomeDescription.Formatted(pawn.Named("PAWN")), LetterDefOf.NeutralEvent, pawn);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref targetRole, "targetRole");
            Scribe_Defs.Look(ref hediff, "hediff");
        }
    }
}