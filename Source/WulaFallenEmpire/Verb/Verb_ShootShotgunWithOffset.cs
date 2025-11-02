using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    public class Verb_ShootShotgunWithOffset : Verb_Shoot
    {  
        protected override bool TryCastShot()
        {
            // Fire the first shot
            bool initialShotSuccess = this.BaseTryCastShot(0);
            if (initialShotSuccess && CasterIsPawn)
            {
                CasterPawn.records.Increment(RecordDefOf.ShotsFired);
            }

            // Get shotgun extension
            ShotgunExtension shotgunExtension = ShotgunExtension.Get(this.verbProps.defaultProjectile);
            if (initialShotSuccess && shotgunExtension != null && shotgunExtension.pelletCount > 1)
            {
                // Fire the rest of the pellets in a loop
                for (int i = 1; i < shotgunExtension.pelletCount; i++)
                {
                    this.BaseTryCastShot(i);
                }
            }
            return initialShotSuccess;
        }

        protected bool BaseTryCastShot(int pelletIndex)
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
            drawPos = ApplyProjectileOffset(drawPos, equipmentSource, pelletIndex);
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
                        if (projectile2.extraDamages == null)
                        {
                            projectile2.extraDamages = new List<ExtraDamage>();
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
                        projectile2.Launch(manningPawn, drawPos, forcedMissTarget, currentTarget, ProjectileHitFlags.All, preventFriendlyFire, equipmentSource);
                        return true;
                    }
                }
            }

            ShotReport shotReport = ShotReport.HitReportFor(caster, this, currentTarget);
            if (verbProps.canGoWild && !Rand.Chance(shotReport.AimOnTargetChance_IgnoringPosture))
            {
                bool flyOverhead = projectile2?.def?.projectile != null && projectile2.def.projectile.flyOverhead;
                resultingLine.ChangeDestToMissWild(shotReport.AimOnTargetChance_StandardTarget, flyOverhead, caster.Map);
                projectile2.Launch(manningPawn, drawPos, resultingLine.Dest, currentTarget, ProjectileHitFlags.NonTargetWorld, preventFriendlyFire, equipmentSource, shotReport.GetRandomCoverToMissInto()?.def);
                return true;
            }

            if (currentTarget.Thing != null && currentTarget.Thing.def.CanBenefitFromCover && !Rand.Chance(shotReport.PassCoverChance))
            {
                projectile2.Launch(manningPawn, drawPos, shotReport.GetRandomCoverToMissInto(), currentTarget, ProjectileHitFlags.NonTargetWorld, preventFriendlyFire, equipmentSource, shotReport.GetRandomCoverToMissInto()?.def);
                return true;
            }
            
            projectile2.Launch(manningPawn, drawPos, currentTarget, currentTarget, ProjectileHitFlags.IntendedTarget, preventFriendlyFire, equipmentSource, shotReport.GetRandomCoverToMissInto()?.def);
            return true;
        }

        private Vector3 ApplyProjectileOffset(Vector3 originalDrawPos, Thing equipmentSource, int pelletIndex)
        {
            if (equipmentSource != null)
            {
                ModExtension_ShootWithOffset offsetExtension = (base.EquipmentSource?.def)?.GetModExtension<ModExtension_ShootWithOffset>();

                if (offsetExtension != null && offsetExtension.offsets != null && offsetExtension.offsets.Count > 0)
                {
                    Vector2 offset = offsetExtension.GetOffsetFor(pelletIndex);

                    Vector3 targetPos = currentTarget.CenterVector3;
                    Vector3 casterPos = caster.DrawPos;
                    float rimworldAngle = targetPos.AngleToFlat(casterPos);
                    
                    float correctedAngle = -rimworldAngle - 90f;
                    
                    Vector2 rotatedOffset = offset.RotatedBy(correctedAngle);
                    
                    originalDrawPos += new Vector3(rotatedOffset.x, 0f, rotatedOffset.y);
                }
            }
            return originalDrawPos;
        }
    }
}