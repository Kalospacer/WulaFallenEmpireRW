using RimWorld;
using Verse;
using UnityEngine;
using Verse.Sound;
using System.Collections.Generic;
using HarmonyLib;

namespace WulaFallenEmpire
{
    [StaticConstructorOnStartup]
    public class ThingComp_AreaShield : ThingComp
    {
        // 现有的字段保持不变...
        private int lastInterceptTicks = -999999;
        public int ticksToReset = 0;
        public int currentHitPoints;
        private bool wasNotAtFullHp = false;
        private bool wasActiveLastCheck = false;

        // 新增：绘制控制字段
        private bool drawShield = true;
        
        // 视觉效果变量
        private float lastInterceptAngle;
        private bool drawInterceptCone;

        public CompProperties_AreaShield Props => (CompProperties_AreaShield)props;
        
        // 回退机制：支持装备和普通物品
        public Pawn Wearer => (parent as Apparel)?.Wearer;
        public bool IsEquipment => parent is Apparel;
        public bool IsStandalone => !IsEquipment;
        
        // 获取护盾持有者（回退机制）
        public Thing Holder => IsEquipment ? (Thing)Wearer : parent;
        
        public bool IsOnCooldown => ticksToReset > 0;
        public int HitPointsMax => Props.baseHitPoints;

        private bool initialized = false;
        private StunHandler stunner;

        // 材质定义
        private static readonly Material ForceFieldMat = MaterialPool.MatFrom("Other/ForceField", ShaderDatabase.MoteGlow);
        private static readonly Material ForceFieldConeMat = MaterialPool.MatFrom("Other/ForceFieldCone", ShaderDatabase.MoteGlow);
        private static readonly MaterialPropertyBlock MatPropertyBlock = new MaterialPropertyBlock();
        private const float TextureActualRingSizeFactor = 1.1601562f;
        private static readonly Color InactiveColor = new Color(0.2f, 0.2f, 0.2f);


        // 新增：检查是否应该显示绘制控制按钮
        public bool ShouldShowDrawToggleGizmo
        {
            get
            {
                // 条件1：装备且穿戴在己方pawn身上
                if (IsEquipment && Wearer != null && Wearer.Faction == Faction.OfPlayer)
                    return true;
                
                // 条件2：固定物品且属于己方派系
                if (IsStandalone && parent.Faction == Faction.OfPlayer)
                    return true;
                
                return false;
            }
        }

        // 护盾绘制方法 - 修改：添加绘制控制检查
        public override void CompDrawWornExtras()
        {
            base.CompDrawWornExtras();

            if (!IsEquipment || !drawShield) return; // 新增绘制控制检查

            DrawShield();
        }
        
        public override void PostDraw()
        {
            base.PostDraw();

            if (IsEquipment || !drawShield) return; // 新增绘制控制检查

            DrawShield();
        }

        /// <summary>
        /// 统一的护盾绘制方法 - 修改：添加绘制控制检查
        /// </summary>
        private void DrawShield()
        {
            if (!drawShield || !Active || Holder?.Map == null || Holder.Destroyed) // 新增绘制控制检查
                return;
                
            Vector3 drawPos = GetHolderDrawPos();
            drawPos.y = AltitudeLayer.MoteOverhead.AltitudeFor();
            float currentAlpha = GetCurrentAlpha();
            if (currentAlpha > 0f)
            {
                // 参考原版：未激活但被选中时使用灰色
                Color color = (!Active && Find.Selector.IsSelected(parent)) ? InactiveColor : Props.color;
                color.a *= currentAlpha;
                MatPropertyBlock.SetColor(ShaderPropertyIDs.Color, color);

                Matrix4x4 matrix = default;
                float scale = Props.radius * 2f * TextureActualRingSizeFactor;
                matrix.SetTRS(drawPos, Quaternion.identity, new Vector3(scale, 1f, scale));
                Graphics.DrawMesh(MeshPool.plane10, matrix, ForceFieldMat, 0, null, 0, MatPropertyBlock);
            }
            // 添加拦截锥形效果
            float coneAlpha = GetCurrentConeAlpha();
            if (coneAlpha > 0f)
            {
                Color color = Props.color;
                color.a *= coneAlpha;
                MatPropertyBlock.SetColor(ShaderPropertyIDs.Color, color);

                Matrix4x4 matrix = default;
                float scale = Props.radius * 2f;
                matrix.SetTRS(drawPos, Quaternion.Euler(0f, lastInterceptAngle - 90f, 0f), new Vector3(scale, 1f, scale));
                Graphics.DrawMesh(MeshPool.plane10, matrix, ForceFieldConeMat, 0, null, 0, MatPropertyBlock);
            }
        }

