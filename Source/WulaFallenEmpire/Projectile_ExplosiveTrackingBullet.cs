using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace WulaFallenEmpire
{
    public class Projectile_ExplosiveTrackingBullet : Projectile_TrackingBullet
    {
        private ExplosiveTrackingBulletDef explosiveDefInt;

        public ExplosiveTrackingBulletDef ExplosiveDef
        {
            get
            {
                if (explosiveDefInt == null)
                {
                    explosiveDefInt = def.GetModExtension<ExplosiveTrackingBulletDef>();
                    if (explosiveDefInt == null)
                    {
                        Log.ErrorOnce($"ExplosiveTrackingBulletDef for {this.def.defName} is null. Creating a default instance.", this.thingIDNumber ^ 0x12345679);
                        this.explosiveDefInt = new ExplosiveTrackingBulletDef();
                    }
                }
                return explosiveDefInt;
            }
        }

        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            base.Impact(hitThing, blockedByShield); // 调用基类的Impact逻辑

            if (ExplosiveDef.explosionRadius > 0f)
            {
                // 爆炸逻辑
                GenExplosion.DoExplosion(
                    center: Position, // 爆炸中心
                    map: Map, // 地图
                    radius: ExplosiveDef.explosionRadius, // 爆炸半径
                    damType: ExplosiveDef.damageDef ?? DamageDefOf.Bomb, // 伤害类型，如果未配置则默认为Bomb
                    instigator: launcher, // 制造者
                    damAmount: this.DamageAmount, // 伤害量，使用子弹当前的伤害量
                    armorPenetration: this.ArmorPenetration, // 护甲穿透，使用子弹当前的护甲穿透
                    explosionSound: ExplosiveDef.soundExplode, // 爆炸音效
                    weapon: equipmentDef, // 武器
                    projectile: def, // 弹药定义
                    intendedTarget: intendedTarget.Thing, // 预期目标
                    postExplosionSpawnThingDef: ExplosiveDef.postExplosionSpawnThingDef, // 爆炸后生成物
                    postExplosionSpawnChance: ExplosiveDef.postExplosionSpawnChance, // 爆炸后生成几率
                    postExplosionSpawnThingCount: ExplosiveDef.postExplosionSpawnThingCount, // 爆炸后生成数量
                    postExplosionGasType: ExplosiveDef.gasType, // 气体类型 (注意参数名已修正)
                    postExplosionGasRadiusOverride: null, // 爆炸气体半径覆盖 (我没有定义这个参数)
                    postExplosionGasAmount: 255, // 爆炸气体数量 (默认值)
                    applyDamageToExplosionCellsNeighbors: ExplosiveDef.applyDamageToExplosionCellsNeighbors, // 是否对爆炸单元格邻居造成伤害
                    preExplosionSpawnThingDef: null, // 爆炸前生成物 (我没有定义这个参数)
                    preExplosionSpawnChance: 0f, // 爆炸前生成几率 (默认值)
                    preExplosionSpawnThingCount: 0, // 爆炸前生成数量 (默认值)
                    chanceToStartFire: ExplosiveDef.explosionChanceToStartFire, // 是否有几率点燃 (注意参数名已修正)
                    damageFalloff: ExplosiveDef.explosionDamageFalloff, // 爆炸伤害衰减
                    direction: null, // 方向 (我没有定义这个参数)
                    ignoredThings: null, // 忽略的物体 (我没有定义这个参数)
                    affectedAngle: null, // 受影响角度 (我没有定义这个参数)
                    doVisualEffects: ExplosiveDef.doExplosionVFX, // 是否显示视觉效果
                    propagationSpeed: 1f, // 传播速度 (默认值)
                    excludeRadius: 0f, // 排除半径 (默认值)
                    doSoundEffects: true, // 是否播放音效 (默认值)
                    postExplosionSpawnThingDefWater: null, // 爆炸后在水中生成物 (我没有定义这个参数)
                    screenShakeFactor: 1f, // 屏幕震动因子 (默认值)
                    flammabilityChanceCurve: null, // 易燃性几率曲线 (我没有定义这个参数)
                    overrideCells: null, // 覆盖单元格 (我没有定义这个参数)
                    postExplosionSpawnSingleThingDef: null, // 爆炸后生成单个物体 (我没有定义这个参数)
                    preExplosionSpawnSingleThingDef: null // 爆炸前生成单个物体 (我没有定义这个参数)
                );
            }
        }
    }
}