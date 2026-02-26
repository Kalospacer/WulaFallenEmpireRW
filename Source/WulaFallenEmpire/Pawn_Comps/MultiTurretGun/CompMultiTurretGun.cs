using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;
using RimWorld;
using UnityEngine;

namespace WulaFallenEmpire
{
    public class CompProperties_MultiTurretGun : CompProperties_TurretGun
    {
        public int ID;
        public float traverseSpeed = 30f; // 旋转速度（度/秒），默认30度/秒
        public float aimTicks = 15;       // 瞄准所需tick数
        public float idleRotationSpeed = 5f; // 空闲时的旋转速度（度/秒）
        public bool smoothRotation = true; // 是否启用平滑旋转
        public float minAimAngle = 10f;    // 开始预热所需的最小瞄准角度差（度）
        public int resetCooldownTicks = 120; // 开火后的复位冷却时间（默认2秒）
        
        public CompProperties_MultiTurretGun()
        {
            compClass = typeof(Comp_MultiTurretGun);
        }
    }
    
    public class Comp_MultiTurretGun : CompTurretGun
    {
        private bool fireAtWill = true;
        private float currentRotationAngle; // 当前实际旋转角度
        private float targetRotationAngle;  // 目标旋转角度
        private float rotationVelocity;     // 旋转速度（用于平滑插值）
        private int ticksWithoutTarget = 0; // 没有目标的tick计数
        private bool isIdleRotating = false; // 是否处于空闲旋转状态
        private float idleRotationDirection = 1f; // 空闲旋转方向
        private bool isAiming = false; // 是否正在瞄准
        private float lastBaseRotationAngle; // 上一次记录的基座角度
        private float lastTargetAngle; // 最后一次目标角度
        
        // 添加缺失的字段
        private LocalTargetInfo lastAttackedTarget = LocalTargetInfo.Invalid;
        private int lastAttackTargetTick;
        
        // Gizmo相关静态字段
        private static readonly CachedTexture ToggleTurretIcon = new CachedTexture("UI/Gizmos/ToggleTurret");
        private static bool gizmoAdded = false; // 防止重复添加Gizmo
        
        // 复位冷却相关
        private int resetCooldownTicksLeft = 0;
        private bool isInResetCooldown = false;
        
        public new CompProperties_MultiTurretGun Props => (CompProperties_MultiTurretGun)props;
        
        // 添加属性
        private bool WarmingUp => burstWarmupTicksLeft > 0;
        
        public override void Initialize(CompProperties props)
        {
            base.Initialize(props);
            // 初始化旋转角度为建筑方向
            currentRotationAngle = parent.Rotation.AsAngle + Props.angleOffset;
            targetRotationAngle = currentRotationAngle;
            lastBaseRotationAngle = parent.Rotation.AsAngle;
            lastTargetAngle = currentRotationAngle;
        }
        
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (!respawningAfterLoad)
            {
                currentRotationAngle = parent.Rotation.AsAngle + Props.angleOffset;
                targetRotationAngle = currentRotationAngle;
                lastBaseRotationAngle = parent.Rotation.AsAngle;
                lastTargetAngle = currentRotationAngle;
            }
            gizmoAdded = false; // 重置Gizmo状态
        }
        
        private bool CanShoot
        {
            get
            {
                if (parent is Pawn pawn)
                {
                    if (!pawn.Spawned || pawn.Downed || pawn.Dead || !pawn.Awake())
                    {
                        return false;
                    }
                    if (pawn.stances.stunner.Stunned)
                    {
                        return false;
                    }
                    if (TurretDestroyed)
                    {
                        return false;
                    }
                    if (pawn.IsColonyMechPlayerControlled && !fireAtWill)
                    {
                        return false;
                    }
                }
                CompCanBeDormant compCanBeDormant = parent.TryGetComp<CompCanBeDormant>();
                if (compCanBeDormant != null && !compCanBeDormant.Awake)
                {
                    return false;
                }
                return true;
            }
        }

