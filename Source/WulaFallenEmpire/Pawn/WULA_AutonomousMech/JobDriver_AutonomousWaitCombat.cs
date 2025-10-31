// JobDriver_AutonomousWaitCombat.cs
using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    public class JobDriver_AutonomousWaitCombat : JobDriver
    {
        private const int TargetSearchInterval = 4;
        private bool collideWithPawns;

        private JobExtension_AutonomousCombat CombatExtension => 
            job.def.GetModExtension<JobExtension_AutonomousCombat>();

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            Toil waitToil = new Toil();
            waitToil.initAction = delegate
            {
                if (pawn.Spawned)
                {
                    pawn.Map.pawnDestinationReservationManager.Reserve(pawn, job, pawn.Position);
                    pawn.pather?.StopDead();
                }
                
                // 初始化战斗设置
                InitializeCombatSettings();
                
                // 立即检查攻击目标
                CheckForAutoAttack();
            };

            waitToil.tickAction = delegate
            {
                // 定期检查攻击目标
                if (Find.TickManager.TicksGame % TargetSearchInterval == 0)
                {
                    CheckForAutoAttack();
                }
                
                // 处理朝向
                HandleFacing();
            };

            waitToil.defaultCompleteMode = ToilCompleteMode.Never;
            yield return waitToil;
        }

        private void InitializeCombatSettings()
        {
            var comp = pawn.GetComp<CompAutonomousMech>();
            if (comp?.CanWorkAutonomously == true)
            {
                // 确保征召设置正确
                if (pawn.drafter != null)
                {
                    pawn.drafter.FireAtWill = CombatExtension?.forceFireAtWill ?? true;
                }
            }
        }

        private void HandleFacing()
        {
            // 如果有指定朝向，就面向该方向
            if (job.overrideFacing != Rot4.Invalid)
            {
                pawn.rotationTracker.FaceTarget(pawn.Position + job.overrideFacing.FacingCell);
                return;
            }

            // 如果有职责焦点，就面向焦点
            if (pawn.mindState?.duty?.focus != null)
            {
                pawn.rotationTracker.FaceTarget(pawn.mindState.duty.focus);
                return;
            }

            // 自动寻找敌人并面向它
            Thing enemyTarget = FindNearestEnemy();
            if (enemyTarget != null)
            {
                pawn.rotationTracker.FaceTarget(enemyTarget);
            }
        }

        private void CheckForAutoAttack()
        {
            if (!CanAutoAttack())
                return;

            // 先检查近战攻击
            if (CheckMeleeAttack())
                return;

            // 再检查远程攻击
            CheckRangedAttack();
        }

        private bool CanAutoAttack()
        {
            // 基础状态检查
            if (pawn.Downed || pawn.stances.FullBodyBusy || pawn.IsCarryingPawn())
                return false;

            var comp = pawn.GetComp<CompAutonomousMech>();
            if (comp?.CanWorkAutonomously != true)
                return false;

            // 检查扩展设置
            if (CombatExtension?.autoAttackEnabled != true)
                return false;

            // 忽略工作标签检查，因为这是独立系统
            if (!CombatExtension.ignoreWorkTags)
            {
                if (pawn.WorkTagIsDisabled(WorkTags.Violent))
                    return false;
            }

            return true;
        }

        private bool CheckMeleeAttack()
        {
            if (!pawn.kindDef.canMeleeAttack)
                return false;

            // 检查相邻格子的敌人
            for (int i = 0; i < 9; i++)
            {
                IntVec3 cell = pawn.Position + GenAdj.AdjacentCellsAndInside[i];
                if (!cell.InBounds(pawn.Map))
                    continue;

                foreach (Thing thing in cell.GetThingList(pawn.Map))
                {
                    if (thing is Pawn targetPawn && IsValidMeleeTarget(targetPawn))
                    {
                        pawn.meleeVerbs.TryMeleeAttack(targetPawn);
                        collideWithPawns = true;
                        return true;
                    }
                }
            }

            return false;
        }

        private bool IsValidMeleeTarget(Pawn target)
        {
            return !target.ThreatDisabled(pawn) && 
                   pawn.HostileTo(target) &&
                   GenHostility.IsActiveThreatTo(target, pawn.Faction) &&
                   !pawn.ThreatDisabledBecauseNonAggressiveRoamer(target);
        }

        private void CheckRangedAttack()
        {
            if (!CanUseRangedWeapon())
                return;

            Verb verb = pawn.CurrentEffectiveVerb;
            if (verb == null || verb.verbProps.IsMeleeAttack)
                return;

            Thing shootTarget = FindRangedTarget();
            if (shootTarget != null)
            {
                pawn.TryStartAttack(shootTarget);
                collideWithPawns = true;
            }
        }

        private bool CanUseRangedWeapon()
        {
            if (!CombatExtension?.canUseRangedWeapon ?? false)
                return false;

            // 检查武器
            if (pawn.equipment?.Primary == null || !pawn.equipment.Primary.def.IsRangedWeapon)
                return false;

            // 检查 FireAtWill 设置
            if (pawn.drafter != null && !pawn.drafter.FireAtWill)
                return false;

            return true;
        }

        private Thing FindRangedTarget()
        {
            int searchRadius = CombatExtension?.attackSearchRadius ?? 25;
            
            TargetScanFlags flags = TargetScanFlags.NeedLOSToAll | 
                                   TargetScanFlags.NeedThreat | 
                                   TargetScanFlags.NeedAutoTargetable;

            if (pawn.CurrentEffectiveVerb?.IsIncendiary_Ranged() == true)
            {
                flags |= TargetScanFlags.NeedNonBurning;
            }

            return (Thing)AttackTargetFinder.BestShootTargetFromCurrentPosition(
                pawn, flags, null, 0f, searchRadius);
        }

        private Thing FindNearestEnemy()
        {
            int searchRadius = CombatExtension?.attackSearchRadius ?? 25;
            
            return (Thing)AttackTargetFinder.BestAttackTarget(
                pawn,
                TargetScanFlags.NeedThreat,
                x => x is Pawn p && pawn.HostileTo(p),
                0f, searchRadius,
                default,
                float.MaxValue,
                canTakeTargets: true);
        }

        public override string GetReport()
        {
            return "WULA_StandingGuard".Translate(); // 自定义报告文本
        }
    }
}
