using RimWorld;

namespace WulaFallenEmpire
{
    public class VerbProperties_Wula_IonicBeam : VerbProperties
    {
        // --- Mode 1: Breaching Beam Properties ---
        public float breachingDamage = 200f;
        public float armorPenetration = 0.8f;
        public int breachingBeamDuration = 30; // Brief duration after hit calculation

        // --- Mode 2: Sustained Beam Properties ---
        public float sustainedDamagePerTick = 15f;
        public int tickInterval = 10;
        public int duration = 120;
    }
}