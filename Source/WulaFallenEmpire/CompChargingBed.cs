using RimWorld;
using Verse;
using System.Linq;

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
        private Pawn lastOccupant;
        private CompProperties_ChargingBed Props => (CompProperties_ChargingBed)props;

        public override void CompTick()
        {
            base.CompTick();

            if (parent is Building_Bed bed)
            {
                Pawn currentOccupant = bed.CurOccupants.FirstOrDefault();

                if (currentOccupant != lastOccupant)
                {
                    Log.Message($"[CompChargingBed] Occupant changed. Old: {lastOccupant?.Name.ToStringShort ?? "null"}, New: {currentOccupant?.Name.ToStringShort ?? "null"} on {parent.Label}");
                }

                // Pawn starts resting
                if (currentOccupant != null && lastOccupant == null)
                {
                    if (IsWula(currentOccupant))
                    {
                        Log.Message($"[CompChargingBed] {currentOccupant.Name.ToStringShort} started resting. Applying hediff.");
                        ApplyChargingHediff(currentOccupant);
                    }
                }
                // Pawn stops resting
                else if (currentOccupant == null && lastOccupant != null)
                {
                    // Logic to remove hediff is now in the JobDriver, but we can log the event.
                    if (IsWula(lastOccupant))
                    {
                         Log.Message($"[CompChargingBed] {lastOccupant.Name.ToStringShort} stopped resting.");
                    }
                }
                
                lastOccupant = currentOccupant;
            }
        }

        private bool IsWula(Pawn pawn)
        {
            return pawn.def.defName == Props.raceDefName || pawn.def.defName == (Props.raceDefName + "Real");
        }

        private void ApplyChargingHediff(Pawn pawn)
        {
            var powerComp = parent.GetComp<CompPowerTrader>();
            Log.Message($"[CompChargingBed] Trying to apply hediff to {pawn.Name.ToStringShort}. PowerOn: {powerComp?.PowerOn}. HasHediff: {pawn.health.hediffSet.HasHediff(Props.hediffDef)}");
            if (powerComp != null && powerComp.PowerOn && !pawn.health.hediffSet.HasHediff(Props.hediffDef))
            {
                Log.Message($"[CompChargingBed] Adding hediff to {pawn.Name.ToStringShort}.");
                pawn.health.AddHediff(Props.hediffDef);
            }
        }

    }
}