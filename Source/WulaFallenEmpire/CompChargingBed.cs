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
            Log.Message("[CompChargingBed] CompTick running.");

            var bed = (Building_Bed)parent;
            if (!bed.AnyOccupants)
            {
                for (int i = chargingPawns.Count - 1; i >= 0; i--)
                {
                    var p = chargingPawns[i];
                    var h = p.health.hediffSet.GetFirstHediffOfDef(Props.hediffDef);
                    if (h != null)
                    {
                        Log.Message($"[CompChargingBed] Bed empty. Removing hediff from {p.LabelShort}.");
                        p.health.RemoveHediff(h);
                    }
                }
                chargingPawns.Clear();
                return;
            }

            var currentOccupants = new HashSet<Pawn>(bed.CurOccupants);
            Log.Message($"[CompChargingBed] Found {currentOccupants.Count} occupants.");

            for (int i = chargingPawns.Count - 1; i >= 0; i--)
            {
                var pawn = chargingPawns[i];
                if (!currentOccupants.Contains(pawn))
                {
                    Log.Message($"[CompChargingBed] Pawn {pawn.LabelShort} left the bed. Removing hediff.");
                    var hediff = pawn.health.hediffSet.GetFirstHediffOfDef(Props.hediffDef);
                    if (hediff != null)
                    {
                        pawn.health.RemoveHediff(hediff);
                    }
                    chargingPawns.RemoveAt(i);
                }
            }

            foreach (var pawn in currentOccupants)
            {
                Log.Message($"[CompChargingBed] Checking occupant: {pawn.LabelShort}.");
                bool hasNeed = pawn.needs.TryGetNeed<Need_WulaEnergy>() != null;
                Log.Message($"[CompChargingBed] Does {pawn.LabelShort} have Need_WulaEnergy? {hasNeed}");

                if (hasNeed)
                {
                    if (!pawn.health.hediffSet.HasHediff(Props.hediffDef))
                    {
                        Log.Message($"[CompChargingBed] Adding charging hediff to {pawn.LabelShort}.");
                        pawn.health.AddHediff(Props.hediffDef);
                    }
                    if (!chargingPawns.Contains(pawn))
                    {
                        chargingPawns.Add(pawn);
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