        public override void CompTick()
        {
            if (!CanShoot)
            {
                ResetCurrentTarget();
                return;
            }
            
            // 检查Pawn是否转向
            CheckPawnRotationChange();
            
            // 更新炮塔旋转（必须在目标检查之前）
            UpdateTurretRotation();
            
            // 处理VerbTick
            AttackVerb.VerbTick();
            if (AttackVerb.state == VerbState.Bursting)
            {
                return;
            }
            
            // 更新复位冷却
            UpdateResetCooldown();
            
            // 如果正在预热
            if (WarmingUp)
            {
                // 检查是否仍然瞄准目标
                if (currentTarget.IsValid && IsAimingAtTarget())
                {
                    burstWarmupTicksLeft--;
                    if (burstWarmupTicksLeft == 0)
                    {
                        // 确保炮塔已经瞄准目标
                        if (IsAimedAtTarget())
                        {
                            AttackVerb.TryStartCastOn(currentTarget, surpriseAttack: false, canHitNonTargetPawns: true, preventFriendlyFire: false, nonInterruptingSelfCast: true);
                            lastAttackTargetTick = Find.TickManager.TicksGame;
                            lastAttackedTarget = currentTarget;
                            
                            // 开火后重置目标，并启动复位冷却
                            ResetCurrentTarget();
                            StartResetCooldown();
                        }
                        else
                        {
                            // 如果没有瞄准，重置预热
                            burstWarmupTicksLeft = 1;
                        }
                    }
                }
                else
                {
                    // 失去目标或失去瞄准，重置预热
                    ResetCurrentTarget();
                }
                return;
            }
            
            // 冷却中
            if (burstCooldownTicksLeft > 0)
            {
                burstCooldownTicksLeft--;
            }
            
            // 冷却结束且复位冷却结束，尝试寻找目标
            if (burstCooldownTicksLeft <= 0 && !isInResetCooldown && parent.IsHashIntervalTick(10))
            {
                TryAcquireTarget();
            }
            
            // 更新空闲旋转
            UpdateIdleRotation();
        }
        
        private void UpdateResetCooldown()
        {
            if (isInResetCooldown && resetCooldownTicksLeft > 0)
            {
                resetCooldownTicksLeft--;
                if (resetCooldownTicksLeft <= 0)
                {
                    isInResetCooldown = false;
                    resetCooldownTicksLeft = 0;
                }
            }
        }
        
        private void StartResetCooldown()
        {
            resetCooldownTicksLeft = Props.resetCooldownTicks;
            isInResetCooldown = true;
            ticksWithoutTarget = 0; // 重置无目标计数
        }
        
        private void CheckPawnRotationChange()
        {
            // 如果父物体是Pawn，检查其朝向是否改变
            if (parent is Pawn pawn)
            {
                float currentBaseRotation = pawn.Rotation.AsAngle;
                
                // 如果朝向改变超过5度，立即更新基座方向
                if (Mathf.Abs(Mathf.DeltaAngle(currentBaseRotation, lastBaseRotationAngle)) > 5f)
                {
                    // 立即调整炮塔角度，跟上Pawn的转向
                    currentRotationAngle += Mathf.DeltaAngle(lastBaseRotationAngle, currentBaseRotation);
                    targetRotationAngle = currentRotationAngle;
                    lastBaseRotationAngle = currentBaseRotation;
                    
                    // 如果有目标，重新计算目标角度
                    if (currentTarget.IsValid)
                    {
                        Vector3 targetPos = currentTarget.CenterVector3;
                        Vector3 turretPos = parent.DrawPos;
                        targetRotationAngle = (targetPos - turretPos).AngleFlat();
                    }
                }
            }
        }
        
        private void TryAcquireTarget()
        {
            // 如果正在复位冷却中，不寻找目标
            if (isInResetCooldown)
                return;
                
            // 尝试寻找新目标
            LocalTargetInfo newTarget = (Thing)AttackTargetFinder.BestShootTargetFromCurrentPosition(
                this, 
                TargetScanFlags.NeedThreat | TargetScanFlags.NeedAutoTargetable
            );
            
            if (newTarget.IsValid)
            {
                // 如果已经有目标并且是同一个目标，保持当前状态
                if (currentTarget.IsValid && currentTarget.Thing == newTarget.Thing)
                {
                    // 检查是否已经瞄准目标
                    if (IsAimedAtTarget())
                    {
                        // 如果已经瞄准，开始预热
                        if (burstWarmupTicksLeft <= 0)
                        {
                            burstWarmupTicksLeft = Mathf.Max(1, Mathf.RoundToInt(Props.aimTicks));
                        }
                    }
                    else if (IsAimingAtTarget())
                    {
                        // 如果正在瞄准但未完成，保持当前状态
                        // 什么也不做，继续旋转
                    }
                    else
                    {
                        // 需要重新瞄准
                        burstWarmupTicksLeft = 0;
                    }
                }
                else
                {
                    // 新目标，重置状态
                    currentTarget = newTarget;
                    burstWarmupTicksLeft = 0;
                    isAiming = true;
                    
                    // 计算目标角度
                    Vector3 targetPos = currentTarget.CenterVector3;
                    Vector3 turretPos = parent.DrawPos;
                    targetRotationAngle = (targetPos - turretPos).AngleFlat();
                    lastTargetAngle = targetRotationAngle;
                }
            }
            else
            {
                // 没有目标
                ResetCurrentTarget();
            }
        }
        
