using RimWorld;
using Verse;
using System.Linq;
using System.Collections.Generic;
using UnityEngine; // For Vector3
using Verse.Sound; // For SoundDef.PlayOneShot
using Verse.AI; // For JobQueue

namespace WulaFallenEmpire
{
    public class Verb_ShootWeaponStealBeam : Verse.Verb_ShootBeam
    {
        private int explosionShotCounter = 0;

        protected VerbProperties_WeaponStealBeam StealBeamVerbProps => (VerbProperties_WeaponStealBeam)verbProps;

        protected override bool TryCastShot()
        {
            bool result = base.TryCastShot();
            
            // 如果光束命中，对目标Pawn施加Hediff并检查是否抢夺武器
            if (result && currentTarget.Thing is Pawn targetPawn && targetPawn.RaceProps.Humanlike) // 只对人形Pawn生效
            {
                ApplyHediffAndCheckForSteal(targetPawn);
            }

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

            // 在这里添加武器抢夺和Hediff施加的逻辑（爆炸命中目标时）
            if (currentTarget.Thing is Pawn targetPawn && targetPawn.RaceProps.Humanlike) // 只对人形Pawn生效
            {
                ApplyHediffAndCheckForSteal(targetPawn);
            }

            // 生成额外的视觉效果
            if (explosiveProps.explosionEffecter != null)
            {
                Effecter effecter = explosiveProps.explosionEffecter.Spawn(explosionCell, caster.Map);
                effecter.Trigger(new TargetInfo(explosionCell, caster.Map), TargetInfo.Invalid);
                effecter.Cleanup();
            }
        }

        private void ApplyHediffAndCheckForSteal(Pawn targetPawn)
        {
            if (StealBeamVerbProps.hediffToApply == null)
            {
                return;
            }

            Hediff hediff = targetPawn.health.hediffSet.GetFirstHediffOfDef(StealBeamVerbProps.hediffToApply);

            if (hediff == null)
            {
                hediff = HediffMaker.MakeHediff(StealBeamVerbProps.hediffToApply, targetPawn);
                targetPawn.health.AddHediff(hediff);
            }

            hediff.Severity += StealBeamVerbProps.hediffSeverityPerHit;

            if (hediff.Severity >= StealBeamVerbProps.hediffMaxSeverity)
            {
                TryStealWeapon(targetPawn);
                if (StealBeamVerbProps.removeHediffOnSteal)
                {
                    targetPawn.health.RemoveHediff(hediff);
                }
            }
        }

        private void TryStealWeapon(Pawn targetPawn)
        {
            if (!CasterIsPawn || CasterPawn == null)
            {
                return;
            }

            // 获取目标Pawn的装备武器
            ThingWithComps targetWeapon = targetPawn.equipment?.Primary;

            if (targetWeapon != null)
            {
                // 将武器从目标Pawn身上移除
                targetPawn.equipment.Remove(targetWeapon);

                // 将武器添加到发射者（宿主）的库存中
                if (!CasterPawn.inventory.innerContainer.TryAdd(targetWeapon))
                {
                    // 如果无法添加到库存，则尝试丢弃在地上
                    GenPlace.TryPlaceThing(targetWeapon, CasterPawn.Position, CasterPawn.Map, ThingPlaceMode.Near);
                    return; // 如果丢弃了，就不尝试装备了
                }

                // 强制发射者装备该武器，并替换当前武器 (JobDriver_Equip 核心逻辑)
                // 强制发射者装备该武器，并替换当前武器 (JobDriver_Equip 核心逻辑)
                CasterPawn.equipment.MakeRoomFor(targetWeapon); // 为新装备腾出空间，并处理旧装备

                // 在 AddEquipment 之前，确保武器不在库存中
                if (CasterPawn.inventory.innerContainer.Contains(targetWeapon))
                {
                    CasterPawn.inventory.innerContainer.Remove(targetWeapon);
                }
                
                CasterPawn.equipment.AddEquipment(targetWeapon); // 添加装备
                targetWeapon.def.soundInteract?.PlayOneShot(new TargetInfo(CasterPawn.Position, CasterPawn.Map)); // 播放音效
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref explosionShotCounter, "explosionShotCounter", 0);
        }
    }
}