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
            yield return Toils_Bed.GotoBed(TargetIndex.A);

            Toil layDownAndCharge = Toils_LayDown.LayDown(TargetIndex.A, true, false, false, false);
            layDownAndCharge.tickAction = delegate
            {
                var bed = (Building_Bed)job.targetA.Thing;
                var powerComp = bed.GetComp<CompPowerTrader>();

                if (powerComp is { PowerOn: false })
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                
                var energyNeed = pawn.needs.TryGetNeed<Need_WulaEnergy>();
                if (energyNeed != null && energyNeed.CurLevelPercentage >= 1f)
                {
                    EndJobWith(JobCondition.Succeeded);
                }
            };
            
            yield return layDownAndCharge;
        }
    }
}