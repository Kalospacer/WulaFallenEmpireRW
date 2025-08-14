using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    public class JobDriver_WulaLayDownToCharge : JobDriver_LayDown
    {
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.AddFinishAction(jobCondition =>
            {
                Log.Message($"[JobDriver_WulaLayDownToCharge] Job finishing for {pawn.Name.ToStringShort} with condition {jobCondition}. Removing hediff.");
                var hediff = pawn.health.hediffSet.GetFirstHediffOfDef(HediffDef.Named("WULA_ChargingHediff"));
                if (hediff != null)
                {
                    pawn.health.RemoveHediff(hediff);
                    Log.Message($"[JobDriver_WulaLayDownToCharge] Hediff removed from {pawn.Name.ToStringShort}.");
                }
                else
                {
                    Log.Message($"[JobDriver_WulaLayDownToCharge] No hediff found on {pawn.Name.ToStringShort} to remove.");
                }
            });

            foreach (Toil toil in base.MakeNewToils())
            {
                yield return toil;
            }

            var bed = (Building_Bed)job.targetA.Thing;
            var powerComp = bed.GetComp<CompPowerTrader>();

            var checkToil = new Toil
            {
                tickAction = delegate
                {
                    if (powerComp != null && !powerComp.PowerOn)
                    {
                        Log.Message($"[JobDriver_WulaLayDownToCharge] Power lost for {pawn.Name.ToStringShort}. Ending job.");
                        EndJobWith(JobCondition.Incompletable);
                        return;
                    }

                    Need_WulaEnergy energyNeed = pawn.needs.TryGetNeed<Need_WulaEnergy>();
                    if (energyNeed != null && energyNeed.CurLevelPercentage >= 0.99f)
                    {
                        Log.Message($"[JobDriver_WulaLayDownToCharge] {pawn.Name.ToStringShort} is fully charged. Ending job.");
                        EndJobWith(JobCondition.Succeeded);
                    }
                },
                defaultCompleteMode = ToilCompleteMode.Never
            };
            yield return checkToil;
        }
    }
}