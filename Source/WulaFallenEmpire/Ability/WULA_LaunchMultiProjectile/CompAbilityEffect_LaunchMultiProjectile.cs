using System.Collections.Generic;
using Verse;
using RimWorld;
using UnityEngine;
using Verse.AI;

namespace WulaFallenEmpire
{
    public class CompAbilityEffect_LaunchMultiProjectile : CompAbilityEffect
    {
        private bool isActive;
        private int startTick;
        private int nextProjectileTick;
        private int projectilesFired;
        private IntVec3? targetCell;
        private bool parametersInitialized;
        
        // 缓存当前状态的参数
        private int currentNumProjectiles;
        private ThingDef currentProjectileDef;
        private float currentOffsetRadius;
        private int currentShotIntervalTicks;

        public new CompProperties_AbilityLaunchMultiProjectile Props => (CompProperties_AbilityLaunchMultiProjectile)props;

        public bool IsActive => isActive;
        public IntVec3? TargetCell => targetCell;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            
            if (parent.pawn == null || parent.pawn.MapHeld == null || !target.IsValid)
            {
                return;
            }

            // 初始化参数
            InitializeParameters();
            
            // 设置目标
            targetCell = target.Cell.ClampInsideMap(parent.pawn.MapHeld);
            
            // 开始发射
            isActive = true;
            startTick = Find.TickManager.TicksGame;
            nextProjectileTick = startTick + Mathf.Max(0, Props.startDelayTicks);
            projectilesFired = 0;
            
            // 如果是持续模式，需要启动持续Job
            if (Props.useSustainedJob)
            {
                StartSustainedJob(target);
            }
        }

