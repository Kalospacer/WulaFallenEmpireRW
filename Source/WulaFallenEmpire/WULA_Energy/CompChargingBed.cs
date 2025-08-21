using RimWorld;
using System.Collections.Generic;
using Verse;

namespace WulaFallenEmpire
{
    public class CompProperties_ChargingBed : CompProperties
    {
        public HediffDef hediffDef;
        public string raceDefName;

        public CompProperties_ChargingBed()
        {
            compClass = typeof(CompChargingBed);
        }
    }

    public class CompChargingBed : ThingComp
    {
        public CompProperties_ChargingBed Props => (CompProperties_ChargingBed)props;

        private List<Pawn> chargingPawns = new List<Pawn>();

        public override void CompTick()
        {
            base.CompTick();

            var bed = (Building_Bed)parent;
            var powerComp = parent.GetComp<CompPowerTrader>();

            // 如果床没电，停止所有充电
            if (powerComp is { PowerOn: false })
            {
                StopAllCharging();
                return;
            }

            var currentOccupants = new HashSet<Pawn>(bed.CurOccupants);

            // 移除已经不在床上的 pawn 的充电效果
            for (int i = chargingPawns.Count - 1; i >= 0; i--)
            {
                var pawn = chargingPawns[i];
                if (!currentOccupants.Contains(pawn))
                {
                    StopCharging(pawn);
                    chargingPawns.RemoveAt(i);
                }
            }

            // 为床上的新 pawn 开始充电
            foreach (var pawn in currentOccupants)
            {
                if (ShouldCharge(pawn) && !chargingPawns.Contains(pawn))
                {
                    StartCharging(pawn);
                }
            }
        }

        private bool ShouldCharge(Pawn pawn)
        {
            return pawn.def.defName == Props.raceDefName;
        }

        private void StartCharging(Pawn pawn)
        {
            if (pawn.health.hediffSet.HasHediff(Props.hediffDef)) return;
            pawn.health.AddHediff(Props.hediffDef);
            if (!chargingPawns.Contains(pawn))
            {
                chargingPawns.Add(pawn);
            }
        }


        private void StopCharging(Pawn pawn)
        {
            var hediff = pawn.health.hediffSet.GetFirstHediffOfDef(Props.hediffDef);
            if (hediff != null)
            {
                pawn.health.RemoveHediff(hediff);
            }
        }

        private void StopAllCharging()
        {
            for (int i = chargingPawns.Count - 1; i >= 0; i--)
            {
                StopCharging(chargingPawns[i]);
                chargingPawns.RemoveAt(i);
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Collections.Look(ref chargingPawns, "chargingPawns", LookMode.Reference);
        }
    }
}