        private bool IsAimingAtTarget()
        {
            if (!currentTarget.IsValid)
                return false;
                
            // 计算当前角度与目标角度的差值
            float angleDiff = Mathf.Abs(Mathf.DeltaAngle(currentRotationAngle, targetRotationAngle));
            
            // 如果角度差小于最小瞄准角度，认为正在瞄准
            return angleDiff <= Props.minAimAngle;
        }
        
        private bool IsAimedAtTarget()
        {
            if (!currentTarget.IsValid)
                return false;
                
            // 计算当前角度与目标角度的差值
            float angleDiff = Mathf.Abs(Mathf.DeltaAngle(currentRotationAngle, targetRotationAngle));
            
            // 如果角度差小于2度，认为已经瞄准
            return angleDiff <= 2f;
        }
        
        private void UpdateTurretRotation()
        {
            if (!Props.smoothRotation)
            {
                // 非平滑旋转：直接设置角度
                if (currentTarget.IsValid)
                {
                    Vector3 targetPos = currentTarget.CenterVector3;
                    Vector3 turretPos = parent.DrawPos;
                    curRotation = (targetPos - turretPos).AngleFlat() + Props.angleOffset;
                }
                else
                {
                    curRotation = parent.Rotation.AsAngle + Props.angleOffset;
                }
                return;
            }
            
            // 平滑旋转逻辑
            if (currentTarget.IsValid)
            {
                // 有目标时，计算朝向目标的角度
                Vector3 targetPos = currentTarget.CenterVector3;
                Vector3 turretPos = parent.DrawPos;
                targetRotationAngle = (targetPos - turretPos).AngleFlat();
                ticksWithoutTarget = 0;
                isIdleRotating = false;
                lastTargetAngle = targetRotationAngle;
            }
            else
            {
                // 没有目标时，朝向初始角度
                targetRotationAngle = parent.Rotation.AsAngle + Props.angleOffset;
                ticksWithoutTarget++;
                lastTargetAngle = targetRotationAngle;
            }
            
            // 确保角度在0-360范围内
            targetRotationAngle = targetRotationAngle % 360f;
            if (targetRotationAngle < 0f)
                targetRotationAngle += 360f;
                
            // 计算角度差（使用DeltaAngle处理360度循环）
            float angleDiff = Mathf.DeltaAngle(currentRotationAngle, targetRotationAngle);
            
            // 如果角度差很小，直接设置到目标角度
            if (Mathf.Abs(angleDiff) < 0.1f)
            {
                currentRotationAngle = targetRotationAngle;
                curRotation = currentRotationAngle;
                return;
            }
            
            // 计算最大旋转速度（度/秒）
            float maxRotationSpeed = Props.traverseSpeed;
            
            // 如果有目标且在预热，根据预热进度调整旋转速度
            if (currentTarget.IsValid && burstWarmupTicksLeft > 0)
            {
                float warmupProgress = 1f - (float)burstWarmupTicksLeft / (float)Math.Max(Props.aimTicks, 1f);
                
                // 预热初期快速旋转，接近完成时减速
                if (warmupProgress < 0.3f)
                {
                    maxRotationSpeed *= 1.8f; // 初期更快
                }
                else if (warmupProgress > 0.7f)
                {
                    maxRotationSpeed *= 0.5f; // 后期更慢，精确瞄准
                }
            }
            
            // 转换为每tick的旋转速度
            float maxRotationPerTick = maxRotationSpeed / 60f;
            
            // 根据角度差调整旋转速度
            float rotationSpeedMultiplier = Mathf.Clamp(Mathf.Abs(angleDiff) / 45f, 0.5f, 1.5f);
            maxRotationPerTick *= rotationSpeedMultiplier;
            
            // 计算这一帧应该旋转的角度
            float rotationThisTick = Mathf.Clamp(angleDiff, -maxRotationPerTick, maxRotationPerTick);
            
            // 应用旋转
            currentRotationAngle += rotationThisTick;
            
            // 确保角度在0-360范围内
            currentRotationAngle = currentRotationAngle % 360f;
            if (currentRotationAngle < 0f)
                currentRotationAngle += 360f;
                
            // 更新基类的curRotation
            curRotation = currentRotationAngle;
        }
        