        // 现有的其他方法保持不变...
        private float GetCurrentAlpha()
        {
            // 多个透明度来源叠加，取最大值
            return Mathf.Max(
                Mathf.Max(
                    Mathf.Max(
                        GetCurrentAlpha_Idle(),
                        GetCurrentAlpha_Selected()
                    ),
                    GetCurrentAlpha_RecentlyIntercepted()
                ),
                0.1f // 最小透明度
            );
        }

        private float GetCurrentAlpha_Idle()
        {
            if (!Active) return 0f;

            // 固定物品：始终显示空闲状态
            if (IsStandalone)
            {
                // 脉冲效果
                return Mathf.Lerp(0.3f, 0.6f,
                    (Mathf.Sin((float)Gen.HashCombineInt(parent.thingIDNumber, 35990913) + Time.realtimeSinceStartup * 2f) + 1f) / 2f);
            }
            // 装备：只在特定条件下显示
            else if (IsEquipment)
            {
                // 装备护盾只在以下情况显示空闲状态：
                if (Holder is Pawn pawn)
                {
                    if (pawn.Drafted || pawn.InAggroMentalState ||
                        (pawn.Faction != null && pawn.Faction.HostileTo(Faction.OfPlayer) && !pawn.IsPrisoner))
                    {
                        return Mathf.Lerp(0.3f, 0.6f,
                            (Mathf.Sin((float)Gen.HashCombineInt(parent.thingIDNumber, 35990913) + Time.realtimeSinceStartup * 2f) + 1f) / 2f);
                    }
                }
            }

            return 0f;
        }

        private float GetCurrentAlpha_Selected()
        {
            // 如果被选中，显示更高的透明度
            if (Find.Selector.IsSelected(parent) && Active)
            {
                return Mathf.Lerp(0.4f, 0.8f,
                    (Mathf.Sin((float)Gen.HashCombineInt(parent.thingIDNumber, 96804938) + Time.realtimeSinceStartup * 2.5f) + 1f) / 2f);
            }

            return 0f;
        }

        private float GetCurrentAlpha_RecentlyIntercepted()
        {
            int ticksSinceIntercept = Find.TickManager.TicksGame - lastInterceptTicks;
            return Mathf.Clamp01(1f - (float)ticksSinceIntercept / 40f) * 0.3f;
        }

        private float GetCurrentConeAlpha()
        {
            if (!drawInterceptCone) return 0f;

            int ticksSinceIntercept = Find.TickManager.TicksGame - lastInterceptTicks;
            return Mathf.Clamp01(1f - (float)ticksSinceIntercept / 40f) * 0.82f;
        }

        private Vector3 GetHolderDrawPos()
        {
            if (Holder is Pawn pawn)
                return pawn.Drawer?.DrawPos ?? pawn.Position.ToVector3Shifted();
            else
                return Holder.DrawPos;
        }

        // 移动状态检测（仅对装备有效）
        public bool IsHolderMoving
        {
            get
            {
                if (IsStandalone) return false; // 固定物品不会移动
                if (Wearer == null || !Wearer.Spawned) return false;
                return Wearer.pather.Moving;
            }
        }

        // 修改Active属性：装备只有在立定时才激活，固定物品始终激活
        public bool Active
        {
            get
            {
                if (Holder == null || !Holder.Spawned || Holder.Destroyed)
                    return false;
                    
                if (Holder is Pawn pawn && (pawn.Dead || pawn.Downed))
                    return false;
                    
                if (IsOnCooldown)
                    return false;
                    
                // 装备：只有在立定时才激活
                if (IsEquipment && IsHolderMoving)
                    return false;
                    
                return true;
            }
        }

        public override void PostPostMake()
        {
            base.PostPostMake();
            currentHitPoints = HitPointsMax;
            drawShield = true; // 默认启用绘制
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref lastInterceptTicks, "lastInterceptTicks", -999999);
            Scribe_Values.Look(ref ticksToReset, "ticksToReset", 0);
            Scribe_Values.Look(ref currentHitPoints, "currentHitPoints", 0);
            Scribe_Values.Look(ref drawShield, "drawShield", true); // 新增：保存绘制状态
        }

