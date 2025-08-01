using Verse;
using RimWorld;
using System.Collections.Generic;

namespace ConfigurableHellsphereCannon
{
    public class ExplosionParameters : DefModExtension
    {
        public float explosionRadius = 4.9f;
        public SoundDef explosionSound = null;
        public ThingDef postExplosionSpawnThingDef = null;
        public float postExplosionSpawnChance = 0f;
        public int postExplosionSpawnThingCount = 1;
        public GasType? postExplosionGasType = null;
        public bool applyDamageToExplosionCellsNeighbors = false;
        public ThingDef preExplosionSpawnThingDef = null;
        public float preExplosionSpawnChance = 0f;
        public int preExplosionSpawnThingCount = 1;
        public bool damageFalloff = false;
        public ThingDef filth = null;
        public bool doVisualEffects = true;
        public float screenShakeFactor = 1f;
        public bool doSoundEffects = true;
        public bool doHitEffects = true;
    }
}