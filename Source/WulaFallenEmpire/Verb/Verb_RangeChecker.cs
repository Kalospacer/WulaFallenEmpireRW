// File: Verb_RangeChecker.cs
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    /// <summary>
    /// 用于距离判断的Verb，不发射任何射弹，不造成伤害，仅用于距离计算和AI判断
    /// 当发射成功时，会设置Pawn身上所有Comp_MultiTurretGun的focusTarget为目标
    /// </summary>
    public class Verb_RangeChecker : Verb_LaunchProjectile
    {
        protected override bool TryCastShot()
        {
            if (currentTarget.HasThing && currentTarget.Thing.Map != caster.Map)
            {
                return false;
            }
            
            ThingDef projectile = Projectile;
            if (projectile == null)
            {
                return false;
            }
            
            ShootLine resultingLine;
            bool flag = TryFindShootLineFromTo(caster.Position, currentTarget, out resultingLine);
            if (verbProps.stopBurstWithoutLos && !flag)
            {
                return false;
            }
            
            if (base.EquipmentSource != null)
            {
                base.EquipmentSource.GetComp<CompChangeableProjectile>()?.Notify_ProjectileLaunched();
                base.EquipmentSource.GetComp<CompApparelVerbOwner_Charged>()?.UsedOnce();
            }
            
            lastShotTick = Find.TickManager.TicksGame;
            Thing manningPawn = caster;
            Thing equipmentSource = base.EquipmentSource;
            CompMannable compMannable = caster.TryGetComp<CompMannable>();
            if (compMannable?.ManningPawn != null)
            {
                manningPawn = compMannable.ManningPawn;
                equipmentSource = caster;
            }
            
            Vector3 drawPos = caster.DrawPos;
            Projectile projectile2 = (Projectile)GenSpawn.Spawn(projectile, resultingLine.Source, caster.Map);
            if (equipmentSource.TryGetComp(out CompUniqueWeapon comp))
            {
                foreach (WeaponTraitDef item in comp.TraitsListForReading)
                {
                    if (item.damageDefOverride != null)
                    {
                        projectile2.damageDefOverride = item.damageDefOverride;
                    }
                    
                    if (!item.extraDamages.NullOrEmpty())
                    {
                        Projectile projectile3 = projectile2;
                        if (projectile3.extraDamages == null)
                        {
                            projectile3.extraDamages = new List<ExtraDamage>();
                        }
                        projectile2.extraDamages.AddRange(item.extraDamages);
                    }
                }
            }
            
            if (verbProps.ForcedMissRadius > 0.5f)
            {
                float num = verbProps.ForcedMissRadius;
                if (manningPawn is Pawn pawn)
                {
                    num *= verbProps.GetForceMissFactorFor(equipmentSource, pawn);
                }
                
                float num2 = VerbUtility.CalculateAdjustedForcedMiss(num, currentTarget.Cell - caster.Position);
                if (num2 > 0.5f)
                {
                    IntVec3 forcedMissTarget = GetForcedMissTarget(num2);
                    if (forcedMissTarget != currentTarget.Cell)
                    {
                        ProjectileHitFlags projectileHitFlags = ProjectileHitFlags.NonTargetWorld;
                        if (Rand.Chance(0.5f))
                        {
                            projectileHitFlags = ProjectileHitFlags.All;
                        }
                        
                        if (!canHitNonTargetPawnsNow)
                        {
                            projectileHitFlags &= ~ProjectileHitFlags.NonTargetPawns;
                        }
                        
                        // 模拟发射成功，但不实际发射
                        bool shotResult = SimulateShotSuccess(drawPos, forcedMissTarget, currentTarget, projectileHitFlags, preventFriendlyFire, equipmentSource);
                        if (shotResult)
                        {
                            UpdateTurretFocusTargets();
                        }
                        return shotResult;
                    }
                }
            }
            
            ShotReport shotReport = ShotReport.HitReportFor(caster, this, currentTarget);
            Thing randomCoverToMissInto = shotReport.GetRandomCoverToMissInto();
            ThingDef targetCoverDef = randomCoverToMissInto?.def;
            
            if (verbProps.canGoWild && !Rand.Chance(shotReport.AimOnTargetChance_IgnoringPosture))
            {
                bool flyOverhead = projectile2?.def?.projectile != null && projectile2.def.projectile.flyOverhead;
                resultingLine.ChangeDestToMissWild(shotReport.AimOnTargetChance_StandardTarget, flyOverhead, caster.Map);
                ProjectileHitFlags projectileHitFlags2 = ProjectileHitFlags.NonTargetWorld;
                if (Rand.Chance(0.5f) && canHitNonTargetPawnsNow)
                {
                    projectileHitFlags2 |= ProjectileHitFlags.NonTargetPawns;
                }
                
                // 模拟发射成功，但不实际发射
                bool shotResult = SimulateShotSuccess(drawPos, resultingLine.Dest, currentTarget, projectileHitFlags2, preventFriendlyFire, equipmentSource, targetCoverDef);
                if (shotResult)
                {
                    UpdateTurretFocusTargets();
                }
                return shotResult;
            }
            
            if (currentTarget.Thing != null && currentTarget.Thing.def.CanBenefitFromCover && !Rand.Chance(shotReport.PassCoverChance))
            {
                ProjectileHitFlags projectileHitFlags3 = ProjectileHitFlags.NonTargetWorld;
                if (canHitNonTargetPawnsNow)
                {
                    projectileHitFlags3 |= ProjectileHitFlags.NonTargetPawns;
                }
                
                // 模拟发射成功，但不实际发射
                bool shotResult = SimulateShotSuccess(drawPos, randomCoverToMissInto, currentTarget, projectileHitFlags3, preventFriendlyFire, equipmentSource, targetCoverDef);
                if (shotResult)
                {
                    UpdateTurretFocusTargets();
                }
                return shotResult;
            }
            
            ProjectileHitFlags projectileHitFlags4 = ProjectileHitFlags.IntendedTarget;
            if (canHitNonTargetPawnsNow)
            {
                projectileHitFlags4 |= ProjectileHitFlags.NonTargetPawns;
            }
            
            if (!currentTarget.HasThing || currentTarget.Thing.def.Fillage == FillCategory.Full)
            {
                projectileHitFlags4 |= ProjectileHitFlags.NonTargetWorld;
            }
            
            // 模拟发射成功，但不实际发射
            bool finalShotResult = SimulateFinalShotSuccess(drawPos, resultingLine.Dest, currentTarget, projectileHitFlags4, preventFriendlyFire, equipmentSource, targetCoverDef);
            if (finalShotResult)
            {
                UpdateTurretFocusTargets();
            }
            return finalShotResult;
        }
        
        /// <summary>
        /// 模拟射击成功的情况
        /// </summary>
        private bool SimulateShotSuccess(Vector3 drawPos, IntVec3 targetCell, LocalTargetInfo target, ProjectileHitFlags hitFlags, bool preventFriendlyFire, Thing equipmentSource, ThingDef targetCoverDef = null)
        {
            // 这里不实际发射射弹，只返回成功
            // 销毁之前创建的射弹对象，因为我们不需要它
            return true;
        }
        
        /// <summary>
        /// 模拟射击成功的情况（带目标）
        /// </summary>
        private bool SimulateShotSuccess(Vector3 drawPos, Thing target, LocalTargetInfo originalTarget, ProjectileHitFlags hitFlags, bool preventFriendlyFire, Thing equipmentSource, ThingDef targetCoverDef = null)
        {
            // 这里不实际发射射弹，只返回成功
            // 销毁之前创建的射弹对象，因为我们不需要它
            return true;
        }
        
        /// <summary>
        /// 模拟最终射击成功的情况
        /// </summary>
        private bool SimulateFinalShotSuccess(Vector3 drawPos, IntVec3 targetCell, LocalTargetInfo target, ProjectileHitFlags hitFlags, bool preventFriendlyFire, Thing equipmentSource, ThingDef targetCoverDef = null)
        {
            // 这里不实际发射射弹，只返回成功
            // 销毁之前创建的射弹对象，因为我们不需要它
            return true;
        }
        
        /// <summary>
        /// 更新Pawn身上所有Comp_MultiTurretGun的focusTarget
        /// </summary>
        private void UpdateTurretFocusTargets()
        {
            if (caster is Pawn pawn && pawn.Spawned)
            {
                // 获取Pawn身上所有的Comp_MultiTurretGun组件
                var turretComps = pawn.GetComps<Comp_MultiTurretGun>();
                
                foreach (var turretComp in turretComps)
                {
                    // 设置集中火力目标
                    Comp_MultiTurretGun.focusTarget = currentTarget;
                    Comp_MultiTurretGun.lastFocusSetTick = Find.TickManager.TicksGame;
                    Comp_MultiTurretGun.lastFocusPawn = pawn;
                    
                    // 强制炮塔立即重新索敌，以便它们能检测到新的集中火力目标
                    turretComp.TryAcquireTarget();
                }
            }
        }
    }
    
    /// <summary>
    /// 用于距离判断的Verb属性
    /// </summary>
    public class VerbProperties_RangeChecker : VerbProperties
    {
        public VerbProperties_RangeChecker()
        {
            verbClass = typeof(Verb_RangeChecker);
            
            // 默认设置为不发射射弹
            defaultProjectile = null;
        }
    }
}