        public override void CompTick()
        {
            base.CompTick();
            
            bool isActive = Active;
            
            // 检查状态变化并通知管理器
            if (isActive != wasActiveLastCheck)
            {
                AreaShieldManager.NotifyShieldStateChanged(this);
                wasActiveLastCheck = isActive;
            }

            if (Holder == null) return;

            if (IsOnCooldown)
            {
                ticksToReset--;
                if (ticksToReset <= 0)
                {
                    Reset();
                }
            }
            else if (isActive && currentHitPoints < HitPointsMax)
            {
                wasNotAtFullHp = true;
                if (parent.IsHashIntervalTick(Props.rechargeHitPointsIntervalTicks))
                {
                    currentHitPoints += 1;
                    if (currentHitPoints > HitPointsMax) 
                        currentHitPoints = HitPointsMax;
                }
            }
            else if (wasNotAtFullHp && currentHitPoints >= HitPointsMax)
            {
                wasNotAtFullHp = false;
            }
        }

        // 新增：绘制控制Gizmo
        private Gizmo CreateDrawToggleGizmo()
        {
            Command_Toggle toggle = new Command_Toggle
            {
                defaultLabel = drawShield ? "WULA_HideAreaShieldLabel".Translate() : "WULA_ShowAreaShieldLabel".Translate(),
                defaultDesc = drawShield ? "WULA_HideAreaShieldDesc".Translate() : "WULA_ShowAreaShieldDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("Wula/UI/Commands/WULA_HideAreaShield"),
                isActive = () => drawShield,
                toggleAction = () => drawShield = !drawShield
            };

            return toggle;
        }

        public override IEnumerable<Gizmo> CompGetWornGizmosExtra()
        {
            EnsureInitialized();

            // 原有的状态显示Gizmo
            if (IsEquipment && Wearer != null && Find.Selector.SingleSelectedThing == Wearer)
            {
                yield return new Gizmo_AreaShieldStatus { shield = this };
                
                // 新增：绘制控制按钮（只在符合条件的装备上显示）
                if (ShouldShowDrawToggleGizmo)
                {
                    yield return CreateDrawToggleGizmo();
                }
            }
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            EnsureInitialized();

            // 原有的状态显示Gizmo
            if (IsStandalone && Find.Selector.SingleSelectedThing == parent)
            {
                yield return new Gizmo_AreaShieldStatus { shield = this };
                
                // 新增：绘制控制按钮（只在符合条件的固定物品上显示）
                if (ShouldShowDrawToggleGizmo)
                {
                    yield return CreateDrawToggleGizmo();
                }
            }
        }

        // 现有的其他方法保持不变...
        private void ApplyCosts(int cost = 1)
        {
            currentHitPoints -= cost;
            if (currentHitPoints <= 0)
            {
                Break();
            }
            
            // 护盾值变化时通知管理器
            AreaShieldManager.NotifyShieldStateChanged(this);
        }

        public bool TryIntercept(Projectile projectile, Vector3 lastExactPos, Vector3 newExactPos)
        {
            // 增强安全检查
            if (!Active || projectile == null || projectile.Destroyed || Holder == null || Holder.Map == null)
                return false;

            if (currentHitPoints <= 0)
                return false;
                
            try
            {
                if (!GenGeo.IntersectLineCircleOutline(Holder.Position.ToVector2(), Props.radius, lastExactPos.ToVector2(), newExactPos.ToVector2()))
                {
                    return false;
                }
                if (projectile.def.projectile.flyOverhead && !Props.interceptAirProjectiles)
                    return false;
                if (!projectile.def.projectile.flyOverhead && !Props.interceptGroundProjectiles)
                    return false;
                if (projectile.Launcher != null && !projectile.Launcher.HostileTo(Holder.Faction) && !Props.interceptNonHostileProjectiles)
                    return false;

                lastInterceptTicks = Find.TickManager.TicksGame;

                // 记录拦截角度用于视觉效果
                lastInterceptAngle = projectile.ExactPosition.AngleToFlat(GetHolderCenter());
                drawInterceptCone = true;

                // 尝试反射
                if (Props.canReflect && TryReflectProjectile(projectile, lastExactPos, newExactPos))
                {
                    // 反射成功，播放反射特效
                    Props.reflectEffecter?.Spawn(projectile.ExactPosition.ToIntVec3(), Holder.Map).Cleanup();
                    ApplyCosts(Props.reflectCost);
                    return false; // 不销毁原抛射体，让它继续飞行（我们会在反射中销毁它）
                }
                else
                {
                    // 普通拦截，播放拦截特效
                    Props.interceptEffecter?.Spawn(projectile.ExactPosition.ToIntVec3(), Holder.Map).Cleanup();
                    ApplyCosts();
                    return true; // 销毁抛射体
                }
            }
            catch (System.Exception ex)
            {
                WulaLog.Debug($"Error in TryIntercept: {ex}");
                return false;
            }
        }

