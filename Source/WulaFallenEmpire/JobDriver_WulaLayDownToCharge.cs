using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    public class JobDriver_WulaLayDownToCharge : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.AddFinishAction(jobCondition =>
            {
                var bed = (Building_Bed)job.targetA.Thing;
                var comp = bed.GetComp<CompChargingBed>();
                if (comp == null) return;
                var hediff = pawn.health.hediffSet.GetFirstHediffOfDef(comp.Props.hediffDef);
                if (hediff != null)
                {
                    pawn.health.RemoveHediff(hediff);
                }
            });

            yield return Toils_Bed.GotoBed(TargetIndex.A);

            Toil layDownAndCharge = Toils_LayDown.LayDown(TargetIndex.A, true, false, false, false);
            layDownAndCharge.AddPreInitAction(delegate
            {
                if (!pawn.health.hediffSet.HasHediff(HediffDef.Named("WULA_ChargingHediff")))
                {
                    var bed = (Building_Bed)job.targetA.Thing;
                    var comp = bed.GetComp<CompChargingBed>();
                    if (comp != null && !pawn.health.hediffSet.HasHediff(comp.Props.hediffDef))
                    {
                        pawn.health.AddHediff(comp.Props.hediffDef);
                    }
                }
            });
            
            layDownAndCharge.tickAction = delegate
            {
                if (pawn.IsHashIntervalTick(60))
                {
                    var bed = (Building_Bed)job.targetA.Thing;
                    var powerComp = bed.GetComp<CompPowerTrader>();

                    if (powerComp != null && !powerComp.PowerOn)
                    {
                        EndJobWith(JobCondition.Incompletable);
                        return;
                    }

                    Need_WulaEnergy energyNeed = pawn.needs.TryGetNeed<Need_WulaEnergy>();
                    if (energyNeed != null && energyNeed.CurLevelPercentage >= 0.99f)
                    {
                        EndJobWith(JobCondition.Succeeded);
                    }
                }
            };
            
            yield return layDownAndCharge;
        }
    }
}