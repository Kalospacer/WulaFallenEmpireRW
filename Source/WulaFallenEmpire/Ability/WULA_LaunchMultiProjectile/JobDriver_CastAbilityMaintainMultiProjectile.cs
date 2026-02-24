using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    public class JobDriver_CastAbilityMaintainMultiProjectile : JobDriver_CastAbility
    {
        private CompAbilityEffect_LaunchMultiProjectile MultiProjectileComp
        {
            get
            {
                if (job?.ability?.EffectComps == null)
                {
                    return null;
                }

                foreach (var comp in job.ability.EffectComps)
                {
                    if (comp is CompAbilityEffect_LaunchMultiProjectile multiComp)
                    {
                        return multiComp;
                    }
                }
                return null;
            }
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => job.ability == null || (!job.ability.CanCast && !job.ability.Casting));
            
            AddFinishAction(delegate
            {
                if (job.ability != null && job.def.abilityCasting)
                {
                    job.ability.StartCooldown(job.ability.def.cooldownTicksRange.RandomInRange);
                }
                
                // 停止多射弹发射
                MultiProjectileComp?.StopMultiProjectile();
            });

            // 停止移动
            Toil stopToil = ToilMaker.MakeToil("StopBeforeMultiProjectileCast");
            stopToil.initAction = delegate
            {
                pawn.pather.StopDead();
            };
            stopToil.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return stopToil;

            // 施法动作
            Toil castToil = Toils_Combat.CastVerb(TargetIndex.A, TargetIndex.B, canHitNonTargetPawns: false);
            if (job.ability != null && job.ability.def.showCastingProgressBar && job.verbToUse != null)
            {
                castToil.WithProgressBar(TargetIndex.A, () => job.verbToUse.WarmupProgress);
            }
            yield return castToil;

            // 持续发射阶段
            Toil maintainToil = ToilMaker.MakeToil("MaintainMultiProjectile");
            maintainToil.initAction = delegate
            {
                pawn.pather.StopDead();
                rotateToFace = TargetIndex.A;
                
                // 如果组件有目标单元格，更新job的目标
                var multiComp = MultiProjectileComp;
                if (multiComp != null && multiComp.TargetCell.HasValue)
                {
                    job.targetA = new LocalTargetInfo(multiComp.TargetCell.Value);
                }
            };
            maintainToil.tickAction = delegate
            {
                pawn.pather.StopDead();

                var multiComp = MultiProjectileComp;
                if (multiComp == null || !multiComp.IsActive)
                {
                    EndJobWith(JobCondition.Succeeded);
                    return;
                }

                // 更新目标（如果有必要）- 修正类型转换
                if (multiComp.TargetCell.HasValue && multiComp.TargetCell.Value.IsValid)
                {
                    // 将 IntVec3? 转换为 LocalTargetInfo
                    job.targetA = new LocalTargetInfo(multiComp.TargetCell.Value);
                }

                // 继续发射射弹（通过组件的Tick方法）
                // 组件会在其CompTick中处理发射逻辑
            };
            
            
            maintainToil.FailOn(() => pawn.Dead || pawn.Downed || !pawn.Spawned);
            maintainToil.handlingFacing = true;
            maintainToil.defaultCompleteMode = ToilCompleteMode.Never;
            yield return maintainToil;
        }

        public override string GetReport()
        {
            if (job?.ability != null)
            {
                return "UsingVerbNoTarget".Translate(job.verbToUse.ReportLabel);
            }

            return base.GetReport();
        }
    }
}