        /// <summary>
        /// 获取持有者中心位置（回退机制）
        /// </summary>
        private Vector3 GetHolderCenter()
        {
            if (Holder is Pawn pawn)
                return pawn.TrueCenter();
            else
                return Holder.DrawPos;
        }

        /// <summary>
        /// 尝试反射抛射体 - 现在会创建新的抛射体
        /// </summary>
        private bool TryReflectProjectile(Projectile originalProjectile, Vector3 lastExactPos, Vector3 newExactPos)
        {
            if (!Props.canReflect || originalProjectile == null || originalProjectile.Destroyed)
                return false;

            // 检查反射概率
            if (Rand.Value > Props.reflectChance)
                return false;
                
            try
            {
                // 计算入射方向
                Vector3 incomingDirection = (newExactPos - lastExactPos).normalized;

                // 计算法线方向（从护盾中心到碰撞点）
                Vector3 normal = (newExactPos - GetHolderCenter()).normalized;

                // 计算反射方向（镜面反射）
                Vector3 reflectDirection = Vector3.Reflect(incomingDirection, normal);

                // 添加随机角度偏移
                float randomAngle = Rand.Range(-Props.reflectAngleRange, Props.reflectAngleRange);
                reflectDirection = Quaternion.Euler(0, randomAngle, 0) * reflectDirection;

                // 创建新的反射抛射体
                return CreateReflectedProjectile(originalProjectile, reflectDirection, newExactPos);
            }
            catch (System.Exception ex)
            {
                WulaLog.Debug($"Error reflecting projectile: {ex}");
            }

            return false;
        }
        
        /// <summary>
        /// 创建反射后的新抛射体
        /// </summary>
        private bool CreateReflectedProjectile(Projectile originalProjectile, Vector3 reflectDirection, Vector3 collisionPoint)
        {
            try
            {
                if (originalProjectile == null || originalProjectile.Destroyed || Holder == null || Holder.Map == null)
                    return false;
                    
                // 计算新的发射位置（护盾位置附近）
                Vector3 spawnPosition = GetReflectSpawnPosition(collisionPoint);
                
                // 确保位置在地图内
                IntVec3 spawnCell = spawnPosition.ToIntVec3();
                if (!spawnCell.InBounds(Holder.Map))
                {
                    spawnCell = Holder.Position;
                }
                
                // 计算新的目标位置
                Vector3 targetPosition = spawnCell.ToVector3Shifted() + reflectDirection * 30f;
                IntVec3 targetCell = targetPosition.ToIntVec3();
                
                // 创建新的抛射体
                Projectile newProjectile = (Projectile)GenSpawn.Spawn(originalProjectile.def, spawnCell, Holder.Map);
                if (newProjectile == null)
                {
                    WulaLog.Debug("Failed to spawn reflected projectile");
                    return false;
                }
                
                // 设置发射者为护盾持有者
                Thing launcher = Holder;
                
                // 发射新抛射体
                newProjectile.Launch(
                    launcher,
                    spawnCell.ToVector3Shifted(),
                    new LocalTargetInfo(targetCell),
                    new LocalTargetInfo(targetCell),
                    ProjectileHitFlags.All,
                    false
                );
                
                // 复制重要的属性
                CopyProjectileProperties(originalProjectile, newProjectile);
                
                // 使用延迟销毁而不是立即销毁
                ReflectedProjectileManager.MarkForDelayedDestroy(originalProjectile);
                
                WulaLog.Debug($"反射抛射体: 由 {Holder?.LabelShort} 从 {spawnCell} 向 {targetCell} 发射");
                return true;
            }
            catch (System.Exception ex)
            {
                WulaLog.Debug($"Error creating reflected projectile: {ex}");
                return false;
            }
        }

