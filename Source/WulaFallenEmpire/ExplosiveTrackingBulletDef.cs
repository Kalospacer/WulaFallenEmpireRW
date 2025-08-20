using Verse;
using RimWorld;

namespace WulaFallenEmpire
{
    public class ExplosiveTrackingBulletDef : DefModExtension
    {
        public float explosionRadius = 1.9f;
        public DamageDef damageDef;
        public int explosionDelay = 0;
        public SoundDef soundExplode;
        public FleckDef preExplosionFlash;
        public ThingDef postExplosionSpawnThingDef;
        public float postExplosionSpawnChance = 0f;
        public int postExplosionSpawnThingCount = 1;
        public GasType? gasType; // 修改为可空类型
        public bool applyDamageToExplosionCellsNeighbors = false;
        public bool doExplosionDamageAfterThingDestroyed = false;
        public float preExplosionSpawnMinMeleeThreat = -1f;
        public float explosionChanceToStartFire = 0f; // 从bool改为float，并设置默认值
        public bool explosionDamageFalloff = false;
        public bool doExplosionVFX = true;
    }
}