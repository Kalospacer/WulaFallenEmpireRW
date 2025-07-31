using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace WulaFallenEmpire
{
    public class Verb_ShootBeamExplosive : Verse.Verb_ShootBeam
    {
        private int explosionShotCounter = 0;

        protected override bool TryCastShot()
        {
            bool result = base.TryCastShot();
            
            if (result && verbProps is VerbPropertiesExplosiveBeam explosiveProps && explosiveProps.enableExplosion)
            {
                explosionShotCounter++;
                
                if (explosionShotCounter >= explosiveProps.explosionShotInterval)
                {
                    explosionShotCounter = 0;
                    TriggerExplosion(explosiveProps);
                }
            }
            
            return result;
        }

        private void TriggerExplosion(VerbPropertiesExplosiveBeam explosiveProps)
        {
            Vector3 explosionPos = InterpolatedPosition;
            IntVec3 explosionCell = explosionPos.ToIntVec3();
            
            if (!explosionCell.InBounds(caster.Map))
                return;

            // 播放爆炸音效
            if (explosiveProps.explosionSound != null)
            {
                explosiveProps.explosionSound.PlayOneShot(new TargetInfo(explosionCell, caster.Map));
            }

            // 生成爆炸
            GenExplosion.DoExplosion(
                center: explosionCell,
                map: caster.Map,
                radius: explosiveProps.explosionRadius,
                damType: explosiveProps.explosionDamageDef ?? DamageDefOf.Bomb,
                instigator: caster,
                damAmount: explosiveProps.explosionDamage > 0 ? explosiveProps.explosionDamage : verbProps.defaultProjectile?.projectile?.GetDamageAmount(EquipmentSource) ?? 20,
                armorPenetration: explosiveProps.explosionArmorPenetration >= 0 ? explosiveProps.explosionArmorPenetration : verbProps.defaultProjectile?.projectile?.GetArmorPenetration(EquipmentSource) ?? 0.3f,
                explosionSound: null, // 我们已经手动播放了音效
                weapon: base.EquipmentSource?.def,
                projectile: null,
                intendedTarget: currentTarget.Thing,
                postExplosionSpawnThingDef: explosiveProps.postExplosionSpawnThingDef,
                postExplosionSpawnChance: explosiveProps.postExplosionSpawnChance,
                postExplosionSpawnThingCount: explosiveProps.postExplosionSpawnThingCount,
                postExplosionGasType: explosiveProps.postExplosionGasType,
                applyDamageToExplosionCellsNeighbors: explosiveProps.applyDamageToExplosionCellsNeighbors,
                preExplosionSpawnThingDef: explosiveProps.preExplosionSpawnThingDef,
                preExplosionSpawnChance: explosiveProps.preExplosionSpawnChance,
                preExplosionSpawnThingCount: explosiveProps.preExplosionSpawnThingCount,
                chanceToStartFire: explosiveProps.chanceToStartFire,
                damageFalloff: explosiveProps.damageFalloff,
                direction: null,
                ignoredThings: null,
                affectedAngle: null,
                doVisualEffects: true,
                propagationSpeed: 0.6f,
                excludeRadius: 0f,
                doSoundEffects: false, // 我们手动处理音效
                screenShakeFactor: explosiveProps.screenShakeFactor // 新增：屏幕震动因子
            );

            // 生成额外的视觉效果
            if (explosiveProps.explosionEffecter != null)
            {
                Effecter effecter = explosiveProps.explosionEffecter.Spawn(explosionCell, caster.Map);
                effecter.Trigger(new TargetInfo(explosionCell, caster.Map), TargetInfo.Invalid);
                effecter.Cleanup();
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref explosionShotCounter, "explosionShotCounter", 0);
        }
    }
}