        /// <summary>
        /// 获取反射抛射体的发射位置（护盾边界上）
        /// </summary>
        private Vector3 GetReflectSpawnPosition(Vector3 collisionPoint)
        {
            if (Holder == null)
                return collisionPoint;

            // 计算从护盾中心到碰撞点的方向
            Vector3 directionFromCenter = (collisionPoint - GetHolderCenter()).normalized;

            // 在护盾边界上生成（稍微向内一点避免立即再次碰撞）
            float spawnDistance = Props.radius * 0.9f;
            Vector3 spawnPosition = GetHolderCenter() + directionFromCenter * spawnDistance;

            return spawnPosition;
        }

        /// <summary>
        /// 复制抛射体重要属性
        /// </summary>
        private void CopyProjectileProperties(Projectile source, Projectile destination)
        {
            try
            {
                var sourceTraverse = Traverse.Create(source);
                var destTraverse = Traverse.Create(destination);
                
                // 复制伤害属性
                destTraverse.Field("damageDefOverride").SetValue(source.damageDefOverride);
                
                // 复制额外伤害
                if (source.extraDamages != null)
                {
                    destTraverse.Field("extraDamages").SetValue(new List<ExtraDamage>(source.extraDamages));
                }
                
                // 复制停止力
                destTraverse.Field("stoppingPower").SetValue(source.stoppingPower);
            }
            catch (System.Exception ex)
            {
                WulaLog.Debug($"Error copying projectile properties: {ex}");
            }
        }
        
        public override void PostPreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
        {
            absorbed = false;
            if (!Active || Holder == null) return;
            
            if (dinfo.Def.isRanged) return;

            if (dinfo.Instigator != null)
            {
                float distance = Holder.Position.DistanceTo(dinfo.Instigator.Position);
                if (distance > Props.radius) return;
            }

            if (currentHitPoints <= 0) return;

            Props.absorbEffecter?.Spawn(Holder.Position, Holder.Map).Cleanup();
            ApplyCosts();
            absorbed = true;
        }

        private void Break()
        {
            Props.breakEffecter?.Spawn(Holder.Position, Holder.Map).Cleanup();
            ticksToReset = Props.rechargeDelay;
            currentHitPoints = 0;
            AreaShieldManager.NotifyShieldStateChanged(this);
        }

        private void Reset()
        {
            if (Holder != null && Holder.Spawned)
            {
                Props.reactivateEffecter?.Spawn(Holder.Position, Holder.Map).Cleanup();
            }
            currentHitPoints = HitPointsMax;
            AreaShieldManager.NotifyShieldStateChanged(this);
        }

        // 显示条件 - 修改为固定物品始终显示，装备有条件显示
        protected bool ShouldDisplay
        {
            get
            {
                if (Holder == null || !Holder.Spawned || Holder.Destroyed || !Active)
                    return false;

                // 对于装备：只在特定条件下显示
                if (IsEquipment && Holder is Pawn pawn)
                {
                    if (pawn.Dead || pawn.Downed)
                        return false;

                    // 装备护盾只在以下情况显示：
                    // 1. 穿戴者被选中
                    // 2. 穿戴者处于战斗状态（征召状态或敌对）
                    // 3. 穿戴者处于攻击性精神状态
                    if (Find.Selector.IsSelected(pawn))
                        return true;

                    if (pawn.Drafted || pawn.InAggroMentalState)
                        return true;

                    if (pawn.Faction != null && pawn.Faction.HostileTo(Faction.OfPlayer) && !pawn.IsPrisoner)
                        return true;
                }
                // 对于固定物品：始终显示（只要护盾激活）
                else if (IsStandalone)
                {
                    return true;
                }

                return false;
            }
        }
        
        private void EnsureInitialized()
        {
            if (initialized) return;

            if (stunner == null)
                stunner = new StunHandler(parent);

            if (currentHitPoints == -1)
                currentHitPoints = Props.startupDelay > 0 ? 0 : HitPointsMax;

            initialized = true;
        }

        public override void Notify_Equipped(Pawn pawn)
        {
            base.Notify_Equipped(pawn);
            AreaShieldManager.NotifyShieldStateChanged(this);
        }

        public override void Notify_Unequipped(Pawn pawn)
        {
            base.Notify_Unequipped(pawn);
            AreaShieldManager.NotifyShieldStateChanged(this);
        }
    }
}