        private void UpdateIdleRotation()
        {
            // 只在没有目标、冷却结束且复位冷却结束时执行空闲旋转
            if (!currentTarget.IsValid && burstCooldownTicksLeft <= 0 && !isInResetCooldown && ticksWithoutTarget > 120)
            {
                if (!isIdleRotating)
                {
                    isIdleRotating = true;
                    idleRotationDirection = Rand.Value > 0.5f ? 1f : -1f;
                }
                
                // 缓慢旋转
                float idleRotationPerTick = Props.idleRotationSpeed / 60f * idleRotationDirection;
                currentRotationAngle += idleRotationPerTick;
                
                // 确保角度在0-360范围内
                currentRotationAngle = currentRotationAngle % 360f;
                if (currentRotationAngle < 0f)
                    currentRotationAngle += 360f;
                    
                // 更新基类的curRotation
                curRotation = currentRotationAngle;
                
                // 每30tick随机可能改变方向
                if (Find.TickManager.TicksGame % 30 == 0 && Rand.Value < 0.1f)
                {
                    idleRotationDirection *= -1f;
                }
            }
            else
            {
                isIdleRotating = false;
            }
        }
        
        private void ResetCurrentTarget()
        {
            currentTarget = LocalTargetInfo.Invalid;
            burstWarmupTicksLeft = 0;
            isAiming = false;
        }
        
        public override void PostDraw()
        {
            base.PostDraw();
            
            // 如果有目标且正在预热，绘制瞄准线
            if (currentTarget.IsValid && burstWarmupTicksLeft > 0)
            {
                DrawTargetingLine();
            }
        }
        
        private void DrawTargetingLine()
        {
            Vector3 lineStart = parent.DrawPos;
            lineStart.y = AltitudeLayer.MetaOverlays.AltitudeFor();
            
            Vector3 lineEnd = currentTarget.CenterVector3;
            lineEnd.y = AltitudeLayer.MetaOverlays.AltitudeFor();
            
            // 计算瞄准精度
            float aimAccuracy = 1f - Mathf.Abs(Mathf.DeltaAngle(currentRotationAngle, targetRotationAngle)) / 45f;
            aimAccuracy = Mathf.Clamp01(aimAccuracy);
            
            // 根据瞄准精度改变线条颜色
            Color lineColor = Color.Lerp(Color.yellow, Color.red, aimAccuracy);
            lineColor.a = 0.7f;
            
            GenDraw.DrawLineBetween(lineStart, lineEnd);
        }
        
        
        private void MakeGun()
        {
            gun = ThingMaker.MakeThing(Props.turretDef);
            UpdateGunVerbs();
        }
        
        private void UpdateGunVerbs()
        {
            List<Verb> allVerbs = gun.TryGetComp<CompEquippable>().AllVerbs;
            for (int i = 0; i < allVerbs.Count; i++)
            {
                Verb verb = allVerbs[i];
                verb.caster = parent;
                verb.castCompleteCallback = delegate
                {
                    burstCooldownTicksLeft = AttackVerb.verbProps.defaultCooldownTime.SecondsToTicks();
                };
            }
        }
        
