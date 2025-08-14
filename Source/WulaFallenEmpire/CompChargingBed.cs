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
            var occupants = new HashSet<Pawn>(bed.CurOccupants);

            for (int i = chargingPawns.Count - 1; i >= 0; i--)
            {
                var pawn = chargingPawns[i];
                if (!occupants.Contains(pawn))
                {
                    var hediff = pawn.health.hediffSet.GetFirstHediffOfDef(Props.hediffDef);
                    if (hediff != null)
                    {
                        pawn.health.RemoveHediff(hediff);
                    }
                    chargingPawns.RemoveAt(i);
                }
            }

            if (bed.AnyOccupants)
            {
                foreach (var pawn in bed.CurOccupants)
                {
                    if (pawn.def.defName == Props.raceDefName)
                    {
                        if (!pawn.health.hediffSet.HasHediff(Props.hediffDef))
                        {
                            pawn.health.AddHediff(Props.hediffDef);
                            if (!chargingPawns.Contains(pawn))
                            {
                                chargingPawns.Add(pawn);
                            }
                        }
                    }
                }
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Collections.Look(ref chargingPawns, "chargingPawns", LookMode.Reference);
        }
    }
}