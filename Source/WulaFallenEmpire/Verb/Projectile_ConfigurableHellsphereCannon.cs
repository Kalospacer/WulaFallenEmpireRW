using Verse;
using RimWorld;
using System.Collections.Generic;

namespace WulaFallenEmpire
{
    /// <summary>
    /// 这个类用于定义可以在XML的 <projectile> 节点中配置的自定义属性。
    /// </summary>
    public class ProjectileProperties_ConfigurableHellsphereCannon : ProjectileProperties
    {
        // --- 修正部分：添加了 explosionChanceToStartFire ---
        // 使用 'new' 关键字来隐藏并替换基类的同名字段
        public new float explosionRadius = 4.9f;
        public new float explosionChanceToStartFire = 0f; // <--- 新增此行，使其可在XML中配置
        public new bool applyDamageToExplosionCellsNeighbors = false;
        public new ThingDef preExplosionSpawnThingDef = null;
        public new float preExplosionSpawnChance = 0f;
        public new int preExplosionSpawnThingCount = 1;
        public new ThingDef postExplosionSpawnThingDef = null;
        public new float postExplosionSpawnChance = 0f;
        public new int postExplosionSpawnThingCount = 1;
        public new GasType? postExplosionGasType = null;
        public new float screenShakeFactor = 1f;
        public new ThingDef postExplosionSpawnThingDefWater = null;
        public new ThingDef postExplosionSpawnSingleThingDef = null;
        public new ThingDef preExplosionSpawnSingleThingDef = null;

        // 这些是您添加的全新字段，不需要 'new' 关键字
        public SoundDef explosionSound = null;
        public bool damageFalloff = false;
        public bool doVisualEffects = true;
        public bool doSoundEffects = true;
        public float? postExplosionGasRadiusOverride = null;
        public int postExplosionGasAmount = 255;
        public float? direction = null;
        public FloatRange? affectedAngle = null;
        public float excludeRadius = 0f;
        public SimpleCurve flammabilityChanceCurve = null;
    }

    /// <summary>
    /// 这是投射物的核心逻辑类，由XML中的 <thingClass> 指定。
    /// </summary>
    public class Projectile_ConfigurableHellsphereCannon : Projectile
    {
        public ProjectileProperties_ConfigurableHellsphereCannon Props => (ProjectileProperties_ConfigurableHellsphereCannon)def.projectile;

        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            Map map = base.Map;
            base.Impact(hitThing, blockedByShield);

            GenExplosion.DoExplosion(
                center: base.Position,
                map: map,
                radius: Props.explosionRadius,
                damType: base.DamageDef,
                instigator: launcher,
                damAmount: DamageAmount,
                armorPenetration: ArmorPenetration,
                explosionSound: Props.explosionSound,
                weapon: equipmentDef,
                projectile: def,
                intendedTarget: intendedTarget.Thing,
                postExplosionSpawnThingDef: Props.postExplosionSpawnThingDef,
                postExplosionSpawnChance: Props.postExplosionSpawnChance,
                postExplosionSpawnThingCount: Props.postExplosionSpawnThingCount,
                postExplosionGasType: Props.postExplosionGasType,
                postExplosionGasRadiusOverride: Props.postExplosionGasRadiusOverride,
                postExplosionGasAmount: Props.postExplosionGasAmount,
                applyDamageToExplosionCellsNeighbors: Props.applyDamageToExplosionCellsNeighbors,
                preExplosionSpawnThingDef: Props.preExplosionSpawnThingDef,
                preExplosionSpawnChance: Props.preExplosionSpawnChance,
                preExplosionSpawnThingCount: Props.preExplosionSpawnThingCount,
                // --- 修正部分：使用了正确的字段 ---
                chanceToStartFire: Props.explosionChanceToStartFire, // <--- 修正了此行
                damageFalloff: Props.damageFalloff,
                direction: Props.direction,
                affectedAngle: Props.affectedAngle,
                doVisualEffects: Props.doVisualEffects,
                propagationSpeed: base.DamageDef.expolosionPropagationSpeed,
                excludeRadius: Props.excludeRadius,
                doSoundEffects: Props.doSoundEffects,
                postExplosionSpawnThingDefWater: Props.postExplosionSpawnThingDefWater,
                screenShakeFactor: Props.screenShakeFactor,
                flammabilityChanceCurve: Props.flammabilityChanceCurve,
                postExplosionSpawnSingleThingDef: Props.postExplosionSpawnSingleThingDef,
                preExplosionSpawnSingleThingDef: Props.preExplosionSpawnSingleThingDef
            );
        }
    }
}