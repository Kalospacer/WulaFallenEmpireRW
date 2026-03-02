// File: Verb_TurretOffestShoot.cs
using RimWorld;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    /// <summary>
    /// 专门为Pawn身上的炮塔设计的Verb，根据PawnRenderNodeProperties中的drawData调整发射原点，并支持ModExtension_ShootWithOffset
    /// </summary>
    public class Verb_TurretOffestShoot : Verb_Shoot
    {
        // 缓存Comp_MultiTurretGun以减少查找次数
        private Comp_MultiTurretGun cachedTurretComp;
        private bool turretCompInitialized = false;
        
        // 缓存发射偏移
        private Vector3 cachedMuzzleOffset = Vector3.zero;
        private int lastUpdateTick = -1;
        private Rot4 lastPawnRotation = Rot4.Invalid;
        
        // 用于ModExtension_ShootWithOffset的字段
        public int offset = 0;
        
        /// <summary>
        /// 获取当前炮塔的发射点偏移
        /// </summary>
        private Vector3 GetTurretMuzzleOffset()
        {
            // 检查是否需要更新缓存
            if (!turretCompInitialized || Find.TickManager.TicksGame - lastUpdateTick > 60)
            {
                UpdateTurretCompCache();
            }
            
            // 检查Pawn朝向是否改变
            if (caster != null && caster is Pawn pawnPawn)
            {
                if (pawnPawn.Rotation != lastPawnRotation)
                {
                    UpdateMuzzleOffsetCache();
                }
            }
            
            return cachedMuzzleOffset;
        }
        
        /// <summary>
        /// 更新炮塔组件缓存
        /// </summary>
        private void UpdateTurretCompCache()
        {
            cachedTurretComp = null;
            
            if (caster == null || base.EquipmentSource == null)
                return;
            
            // 使用GetComp<T>方法替代GetComps<T>
            if (caster is ThingWithComps thingWithComps)
            {
                // 获取所有Comp_MultiTurretGun组件
                foreach (ThingComp comp in thingWithComps.AllComps)
                {
                    if (comp is Comp_MultiTurretGun turretComp && turretComp.gun == base.EquipmentSource)
                    {
                        cachedTurretComp = turretComp;
                        break;
                    }
                }
            }
            
            turretCompInitialized = true;
            UpdateMuzzleOffsetCache();
        }
        
        /// <summary>
        /// 更新发射偏移缓存
        /// </summary>
        private void UpdateMuzzleOffsetCache()
        {
            cachedMuzzleOffset = Vector3.zero;
            lastUpdateTick = Find.TickManager.TicksGame;
            
            if (cachedTurretComp == null)
                return;
                
            // 获取炮塔属性
            var props = cachedTurretComp.Props as CompProperties_MultiTurretGun;
            if (props == null || props.renderNodeProperties.NullOrEmpty())
                return;
                
            // 获取Pawn当前朝向
            if (caster is Pawn pawn)
            {
                lastPawnRotation = pawn.Rotation;
                
                // 获取第一个渲染节点属性
                var renderNodeProps = props.renderNodeProperties[0];
                
                // 使用DrawData的OffsetForRot方法获取偏移
                if (renderNodeProps.drawData != null)
                {
                    cachedMuzzleOffset = renderNodeProps.drawData.OffsetForRot(pawn.Rotation);
                }
            }
        }
        
        /// <summary>
        /// 重写射击方法，应用炮塔偏移和ModExtension_ShootWithOffset
        /// </summary>
        protected override bool TryCastShot()
        {
            // 获取炮塔偏移
            Vector3 turretOffset = GetTurretMuzzleOffset();
            
            // 结合ModExtension_ShootWithOffset的偏移
            return TryCastShotWithCombinedOffset(turretOffset);
        }
        
        /// <summary>
        /// 应用组合偏移的射击方法
        /// </summary>
        private bool TryCastShotWithCombinedOffset(Vector3 baseTurretOffset)
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

            // 关键修改：应用炮塔的发射点偏移和ModExtension_ShootWithOffset的偏移
            Vector3 drawPos = caster.DrawPos;
            drawPos += baseTurretOffset; // 先应用炮塔偏移
            
            // 然后应用ModExtension_ShootWithOffset的偏移
            drawPos = ApplyProjectileOffset(drawPos, equipmentSource);

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

                        projectile2.Launch(manningPawn, drawPos, forcedMissTarget, currentTarget, projectileHitFlags, preventFriendlyFire, equipmentSource);
                        return true;
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

                projectile2.Launch(manningPawn, drawPos, resultingLine.Dest, currentTarget, projectileHitFlags2, preventFriendlyFire, equipmentSource, targetCoverDef);
                return true;
            }

            if (currentTarget.Thing != null && currentTarget.Thing.def.CanBenefitFromCover && !Rand.Chance(shotReport.PassCoverChance))
            {
                ProjectileHitFlags projectileHitFlags3 = ProjectileHitFlags.NonTargetWorld;
                if (canHitNonTargetPawnsNow)
                {
                    projectileHitFlags3 |= ProjectileHitFlags.NonTargetPawns;
                }

                projectile2.Launch(manningPawn, drawPos, randomCoverToMissInto, currentTarget, projectileHitFlags3, preventFriendlyFire, equipmentSource, targetCoverDef);
                return true;
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
            
            if (currentTarget.Thing != null)
            {
                projectile2.Launch(manningPawn, drawPos, currentTarget, currentTarget, projectileHitFlags4, preventFriendlyFire, equipmentSource, targetCoverDef);
            }
            else
            {
                projectile2.Launch(manningPawn, drawPos, resultingLine.Dest, currentTarget, projectileHitFlags4, preventFriendlyFire, equipmentSource, targetCoverDef);
            }
            
            if (CasterIsPawn)
            {
                CasterPawn.records.Increment(RecordDefOf.ShotsFired);
            }
            
            return true;
        }
        
        /// <summary>
        /// 应用ModExtension_ShootWithOffset的偏移（从Verb_ShootWithOffset复制）
        /// </summary>
        private Vector3 ApplyProjectileOffset(Vector3 originalDrawPos, Thing equipmentSource)
        {
            if (equipmentSource != null)
            {
                // 获取投射物偏移的模组扩展
                ModExtension_ShootWithOffset offsetExtension =
                    equipmentSource.def.GetModExtension<ModExtension_ShootWithOffset>();

                if (offsetExtension != null && offsetExtension.offsets != null && offsetExtension.offsets.Count > 0)
                {
                    // 获取当前连发射击的剩余次数
                    int burstShotsLeft = GetBurstShotsLeft();

                    // 计算从发射者到目标的角度
                    Vector3 targetPos = currentTarget.CenterVector3;
                    Vector3 casterPos = originalDrawPos; // 使用已经应用了炮塔偏移的位置
                    float rimworldAngle = targetPos.AngleToFlat(casterPos);

                    // 将RimWorld角度转换为适合偏移计算的角度
                    float correctedAngle = ConvertRimWorldAngleToOffsetAngle(rimworldAngle);

                    // 应用偏移并旋转到正确方向
                    Vector2 offset = offsetExtension.GetOffsetFor(burstShotsLeft);
                    Vector2 rotatedOffset = offset.RotatedBy(correctedAngle);

                    // 将2D偏移转换为3D并应用到绘制位置
                    originalDrawPos += new Vector3(rotatedOffset.x, 0f, rotatedOffset.y);
                }
            }

            return originalDrawPos;
        }

        /// <summary>
        /// 获取当前连发射击剩余次数（从Verb_ShootWithOffset复制）
        /// </summary>
        /// <returns>连发射击剩余次数</returns>
        private int GetBurstShotsLeft()
        {
            if (burstShotsLeft >= 0)
            {
                return (int)burstShotsLeft;
            }
            return 0;
        }

        /// <summary>
        /// 将RimWorld角度转换为偏移计算用的角度（从Verb_ShootWithOffset复制）
        /// RimWorld使用顺时针角度系统，需要转换为标准的数学角度系统
        /// </summary>
        /// <param name="rimworldAngle">RimWorld角度</param>
        /// <returns>转换后的角度</returns>
        private float ConvertRimWorldAngleToOffsetAngle(float rimworldAngle)
        {
            // RimWorld角度：0°=东，90°=北，180°=西，270°=南
            // 转换为：0°=东，90°=南，180°=西，270°=北
            return -rimworldAngle - 90f;
        }
        
        /// <summary>
        /// 用于调试：在Gizmo模式下显示发射点偏移
        /// </summary>
        public override void DrawHighlight(LocalTargetInfo target)
        {
            base.DrawHighlight(target);
            
            // 在调试模式下显示发射点
            if (DebugSettings.godMode && caster != null && caster.Spawned)
            {
                // 获取炮塔偏移
                Vector3 turretOffset = GetTurretMuzzleOffset();
                
                // 获取ModExtension_ShootWithOffset的偏移
                Vector3 modExtensionOffset = Vector3.zero;
                if (base.EquipmentSource != null)
                {
                    var offsetExtension = base.EquipmentSource.def.GetModExtension<ModExtension_ShootWithOffset>();
                    if (offsetExtension != null && offsetExtension.offsets != null && offsetExtension.offsets.Count > 0)
                    {
                        int burstShotsLeft = GetBurstShotsLeft();
                        Vector2 offset2D = offsetExtension.GetOffsetFor(burstShotsLeft);
                        
                        if (target.IsValid)
                        {
                            Vector3 targetPos = target.CenterVector3;
                            Vector3 casterPos = caster.DrawPos + turretOffset;
                            float rimworldAngle = targetPos.AngleToFlat(casterPos);
                            float correctedAngle = ConvertRimWorldAngleToOffsetAngle(rimworldAngle);
                            Vector2 rotatedOffset = offset2D.RotatedBy(correctedAngle);
                            modExtensionOffset = new Vector3(rotatedOffset.x, 0f, rotatedOffset.y);
                        }
                    }
                }
                
                // 计算最终发射点位置
                Vector3 finalMuzzlePos = caster.DrawPos + turretOffset + modExtensionOffset;
                
                // 绘制偏移可视化
                if (turretOffset != Vector3.zero)
                {
                    Vector3 turretMuzzlePos = caster.DrawPos + turretOffset;
                    GenDraw.DrawLineBetween(caster.DrawPos, turretMuzzlePos, SimpleColor.Red);
                    GenDraw.DrawRadiusRing(turretMuzzlePos.ToIntVec3(), 0.15f, Color.red);
                }
                
                if (modExtensionOffset != Vector3.zero)
                {
                    Vector3 turretMuzzlePos = caster.DrawPos + turretOffset;
                    GenDraw.DrawLineBetween(turretMuzzlePos, finalMuzzlePos, SimpleColor.Blue);
                    GenDraw.DrawRadiusRing(finalMuzzlePos.ToIntVec3(), 0.2f, Color.blue);
                }
                else if (turretOffset != Vector3.zero)
                {
                    GenDraw.DrawRadiusRing(finalMuzzlePos.ToIntVec3(), 0.2f, Color.red);
                }
            }
        }
    }
    
    /// <summary>
    /// 专门用于炮塔的Verb属性
    /// </summary>
    public class VerbProperties_TurretShootWithOffset : VerbProperties
    {
        public VerbProperties_TurretShootWithOffset()
        {
            verbClass = typeof(Verb_TurretOffestShoot);
        }
    }
}