        // 添加Gizmo方法
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }
            
            // 只让第一个Comp_MultiTurretGun组件生成Gizmo
            if (parent is Pawn pawn && pawn.IsColonyMechPlayerControlled)
            {
                // 获取所有Comp_MultiTurretGun组件
                var allTurretComps = parent.GetComps<Comp_MultiTurretGun>();

                // 检查当前组件是否是第一个
                if (!gizmoAdded)
                {
                    gizmoAdded = true;

                    // 检查所有炮塔是否都有相同的fireAtWill状态
                    bool allTurretsEnabled = true;
                    bool allTurretsDisabled = true;

                    foreach (var turretComp in allTurretComps)
                    {
                        if (turretComp.fireAtWill)
                            allTurretsDisabled = false;
                        else
                            allTurretsEnabled = false;
                    }

                    // 确定Gizmo的初始状态
                    bool mixedState = !(allTurretsEnabled || allTurretsDisabled);

                    Command_Toggle command = new Command_Toggle();
                    command.defaultLabel = "CommandToggleTurret".Translate();
                    command.defaultDesc = "CommandToggleTurretDesc".Translate();
                    command.icon = ToggleTurretIcon.Texture;
                    command.isActive = () =>
                    {
                        // 如果有混合状态，显示为false但使用特殊图标
                        if (mixedState)
                            return false;
                        return allTurretsEnabled;
                    };
                    command.toggleAction = () =>
                    {
                        // 切换所有炮塔的状态
                        bool newState = !allTurretsEnabled;

                        foreach (var turretComp in allTurretComps)
                        {
                            turretComp.fireAtWill = newState;

                            // 如果关闭炮塔，重置当前目标
                            if (!newState)
                            {
                                turretComp.ResetCurrentTarget();
                            }
                        }
                    };

                    // 如果有混合状态，显示特殊图标
                    if (mixedState)
                    {
                        command.icon = ContentFinder<Texture2D>.Get("UI/Commands/MixedState");
                    }

                    yield return command;
                }
            }
        }
        
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref burstCooldownTicksLeft, "burstCooldownTicksLeft", 0);
            Scribe_Values.Look(ref burstWarmupTicksLeft, "burstWarmupTicksLeft", 0);
            Scribe_TargetInfo.Look(ref currentTarget, "currentTarget_" + Props.ID);
            Scribe_Deep.Look(ref gun, "gun_" + Props.ID);
            Scribe_Values.Look(ref fireAtWill, "fireAtWill", defaultValue: true);
            Scribe_Values.Look(ref currentRotationAngle, "currentRotationAngle", 0f);
            Scribe_Values.Look(ref targetRotationAngle, "targetRotationAngle", 0f);
            Scribe_Values.Look(ref rotationVelocity, "rotationVelocity", 0f);
            Scribe_Values.Look(ref ticksWithoutTarget, "ticksWithoutTarget", 0);
            Scribe_Values.Look(ref isIdleRotating, "isIdleRotating", false);
            Scribe_Values.Look(ref idleRotationDirection, "idleRotationDirection", 1f);
            Scribe_Values.Look(ref isAiming, "isAiming", false);
            Scribe_Values.Look(ref lastBaseRotationAngle, "lastBaseRotationAngle", 0f);
            Scribe_Values.Look(ref lastTargetAngle, "lastTargetAngle", 0f);
            
            // 保存缺失的字段
            Scribe_TargetInfo.Look(ref lastAttackedTarget, "lastAttackedTarget_" + Props.ID);
            Scribe_Values.Look(ref lastAttackTargetTick, "lastAttackTargetTick_" + Props.ID, 0);
            
            // 保存复位冷却相关字段
            Scribe_Values.Look(ref resetCooldownTicksLeft, "resetCooldownTicksLeft", 0);
            Scribe_Values.Look(ref isInResetCooldown, "isInResetCooldown", false);
            
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (gun == null)
                {
                    MakeGun();
                }
                else
                {
                    UpdateGunVerbs();
                }
                
                // 确保旋转角度有效
                if (currentRotationAngle == 0f)
                {
                    currentRotationAngle = parent.Rotation.AsAngle + Props.angleOffset;
                    targetRotationAngle = currentRotationAngle;
                    lastBaseRotationAngle = parent.Rotation.AsAngle;
                    lastTargetAngle = currentRotationAngle;
                }
            }
        }
        
        // 如果需要实现 IAttackTargetSearcher 接口
        public new Thing Thing => parent;
        
        public new LocalTargetInfo LastAttackedTarget => lastAttackedTarget;
        
        public new int LastAttackTargetTick => lastAttackTargetTick;
    }
}
