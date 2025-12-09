using System.Collections.Generic;
using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class CompProperties_PhaseCombatTower : CompProperties
    {
        // 阶段1: 启动期
        public int warmupTicks = 180; // 启动期帧数 (3秒)
        
        // 阶段2: 爆炸阶段
        public List<ExplosionData> explosions = new List<ExplosionData>();
        
        // 阶段3: 生成Pawn阶段
        public List<string> pawnKindDefs = new List<string>(); // Pawn种类列表
        public int spawnCount = 5; // 需要生成的Pawn数量 (Y)
        public int spawnIntervalTicks = 120; // 生成间隔帧数 (Z)
        
        // 爆炸冷却期
        public int explosionCooldownTicks = 60; // 爆炸之间的冷却时间 (默认1秒)
        
        public CompProperties_PhaseCombatTower()
        {
            compClass = typeof(CompPhaseCombatTower);
        }
    }
    
    // 爆炸数据定义
    public class ExplosionData
    {
        public DamageDef damageDef; // 伤害类型
        public float radius = 3f; // 爆炸范围
        public float armorPenetration = 0f; // 穿甲系数
        public int damageAmount = 30; // 伤害值
        public SoundDef explosionSound = null; // 爆炸声音
        public float chanceToStartFire = 0f; // 起火概率
        public bool damageFalloff = true; // 伤害是否随距离衰减
        
        // 气体释放参数
        public GasType? postExplosionGasType = null; // 爆炸后生成的气体类型
        public float? postExplosionGasRadiusOverride = null; // 气体半径覆盖（如果为null，则使用爆炸半径）
        public int postExplosionGasAmount = 255; // 气体数量（0-255）
        
        // 爆炸前/后生成物体
        public ThingDef preExplosionSpawnThingDef = null; // 爆炸前生成物
        public float preExplosionSpawnChance = 0f; // 爆炸前生成几率
        public int preExplosionSpawnThingCount = 1; // 爆炸前生成数量
        
        public ThingDef postExplosionSpawnThingDef = null; // 爆炸后生成物
        public float postExplosionSpawnChance = 0f; // 爆炸后生成几率
        public int postExplosionSpawnThingCount = 1; // 爆炸后生成数量
        
        // 高级参数
        public bool applyDamageToExplosionCellsNeighbors = false; // 是否对爆炸单元格邻居造成伤害
        public float? direction = null; // 爆炸方向（角度，0-360）
        public FloatRange? affectedAngle = null; // 受影响角度范围
        public float propagationSpeed = 1f; // 爆炸传播速度
        public float excludeRadius = 0f; // 排除半径（中心区域不受伤害）
        public float screenShakeFactor = 1f; // 屏幕震动因子
    }
}
