using RimWorld;
using Verse;
using Verse.Sound;

namespace WulaFallenEmpire
{
    public class VerbPropertiesExplosiveBeam : VerbProperties
    {
        // 爆炸开关
        public bool enableExplosion = false;
        
        // 每x个shotcount触发一次爆炸
        public int explosionShotInterval = 1;
        
        // 爆炸基础属性
        public float explosionRadius = 2.9f;
        public DamageDef explosionDamageDef = null; // null时使用默认的Bomb
        public int explosionDamage = -1; // -1时使用武器默认伤害
        public float explosionArmorPenetration = -1f; // -1时使用武器默认穿甲
        
        // 爆炸音效和特效
        public SoundDef explosionSound = null;
        public EffecterDef explosionEffecter = null;
        
        // 爆炸后生成物品
        public ThingDef postExplosionSpawnThingDef = null;
        public float postExplosionSpawnChance = 0f;
        public int postExplosionSpawnThingCount = 1;
        
        // 爆炸前生成物品
        public ThingDef preExplosionSpawnThingDef = null;
        public float preExplosionSpawnChance = 0f;
        public int preExplosionSpawnThingCount = 1;
        
        // 气体效果
        public GasType? postExplosionGasType = null;
        
        // 其他爆炸属性
        public bool applyDamageToExplosionCellsNeighbors = true;
        public float chanceToStartFire = 0f;
        public bool damageFalloff = true;
        public float screenShakeFactor = 0f; // 新增：屏幕震动因子
        
        public VerbPropertiesExplosiveBeam()
        {
            // 设置默认值
            verbClass = typeof(Verb_ShootBeamExplosive);
        }
    }
}
