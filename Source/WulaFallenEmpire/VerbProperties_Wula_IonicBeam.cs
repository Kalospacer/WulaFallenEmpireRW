using RimWorld;
using Verse;

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

        // --- NEW: Explosion Path Properties (for both modes) ---
        public bool explosionEnabled = false;
        public int explosionTickInterval = 15;
        public DamageDef explosionDamageDef;
        public float explosionEnergyCostRatio = 0.5f; // Only for Breaching Beam
        
        // Manual explosion effect properties
        public float explosionHeatEnergyPerCell = 0;
        public FleckDef explosionCellFleck;
        public Color explosionColorCenter = Color.white;
        public Color explosionColorEdge = Color.white;
        public SoundDef soundExplosion;
    }
}