        public override void CompTick()
        {
            base.CompTick();

            if (!isActive)
            {
                return;
            }

            // 终止条件检查
            if (parent.pawn == null || parent.pawn.Dead || !parent.pawn.Spawned || parent.pawn.MapHeld == null)
            {
                StopMultiProjectile();
                return;
            }

            // 检查是否还在持续施法状态
            if (Props.useSustainedJob && !IsPawnMaintainingJob())
            {
                StopMultiProjectile();
                return;
            }

            // 检查最大持续时间
            if (Props.maxSustainTicks > 0 && (Find.TickManager.TicksGame - startTick) >= Props.maxSustainTicks)
            {
                StopMultiProjectile();
                return;
            }

            // 检查是否已经发射完所有射弹
            if (projectilesFired >= currentNumProjectiles)
            {
                StopMultiProjectile();
                return;
            }

            int currentTick = Find.TickManager.TicksGame;
            
            // 发射下一发
            if (currentTick >= nextProjectileTick)
            {
                if (targetCell.HasValue)
                {
                    LaunchProjectile(new LocalTargetInfo(targetCell.Value));
                }
                
                projectilesFired++;
                
                // 如果还有剩余射弹，设置下一次发射时间
                if (projectilesFired < currentNumProjectiles)
                {
                    nextProjectileTick = currentTick + Mathf.Max(1, currentShotIntervalTicks);
                }
                else
                {
                    // 所有射弹已发射完毕
                    if (!Props.useSustainedJob)
                    {
                        StopMultiProjectile();
                    }
                }
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();

            Scribe_Values.Look(ref isActive, "isActive", false);
            Scribe_Values.Look(ref startTick, "startTick", 0);
            Scribe_Values.Look(ref nextProjectileTick, "nextProjectileTick", 0);
            Scribe_Values.Look(ref projectilesFired, "projectilesFired", 0);
            
            if (Scribe.mode == LoadSaveMode.PostLoadInit && isActive)
            {
                InitializeParameters();
            }
        }

        /// <summary>
        /// 初始化当前参数
        /// </summary>
        private void InitializeParameters()
        {
            if (parametersInitialized)
                return;

            var state = GetCurrentState();
            
            currentNumProjectiles = state?.numProjectiles ?? Props.numProjectiles;
            currentProjectileDef = state?.projectileDef ?? Props.projectileDef;
            currentOffsetRadius = state?.offsetRadius ?? Props.offsetRadius;
            currentShotIntervalTicks = state?.shotIntervalTicks ?? Props.shotIntervalTicks;
            
            parametersInitialized = true;
        }

        /// <summary>
        /// 启动持续发射Job
        /// </summary>
        private void StartSustainedJob(LocalTargetInfo target)
        {
            // 创建一个持续施法的工作
            Job job = JobMaker.MakeJob(Wula_JobDefOf.WULA_Launch_Proj, target);
            job.ability = parent;
            job.verbToUse = parent.verb;
            parent.pawn.jobs.StartJob(job, JobCondition.InterruptForced, null, false, true, null, null, false);
        }

        /// <summary>
        /// 检查pawn是否还在维持发射工作
        /// </summary>
        private bool IsPawnMaintainingJob()
        {
            if (parent.pawn?.jobs?.curJob == null)
                return false;

            var curJob = parent.pawn.jobs.curJob;
            return curJob.ability == parent && curJob.def != null && curJob.def.abilityCasting;
        }

        private void LaunchProjectile(LocalTargetInfo target)
        {
            if (currentProjectileDef == null)
                return;

            Pawn pawn = parent.pawn;
            LocalTargetInfo finalTarget = target;
            
            // 如果有偏移范围，计算偏移后的目标
            if (Props.useRandomOffset && currentOffsetRadius > 0)
            {
                finalTarget = GetOffsetTarget(target, currentOffsetRadius);
            }
            
            Projectile projectile = (Projectile)GenSpawn.Spawn(currentProjectileDef, pawn.Position, pawn.Map);
            projectile.Launch(pawn, pawn.DrawPos, finalTarget, finalTarget, 
                ProjectileHitFlags.IntendedTarget, parent.verb.preventFriendlyFire);
        }

        /// <summary>
        /// 停止多射弹发射
        /// </summary>
        public void StopMultiProjectile()
        {
            isActive = false;
            startTick = 0;
            nextProjectileTick = 0;
            projectilesFired = 0;
            targetCell = null;
            parametersInitialized = false;
        }

        /// <summary>
        /// 获取偏移后的目标点
        /// </summary>
        private LocalTargetInfo GetOffsetTarget(LocalTargetInfo originalTarget, float offsetRadius)
        {
            if (!Props.useRandomOffset || offsetRadius <= 0)
                return originalTarget;
                
            Vector3 basePos = originalTarget.Cell.ToVector3Shifted();
            Map map = parent.pawn.Map;
            
            // 生成随机偏移
            Vector3 offset = Vector3.zero;
            
            if (Props.offsetInLineOnly)
            {
                // 只在线条方向偏移（从施法者到目标的方向）
                Vector3 casterPos = parent.pawn.Position.ToVector3Shifted();
                Vector3 direction = (basePos - casterPos).normalized;
                
                // 使用offsetRange或offsetRadius
                float offsetDistance = Props.offsetRange.RandomInRange;
                if (Mathf.Abs(offsetDistance) < Props.minOffsetDistance)
                {
                    offsetDistance = Mathf.Sign(offsetDistance) * Props.minOffsetDistance;
                }
                
                offset = direction * offsetDistance;
            }
            else if (Props.offsetInCircle)
            {
                // 在圆形范围内随机偏移
                float angle = Rand.Range(0f, 360f);
                float distance = Rand.Range(0f, offsetRadius);
                
                // 确保最小距离
                distance = Mathf.Max(distance, Props.minOffsetDistance);
                
                offset = new Vector3(
                    Mathf.Cos(angle * Mathf.Deg2Rad) * distance,
                    0f,
                    Mathf.Sin(angle * Mathf.Deg2Rad) * distance
                );
            }
            else
            {
                // 在矩形范围内随机偏移
                offset = new Vector3(
                    Props.offsetRange.RandomInRange,
                    0f,
                    Props.offsetRange.RandomInRange
                );
                
                // 限制最大偏移距离
                if (offsetRadius > 0 && offset.magnitude > offsetRadius)
                {
                    offset = offset.normalized * offsetRadius;
                }
            }
            
            // 计算最终目标点
            IntVec3 targetCell = (basePos + offset).ToIntVec3();
            targetCell = targetCell.ClampInsideMap(map);
            
            return new LocalTargetInfo(targetCell);
        }

        /// <summary>
        /// 绘制效果预览（显示偏移范围）
        /// </summary>
        public override void DrawEffectPreview(LocalTargetInfo target)
        {
            base.DrawEffectPreview(target);
            
            float currentRadius = GetCurrentOffsetRadius();
            if (Props.useRandomOffset && currentRadius > 0)
            {
                // 绘制偏移范围
                GenDraw.DrawRadiusRing(target.Cell, currentRadius);
            }
        }

        public override bool AICanTargetNow(LocalTargetInfo target)
        {
            return target.Pawn != null;
        }

        #region 状态获取方法

        /// <summary>
        /// 获取当前状态的射弹数量
        /// </summary>
        private int GetCurrentNumProjectiles()
        {
            var state = GetCurrentState();
            return state?.numProjectiles ?? Props.numProjectiles;
        }

        /// <summary>
        /// 获取当前状态的射弹类型
        /// </summary>
        private ThingDef GetCurrentProjectileDef()
        {
            var state = GetCurrentState();
            return state?.projectileDef ?? Props.projectileDef;
        }

        /// <summary>
        /// 获取当前状态的偏移半径
        /// </summary>
        private float GetCurrentOffsetRadius()
        {
            var state = GetCurrentState();
            return state?.offsetRadius ?? Props.offsetRadius;
        }

        /// <summary>
        /// 获取当前状态的发射间隔
        /// </summary>
        private int GetCurrentShotIntervalTicks()
        {
            var state = GetCurrentState();
            return state?.shotIntervalTicks ?? Props.shotIntervalTicks;
        }

        /// <summary>
        /// 获取当前状态（基于施法者的Hediff）
        /// </summary>
        private ProjectileState GetCurrentState()
        {
            if (parent.pawn == null || parent.pawn.health?.hediffSet == null || Props.states == null)
                return null;

            // 检查施法者是否有匹配的Hediff
            foreach (var state in Props.states)
            {
                if (state.hediffDef != null && parent.pawn.health.hediffSet.HasHediff(state.hediffDef))
                {
                    return state;
                }
            }

            return null;
        }

        #endregion
    }
}
