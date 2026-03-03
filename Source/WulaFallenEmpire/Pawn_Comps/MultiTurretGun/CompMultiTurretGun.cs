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
        public float resetCooldownTime = 3f; // 复位冷却时间（秒）
        
        // Gizmo 相关属性
        public string gizmoLabel; // Gizmo 标签
        public string gizmoDescription; // Gizmo 描述
        public string gizmoIconPath = "UI/Gizmos/ToggleTurret"; // Gizmo 图标路径
        
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
        private int resetCooldownTicksLeft = 0; // 复位冷却剩余tick数
        private bool isInResetCooldown = false; // 是否处于复位冷却中
        
        // 添加缺失的字段
        private LocalTargetInfo lastAttackedTarget = LocalTargetInfo.Invalid;
        private int lastAttackTargetTick;
        
        // 集中火力目标
        public static LocalTargetInfo focusTarget = LocalTargetInfo.Invalid;
        public static int lastFocusSetTick = 0;
        public static Thing lastFocusPawn = null;
        
        // Gizmo 缓存
        private Command_Toggle cachedGizmo;
        private Command_Action cachedFocusGizmo;
        private bool gizmoInitialized = false;
        
        public new CompProperties_MultiTurretGun Props => (CompProperties_MultiTurretGun)props;
        
        // 添加属性
        private bool WarmingUp => burstWarmupTicksLeft > 0;
        
        // 公开 fireAtWill 属性的访问器
        public bool FireAtWill
        {
            get => fireAtWill;
            set
            {
                if (fireAtWill != value)
                {
                    fireAtWill = value;
                    
                    // 如果关闭炮塔，重置当前目标
                    if (!fireAtWill)
                    {
                        ResetCurrentTarget();
                    }
                }
            }
        }
        
        // 是否为ID=0的主控组件
        private bool IsMasterTurret => Props.ID == 0;
        
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
            // 重置 Gizmo 缓存
            gizmoInitialized = false;
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
                    if (!fireAtWill)
                    {
                        return false;
                    }
                }
                CompCanBeDormant compCanBeDormant = parent.TryGetComp<CompCanBeDormant>();
                if (compCanBeDormant != null && !compCanBeDormant.Awake)
                {
                    return false;
                }
                CompMechPilotHolder compMechPilotHolder = parent.TryGetComp<CompMechPilotHolder>();
                if (compMechPilotHolder != null && !compMechPilotHolder.HasPilots)
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
                            
                            // 开火后开始复位冷却
                            StartResetCooldown();
                            
                            // 开火后重置目标，等待冷却结束
                            currentTarget = LocalTargetInfo.Invalid;
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
            
            // 冷却结束，尝试寻找目标（但不在复位冷却中时）
            if (burstCooldownTicksLeft <= 0 && !isInResetCooldown && parent.IsHashIntervalTick(10))
            {
                TryAcquireTarget();
            }
            
            // 检查是否已经瞄准目标但还没有开始预热
            // 这是关键修复：当炮塔已经瞄准目标但burstWarmupTicksLeft为0时，开始预热
            if (currentTarget.IsValid && IsAimedAtTarget() && burstWarmupTicksLeft <= 0 && burstCooldownTicksLeft <= 0 && !isInResetCooldown)
            {
                burstWarmupTicksLeft = Mathf.Max(1, Mathf.RoundToInt(Props.aimTicks));
                return;
            }
            
            // 更新空闲旋转（只在复位冷却结束后）
            if (!isInResetCooldown)
            {
                UpdateIdleRotation();
            }
        }
        
        private void UpdateResetCooldown()
        {
            if (isInResetCooldown)
            {
                resetCooldownTicksLeft--;
                if (resetCooldownTicksLeft <= 0)
                {
                    isInResetCooldown = false;
                }
            }
        }
        
        private void StartResetCooldown()
        {
            // 计算复位冷却的tick数（秒转tick）
            int resetCooldownTicks = Mathf.RoundToInt(Props.resetCooldownTime * 60f);
            resetCooldownTicksLeft = Mathf.Max(1, resetCooldownTicks);
            isInResetCooldown = true;
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

        public void TryAcquireTarget()
        {
            // 1. 首先检查是否有集中火力目标且可以对其开火
            if (focusTarget.IsValid && focusTarget.Thing != null && focusTarget.Thing.Spawned)
            {
                // 检查是否属于同一个Pawn且在同一张地图
                if (lastFocusPawn == parent && focusTarget.Thing.Map == parent.Map)
                {
                    // 检查能否看到目标
                    if (CanSeeTarget(focusTarget.Thing))
                    {
                        // 检查是否在射程内
                        float distance = (focusTarget.CenterVector3 - parent.DrawPos).MagnitudeHorizontal();
                        float maxRange = AttackVerb.verbProps.range;
                        
                        if (distance <= maxRange)
                        {
                            // 优先目标有效，将其作为目标
                            SetCurrentTarget(focusTarget);
                            return;
                        }
                    }
                }
                else
                {
                    // 不属于同一个Pawn或不在同一地图，清除集中火力目标
                    focusTarget = LocalTargetInfo.Invalid;
                }
            }
            
            // 2. 如果没有有效的集中火力目标，执行原有索敌逻辑
            LocalTargetInfo newTarget = (Thing)AttackTargetFinder.BestShootTargetFromCurrentPosition(
                this, 
                TargetScanFlags.NeedThreat | TargetScanFlags.NeedAutoTargetable
            );
            
            if (newTarget.IsValid)
            {
                // 如果有目标，立即结束复位冷却
                isInResetCooldown = false;
                resetCooldownTicksLeft = 0;
                
                // 如果已经有目标并且是同一个目标，保持当前状态
                if (currentTarget.IsValid && currentTarget.Thing == newTarget.Thing)
                {
                    // 检查是否已经瞄准目标但还没有开始预热
                    // 关键修复：确保当炮塔已经瞄准目标时开始预热
                    if (IsAimedAtTarget() && burstWarmupTicksLeft <= 0)
                    {
                        burstWarmupTicksLeft = Mathf.Max(1, Mathf.RoundToInt(Props.aimTicks));
                    }
                }
                else
                {
                    // 新目标，重置状态
                    SetCurrentTarget(newTarget);
                }
            }
            else
            {
                // 没有目标，但如果在复位冷却中，不立即复位
                if (!isInResetCooldown)
                {
                    ResetCurrentTarget();
                }
                // 如果在复位冷却中，保持当前状态，等待冷却结束
            }
        }
        
        private bool CanSeeTarget(Thing target)
        {
            // 简单的视线检查，可以替换为更复杂的逻辑
            if (target == null || !target.Spawned)
                return false;
                
            // 检查是否有障碍物遮挡
            if (!GenSight.LineOfSight(parent.Position, target.Position, parent.Map, skipFirstCell: true))
            {
                return false;
            }
            
            return true;
        }
        
        private void SetCurrentTarget(LocalTargetInfo newTarget)
        {
            currentTarget = newTarget;
            burstWarmupTicksLeft = 0;
            isAiming = true;
            
            // 计算目标角度
            Vector3 targetPos = currentTarget.CenterVector3;
            Vector3 turretPos = parent.DrawPos;
            targetRotationAngle = (targetPos - turretPos).AngleFlat();
            lastTargetAngle = targetRotationAngle;
            
            // 重要：立即结束复位冷却，因为有了新目标
            isInResetCooldown = false;
            resetCooldownTicksLeft = 0;
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
                // 没有目标时，只有在复位冷却结束后才朝向初始角度
                if (!isInResetCooldown)
                {
                    targetRotationAngle = parent.Rotation.AsAngle + Props.angleOffset;
                    ticksWithoutTarget++;
                    lastTargetAngle = targetRotationAngle;
                }
                else
                {
                    // 在复位冷却中，保持当前角度不改变
                    // 不更新targetRotationAngle，炮塔保持当前方向
                    ticksWithoutTarget++;
                }
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
            // 只在没有目标、冷却结束、并且复位冷却结束后执行空闲旋转
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
            
            // 如果当前目标是集中火力目标，用特殊颜色绘制瞄准线
            if (currentTarget.IsValid && currentTarget.Thing == focusTarget.Thing)
            {
                DrawFocusTargetingLine();
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
            
            GenDraw.DrawLineBetween(lineStart, lineEnd, SimpleColor.White);
        }
        
        private void DrawFocusTargetingLine()
        {
            Vector3 lineStart = parent.DrawPos;
            lineStart.y = AltitudeLayer.MetaOverlays.AltitudeFor();
            
            Vector3 lineEnd = currentTarget.CenterVector3;
            lineEnd.y = AltitudeLayer.MetaOverlays.AltitudeFor();
            
            // 集中火力目标的特殊颜色
            Color lineColor = new Color(1f, 0.5f, 0f, 0.8f); // 橙色
            
            // 绘制更粗的线条
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
        
        // 重构后的 Gizmo 方法 - 每个组件独立控制
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }
            
            // 只对殖民地玩家控制的单位显示 Gizmo
            if (parent is Pawn pawn && pawn.Faction.IsPlayer)
            {
                // 延迟初始化 Gizmo，只在需要时创建
                if (!gizmoInitialized)
                {
                    InitializeGizmo();
                    gizmoInitialized = true;
                }
                
                // 显示炮塔开关Gizmo
                if (cachedGizmo != null)
                {
                    yield return cachedGizmo;
                }
                
                // 只有ID=0的主控炮塔显示集中火力Gizmo
                if (IsMasterTurret && cachedFocusGizmo != null)
                {
                    yield return cachedFocusGizmo;
                }
            }
        }
        
        private void InitializeGizmo()
        {
            // 确定标签
            string label = !string.IsNullOrEmpty(Props.gizmoLabel) ? 
                Props.gizmoLabel : 
                Props.turretDef?.label ?? "Turrets".Translate();

            // 确定标签
            string description = !string.IsNullOrEmpty(Props.gizmoDescription) ?
                Props.gizmoDescription :
                Props.turretDef?.description + "Wula_ToggleTurretgizmoDesc_Short".Translate();

            // 确定图标
            Texture2D icon = ContentFinder<Texture2D>.Get(Props.gizmoIconPath, false);
            if (icon == null)
            {
                // 使用默认图标
                icon = ContentFinder<Texture2D>.Get("UI/Gizmos/ToggleTurret", false) ?? BaseContent.BadTex;
            }
            
            // 创建炮塔开关Gizmo
            cachedGizmo = new Command_Toggle();
            cachedGizmo.defaultLabel = label + (Props.ID > 0 ? $" #{Props.ID}" : "");
            cachedGizmo.defaultDesc = description;
            cachedGizmo.icon = icon;
            cachedGizmo.isActive = () => FireAtWill;
            cachedGizmo.toggleAction = () =>
            {
                FireAtWill = !FireAtWill;
            };
            
            // 添加热键
            cachedGizmo.hotKey = KeyBindingDefOf.Misc1;
            
            // 为主控炮塔创建集中火力Gizmo
            if (IsMasterTurret)
            {
                // 如果有集中火力目标，添加清除按钮
                if (focusTarget.IsValid && focusTarget != null && lastFocusPawn == parent)
                {
                    cachedFocusGizmo.defaultLabel = "Wula_ClearFocus".Translate();
                    cachedFocusGizmo.defaultDesc = "Wula_ClearFocusDesc".Translate();
                    cachedFocusGizmo.icon = ContentFinder<Texture2D>.Get("Wula/UI/Commands/WULA_TargetFocus", false) ?? BaseContent.BadTex;
                }
                else {
                    cachedFocusGizmo = new Command_Action();
                    cachedFocusGizmo.defaultLabel = "Wula_FocusFire".Translate();
                    cachedFocusGizmo.defaultDesc = "Wula_FocusFireDesc".Translate();
                    cachedFocusGizmo.icon = ContentFinder<Texture2D>.Get("Wula/UI/Commands/WULA_TargetFocus", false) ?? BaseContent.BadTex;
                    cachedFocusGizmo.action = () =>
                    {
                        // 显示目标选择菜单
                        ShowTargetSelectMenu();
                    };
                }
            }
        }
        
        private void ShowTargetSelectMenu()
        {
            // 如果已经有集中火力目标，清除它
            if (focusTarget.IsValid && focusTarget != null && lastFocusPawn == parent)
            {
                focusTarget = LocalTargetInfo.Invalid;
                return;
            }

            // 启动目标选择
            Find.Targeter.BeginTargeting(new TargetingParameters
            {
                canTargetLocations = false,
                canTargetPawns = true,
                canTargetBuildings = true,
                canTargetItems = false
            }, SetFocusTarget, null, OnFocusTargetCancelled);
        }
        
        private void SetFocusTarget(LocalTargetInfo target)
        {
            if (target.Thing != null && target.Thing.Spawned)
            {
                focusTarget = target;
                lastFocusSetTick = Find.TickManager.TicksGame;
                lastFocusPawn = parent;
            }
        }
        private void OnFocusTargetCancelled()
        {
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
            Scribe_Values.Look(ref resetCooldownTicksLeft, "resetCooldownTicksLeft", 0);
            Scribe_Values.Look(ref isInResetCooldown, "isInResetCooldown", false);
            
            // 保存集中火力目标
            Scribe_TargetInfo.Look(ref focusTarget, "focusTarget");
            Scribe_Values.Look(ref lastFocusSetTick, "lastFocusSetTick", 0);
            Scribe_References.Look(ref lastFocusPawn, "lastFocusPawn");
            
            // 保存缺失的字段
            Scribe_TargetInfo.Look(ref lastAttackedTarget, "lastAttackedTarget_" + Props.ID);
            Scribe_Values.Look(ref lastAttackTargetTick, "lastAttackTargetTick_" + Props.ID, 0);
            
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
                
                // 重置 Gizmo 缓存
                gizmoInitialized = false;
                cachedGizmo = null;
                cachedFocusGizmo = null;
            }
        }
        
        // 如果需要实现 IAttackTargetSearcher 接口
        public new Thing Thing => parent;
        
        public new LocalTargetInfo LastAttackedTarget => lastAttackedTarget;
        
        public new int LastAttackTargetTick => lastAttackTargetTick;
    }
}
