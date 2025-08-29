using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace WulaFallenEmpire
{
    public class Projectile_ExplosiveTrackingBullet : Projectile_TrackingBullet
    {
        private ExplosiveTrackingBulletDef explosiveDefInt;
        private int ticksToDetonation;

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

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref this.ticksToDetonation, "ticksToDetonation", 0, false);
        }

        protected override void Tick()
        {
            base.Tick(); // Call base Projectile_TrackingBullet Tick logic
            bool flag = this.ticksToDetonation > 0;
            if (flag)
            {
                this.ticksToDetonation--;
                bool flag2 = this.ticksToDetonation <= 0;
                if (flag2)
                {
                    this.Explode();
                }
            }
        }

        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            bool flag = hitThing == null || blockedByShield || ExplosiveDef.explosionDelay == 0; // Use ExplosiveDef for explosionDelay
            if (flag)
            {
                this.Explode();
            }
            else
            {
                this.landed = true;
                this.ticksToDetonation = ExplosiveDef.explosionDelay; // Use ExplosiveDef for explosionDelay
                GenExplosion.NotifyNearbyPawnsOfDangerousExplosive(this, ExplosiveDef.damageDef ?? DamageDefOf.Bomb, this.launcher.Faction, this.launcher); // Use ExplosiveDef for damageDef
                // 停止追踪并清空速度，确保子弹停止移动
                this.homing = false;
                this.curSpeed = Vector3.zero;
            }
        }

        protected virtual void Explode()
        {
            Map map = base.Map;
            // ModExtension_Cone modExtension = this.def.GetModExtension<ModExtension_Cone>(); // Not used in this explosive logic
            this.DoExplosion(map); // Call the helper DoExplosion with map
            
            // Cone explosion logic (if needed, based on ModExtension_Cone) - Currently not implemented for this class
            // bool flag = modExtension != null;
            // if (flag)
            // {
            //     ProjectileProperties projectile = this.def.projectile;
            //     ModExtension_Cone modExtension_Cone = modExtension;
            //     IntVec3 position = base.Position;
            //     Map map2 = map;
            //     Quaternion exactRotation = this.ExactRotation;
            //     DamageDef damageDef = projectile.damageDef;
            //     Thing launcher = base.Launcher;
            //     int damageAmount = this.DamageAmount;
            //     float armorPenetration = this.ArmorPenetration;
            //     SoundDef soundExplode = this.def.projectile.soundExplode;
            //     ThingDef equipmentDef = this.equipmentDef;
            //     ThingDef def = this.def;
            //     Thing thing = this.intendedTarget.Thing;
            //     ThingDef postExplosionSpawnThingDef = null;
            //     float postExplosionSpawnChance = 0f;
            //     int postExplosionSpawnThingCount = 1;
            //     float screenShakeFactor = this.def.projectile.screenShakeFactor;
            //     modExtension_Cone.DoConeExplosion(position, map2, exactRotation, damageDef, launcher, damageAmount, armorPenetration, soundExplode, equipmentDef, def, thing, postExplosionSpawnThingDef, postExplosionSpawnChance, postExplosionSpawnThingCount, null, null, 255, false, null, 0f, 1, 0f, false, null, null, 1f, 0f, null, screenShakeFactor, null, null);
            // }

            // Explosion effect (if needed, based on def.projectile.explosionEffect) - Currently not implemented for this class
            // bool flag2 = this.def.projectile.explosionEffect != null;
            // if (flag2)
            // {
            //     Effecter effecter = this.def.projectile.explosionEffect.Spawn();
            //     bool flag3 = this.def.projectile.explosionEffectLifetimeTicks != 0;
            //     if (flag3)
            //     {
            //         map.effecterMaintainer.AddEffecterToMaintain(effecter, base.Position.ToVector3().ToIntVec3(), this.def.projectile.explosionEffectLifetimeTicks);
            //     }
            //     else
            //     {
            //         effecter.Trigger(new TargetInfo(base.Position, map, false), new TargetInfo(base.Position, map, false), -1);
            //         effecter.Cleanup();
            //     }
            // }
            this.Destroy(DestroyMode.Vanish);
        }

        protected void DoExplosion(Map map)
        {
            IntVec3 position = base.Position;
            float explosionRadius = ExplosiveDef.explosionRadius; // Use ExplosiveDef for explosionRadius
            DamageDef damageDef = ExplosiveDef.damageDef ?? DamageDefOf.Bomb; // Use ExplosiveDef for damageDef
            Thing launcher = this.launcher;
            int damageAmount = this.DamageAmount;
            float armorPenetration = this.ArmorPenetration;
            SoundDef soundExplode = ExplosiveDef.soundExplode; // Use ExplosiveDef for soundExplode
            ThingDef equipmentDef = this.equipmentDef;
            ThingDef def = this.def; // This is the projectile's ThingDef
            Thing thing = this.intendedTarget.Thing;
            ThingDef postExplosionSpawnThingDef = ExplosiveDef.postExplosionSpawnThingDef; // Use ExplosiveDef for postExplosionSpawnThingDef
            ThingDef postExplosionSpawnThingDefWater = ExplosiveDef.postExplosionSpawnThingDefWater; // Use ExplosiveDef for postExplosionSpawnThingDefWater
            float postExplosionSpawnChance = ExplosiveDef.postExplosionSpawnChance; // Use ExplosiveDef for postExplosionSpawnChance
            int postExplosionSpawnThingCount = ExplosiveDef.postExplosionSpawnThingCount; // Use ExplosiveDef for postExplosionSpawnThingCount
            GasType? postExplosionGasType = ExplosiveDef.gasType; // Use ExplosiveDef for gasType
            ThingDef preExplosionSpawnThingDef = ExplosiveDef.preExplosionSpawnThingDef; // Use ExplosiveDef for preExplosionSpawnThingDef
            float preExplosionSpawnChance = ExplosiveDef.preExplosionSpawnChance; // Use ExplosiveDef for preExplosionSpawnChance
            int preExplosionSpawnThingCount = ExplosiveDef.preExplosionSpawnThingCount; // Use ExplosiveDef for preExplosionSpawnThingCount
            bool applyDamageToExplosionCellsNeighbors = ExplosiveDef.applyDamageToExplosionCellsNeighbors; // Use ExplosiveDef for applyDamageToExplosionCellsNeighbors
            float explosionChanceToStartFire = ExplosiveDef.explosionChanceToStartFire; // Use ExplosiveDef for explosionChanceToStartFire
            bool explosionDamageFalloff = ExplosiveDef.explosionDamageFalloff; // Use ExplosiveDef for explosionDamageFalloff
            float? direction = new float?(this.origin.AngleToFlat(this.destination)); // This remains from original logic
            float screenShakeFactor = ExplosiveDef.screenShakeFactor; // Use ExplosiveDef for screenShakeFactor
            bool doExplosionVFX = ExplosiveDef.doExplosionVFX; // Use ExplosiveDef for doExplosionVFX

            GenExplosion.DoExplosion(
                center: ExactPosition.ToIntVec3(), // 爆炸中心
                map: map, // 地图
                radius: explosionRadius, // 爆炸半径
                damType: damageDef, // 伤害类型
                instigator: launcher, // 制造者
                damAmount: damageAmount, // 伤害量
                armorPenetration: armorPenetration, // 护甲穿透
                explosionSound: soundExplode, // 爆炸音效
                weapon: equipmentDef, // 武器
                projectile: def, // 弹药定义
                intendedTarget: thing, // 预期目标
                postExplosionSpawnThingDef: postExplosionSpawnThingDef, // 爆炸后生成物
                postExplosionSpawnChance: postExplosionSpawnChance, // 爆炸后生成几率
                postExplosionSpawnThingCount: postExplosionSpawnThingCount, // 爆炸后生成数量
                postExplosionGasType: postExplosionGasType, // 气体类型
                postExplosionGasRadiusOverride: null, // 爆炸气体半径覆盖
                postExplosionGasAmount: 255, // 爆炸气体数量
                applyDamageToExplosionCellsNeighbors: applyDamageToExplosionCellsNeighbors, // 是否对爆炸单元格邻居造成伤害
                preExplosionSpawnThingDef: preExplosionSpawnThingDef, // 爆炸前生成物
                preExplosionSpawnChance: preExplosionSpawnChance, // 爆炸前生成几率
                preExplosionSpawnThingCount: preExplosionSpawnThingCount, // 爆炸前生成数量
                chanceToStartFire: explosionChanceToStartFire, // 是否有几率点燃
                damageFalloff: explosionDamageFalloff, // 爆炸伤害衰减
                direction: direction, // 方向
                ignoredThings: null, // 忽略的物体
                affectedAngle: null, // 受影响角度
                doVisualEffects: doExplosionVFX, // 是否显示视觉效果
                propagationSpeed: 1f, // 传播速度
                excludeRadius: 0f, // 排除半径
                doSoundEffects: true, // 是否播放音效
                postExplosionSpawnThingDefWater: postExplosionSpawnThingDefWater, // 爆炸后在水中生成物
                screenShakeFactor: screenShakeFactor, // 屏幕震动因子
                flammabilityChanceCurve: null, // 易燃性几率曲线
                overrideCells: null, // 覆盖单元格
                postExplosionSpawnSingleThingDef: null, // 爆炸后生成单个物体
                preExplosionSpawnSingleThingDef: null // 爆炸前生成单个物体
            );
        }
    }
}