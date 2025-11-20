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
        private int lastInterceptTicks = -999999;
        public int ticksToReset = 0;
        public int currentHitPoints;
        private bool wasNotAtFullHp = false;
        private bool wasActiveLastCheck = false;

        // 视觉效果变量
        private float lastInterceptAngle;
        private bool drawInterceptCone;

        public CompProperties_AreaShield Props => (CompProperties_AreaShield)props;
        public Pawn Wearer => (parent as Apparel)?.Wearer;
        public bool IsOnCooldown => ticksToReset > 0;
        public int HitPointsMax => Props.baseHitPoints;

        private StunHandler stunner;
        private bool initialized = false;

        public bool Active
        {
            get
            {
                if (Wearer == null || !Wearer.Spawned || Wearer.Dead || Wearer.Downed || IsOnCooldown)
                    return false;
                return true;
            }
        }

        // 材质定义
        private static readonly Material ForceFieldMat = MaterialPool.MatFrom("Other/ForceField", ShaderDatabase.MoteGlow);
        private static readonly Material ForceFieldConeMat = MaterialPool.MatFrom("Other/ForceFieldCone", ShaderDatabase.MoteGlow);
        private static readonly MaterialPropertyBlock MatPropertyBlock = new MaterialPropertyBlock();

        public override void PostPostMake()
        {
            base.PostPostMake();
            currentHitPoints = HitPointsMax;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref lastInterceptTicks, "lastInterceptTicks", -999999);
            Scribe_Values.Look(ref ticksToReset, "ticksToReset", 0);
            Scribe_Values.Look(ref currentHitPoints, "currentHitPoints", 0);
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

            if (Wearer == null) return;

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
            if (!Active) return false;
            if (currentHitPoints <= 0) return false;

            if (!GenGeo.IntersectLineCircleOutline(Wearer.Position.ToVector2(), Props.radius, lastExactPos.ToVector2(), newExactPos.ToVector2()))
            {
                return false;
            }

            if (projectile.def.projectile.flyOverhead && !Props.interceptAirProjectiles) return false;
            if (!projectile.def.projectile.flyOverhead && !Props.interceptGroundProjectiles) return false;
            if (projectile.Launcher != null && !projectile.Launcher.HostileTo(Wearer.Faction) && !Props.interceptNonHostileProjectiles) return false;
            
            lastInterceptTicks = Find.TickManager.TicksGame;
            
            // 记录拦截角度用于视觉效果
            lastInterceptAngle = projectile.ExactPosition.AngleToFlat(Wearer.TrueCenter());
            drawInterceptCone = true;
            
            // 尝试反射
            if (Props.canReflect && TryReflectProjectile(projectile, lastExactPos, newExactPos))
            {
                // 反射成功，播放反射特效
                Props.reflectEffecter?.Spawn(projectile.ExactPosition.ToIntVec3(), Wearer.Map).Cleanup();
                ApplyCosts(Props.reflectCost);
                return false; // 不销毁原抛射体，让它继续飞行（我们会在反射中销毁它）
            }
            else
            {
                // 普通拦截，播放拦截特效
                Props.interceptEffecter?.Spawn(projectile.ExactPosition.ToIntVec3(), Wearer.Map).Cleanup();
                ApplyCosts();
                return true; // 销毁抛射体
            }
        }

        /// <summary>
        /// 尝试反射抛射体 - 现在会创建新的抛射体
        /// </summary>
        private bool TryReflectProjectile(Projectile originalProjectile, Vector3 lastExactPos, Vector3 newExactPos)
        {
            if (!Props.canReflect) return false;
            
            // 检查反射概率
            if (Rand.Value > Props.reflectChance) return false;

            try
            {
                // 计算入射方向
                Vector3 incomingDirection = (newExactPos - lastExactPos).normalized;
                
                // 计算法线方向（从护盾中心到碰撞点）
                Vector3 normal = (newExactPos - Wearer.DrawPos).normalized;
                
                // 计算反射方向（镜面反射）
                Vector3 reflectDirection = Vector3.Reflect(incomingDirection, normal);
                
                // 添加随机角度偏移
                float randomAngle = Rand.Range(-Props.reflectAngleRange, Props.reflectAngleRange);
                reflectDirection = Quaternion.Euler(0, randomAngle, 0) * reflectDirection;
                
                // 创建新的反射抛射体
                CreateReflectedProjectile(originalProjectile, reflectDirection, newExactPos);
                
                return true;
            }
            catch (System.Exception ex)
            {
                Log.Warning($"Error reflecting projectile: {ex}");
            }
            
            return false;
        }

        /// <summary>
        /// 创建反射后的新抛射体
        /// </summary>
        private void CreateReflectedProjectile(Projectile originalProjectile, Vector3 reflectDirection, Vector3 collisionPoint)
        {
            try
            {
                // 计算新的发射位置（护盾位置附近）
                Vector3 spawnPosition = GetReflectSpawnPosition(collisionPoint);
                
                // 计算新的目标位置
                Vector3 targetPosition = spawnPosition + reflectDirection * 30f; // 足够远的距离
                
                // 创建新的抛射体
                Projectile newProjectile = (Projectile)GenSpawn.Spawn(originalProjectile.def, spawnPosition.ToIntVec3(), Wearer.Map);
                
                // 设置发射者为原抛射体的发射者
                Thing launcher = originalProjectile.Launcher ?? Wearer;
                
                // 发射新抛射体
                newProjectile.Launch(
                    launcher,
                    spawnPosition,
                    new LocalTargetInfo(targetPosition.ToIntVec3()),
                    new LocalTargetInfo(targetPosition.ToIntVec3()),
                    ProjectileHitFlags.All,
                    false
                );
                
                // 复制重要的属性
                CopyProjectileProperties(originalProjectile, newProjectile);
                
                // 销毁原抛射体
                originalProjectile.Destroy(DestroyMode.Vanish);
                
                Log.Message($"反射抛射体: 从 {spawnPosition} 向 {targetPosition} 发射");
            }
            catch (System.Exception ex)
            {
                Log.Warning($"Error creating reflected projectile: {ex}");
            }
        }

        /// <summary>
        /// 获取反射抛射体的发射位置（护盾边界上）
        /// </summary>
        private Vector3 GetReflectSpawnPosition(Vector3 collisionPoint)
        {
            // 计算从护盾中心到碰撞点的方向
            Vector3 directionFromCenter = (collisionPoint - Wearer.DrawPos).normalized;
            
            // 在护盾边界上生成（稍微向内一点避免立即再次碰撞）
            float spawnDistance = Props.radius * 0.9f;
            Vector3 spawnPosition = Wearer.DrawPos + directionFromCenter * spawnDistance;
            
            // 确保位置在地图内
            IntVec3 spawnCell = spawnPosition.ToIntVec3();
            if (!spawnCell.InBounds(Wearer.Map))
            {
                spawnCell = Wearer.Position;
            }
            
            return spawnCell.ToVector3Shifted();
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
                Log.Warning($"Error copying projectile properties: {ex}");
            }
        }
        
        public override void PostPreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
        {
            absorbed = false;
            if (!Active || Wearer == null) return;
            
            if (dinfo.Def.isRanged) return;

            if (dinfo.Instigator != null)
            {
                float distance = Wearer.Position.DistanceTo(dinfo.Instigator.Position);
                if (distance > Props.radius) return;
            }

            if (currentHitPoints <= 0) return;

            Props.absorbEffecter?.Spawn(Wearer.Position, Wearer.Map).Cleanup();
            ApplyCosts();
            absorbed = true;
        }

        private void Break()
        {
            Props.breakEffecter?.Spawn(Wearer.Position, Wearer.Map).Cleanup();
            ticksToReset = Props.rechargeDelay;
            currentHitPoints = 0;
            AreaShieldManager.NotifyShieldStateChanged(this);
        }

        private void Reset()
        {
            if (Wearer != null && Wearer.Spawned)
            {
                Props.reactivateEffecter?.Spawn(Wearer.Position, Wearer.Map).Cleanup();
            }
            currentHitPoints = HitPointsMax;
            AreaShieldManager.NotifyShieldStateChanged(this);
        }

        // 护盾绘制方法
        public override void CompDrawWornExtras()
        {
            base.CompDrawWornExtras();
            
            if (!Active || Wearer?.Map == null || !ShouldDisplay) 
                return;

            Vector3 drawPos = Wearer.Drawer?.DrawPos ?? Wearer.Position.ToVector3Shifted();
            drawPos.y = AltitudeLayer.MoteOverhead.AltitudeFor();

            float alpha = GetCurrentAlpha();
            if (alpha > 0f)
            {
                Color color = Props.color;
                color.a *= alpha;
                MatPropertyBlock.SetColor(ShaderPropertyIDs.Color, color);
                Matrix4x4 matrix = default;
                
                float scale = Props.radius * 2f * 1.1601562f;
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

        // 显示条件
        protected bool ShouldDisplay
        {
            get
            {
                if (Wearer == null || !Wearer.Spawned || Wearer.Dead || Wearer.Downed || !Active)
                    return false;
                    
                if (Wearer.Drafted || Wearer.InAggroMentalState || 
                    (Wearer.Faction != null && Wearer.Faction.HostileTo(Faction.OfPlayer) && !Wearer.IsPrisoner))
                    return true;
                    
                if (Find.Selector.IsSelected(Wearer))
                    return true;
                    
                return false;
            }
        }

        private float GetCurrentAlpha()
        {
            float idleAlpha = Mathf.Lerp(0.3f, 0.6f, (Mathf.Sin((float)Gen.HashCombineInt(parent.thingIDNumber, 35990913) + Time.realtimeSinceStartup * 2f) + 1f) / 2f);
            float interceptAlpha = Mathf.Clamp01(1f - (float)(Find.TickManager.TicksGame - lastInterceptTicks) / 40f);
            return Mathf.Max(idleAlpha, interceptAlpha);
        }

        private float GetCurrentConeAlpha()
        {
            if (!drawInterceptCone) return 0f;
            return Mathf.Clamp01(1f - (float)(Find.TickManager.TicksGame - lastInterceptTicks) / 40f) * 0.82f;
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

        public override IEnumerable<Gizmo> CompGetWornGizmosExtra()
        {
            EnsureInitialized();

            if (Wearer != null && Find.Selector.SingleSelectedThing == Wearer)
            {
                yield return new Gizmo_AreaShieldStatus { shield = this };
            }
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
