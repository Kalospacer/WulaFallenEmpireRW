using System.Collections.Generic;
using Verse;
using Verse.AI.Group;
using RimWorld;

namespace WulaFallenEmpire
{
    public class PsychicRitualDef_AddHediff : PsychicRitualDef_InvocationCircle
    {
        public HediffDef hediff;

        public override List<PsychicRitualToil> CreateToils(PsychicRitual psychicRitual, PsychicRitualGraph parent)
        {
            List<PsychicRitualToil> list = base.CreateToils(psychicRitual, parent);
            list.Add(new PsychicRitualToil_AddHediff(TargetRole, hediff));
            list.Add(new PsychicRitualToil_TargetCleanup(InvokerRole, TargetRole));
            return list;
        }
    }
}