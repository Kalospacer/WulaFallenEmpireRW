using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace WulaFallenEmpire
{
    public class Projectile_TrackingBullet : Bullet
    {
        private TrackingBulletDef trackingDefInt;

        protected Vector3 exactPositionInt;
        public Vector3 curSpeed;
        public bool homing = true;
        private int destroyTicksAfterLosingTrack = -1; // 失去追踪后多少tick自毁，-1表示不自毁
        private int Fleck_MakeFleckTick; // 拖尾特效的计时器
        private Vector3 lastTickPosition; // 记录上一帧的位置，用于计算移动方向

        private static class NonPublicFields
        {
            public static FieldInfo Projectile_AmbientSustainer = typeof(Projectile).GetField("ambientSustainer", BindingFlags.Instance | BindingFlags.NonPublic);
            public static FieldInfo ThingWithComps_comps = typeof(ThingWithComps).GetField("comps", BindingFlags.Instance | BindingFlags.NonPublic);
            public static MethodInfo ProjectileCheckForFreeInterceptBetween = typeof(Projectile).GetMethod("CheckForFreeInterceptBetween", BindingFlags.Instance | BindingFlags.NonPublic);
        }

        public TrackingBulletDef TrackingDef
        {
            get
            {
                if (trackingDefInt == null)
                {
                    trackingDefInt = def.GetModExtension<TrackingBulletDef>();
                    if (trackingDefInt == null)
                    {
                        Log.ErrorOnce($"TrackingBulletDef for {this.def.defName} is null. Creating a default instance.", this.thingIDNumber ^ 0x12345678);
                        this.trackingDefInt = new TrackingBulletDef();
                    }
                }
                return trackingDefInt;
            }
        }

        public override Vector3 ExactPosition => exactPositionInt;

        public override Quaternion ExactRotation => Quaternion.LookRotation(curSpeed);

        public override void Launch(Thing launcher, Vector3 origin, LocalTargetInfo usedTarget, LocalTargetInfo intendedTarget, ProjectileHitFlags hitFlags, bool preventFriendlyFire = false, Thing equipment = null, ThingDef targetCoverDef = null)
        {
            base.Launch(launcher, origin, usedTarget, intendedTarget, hitFlags, preventFriendlyFire, equipment, targetCoverDef);
            exactPositionInt = origin.Yto0() + Vector3.up * def.Altitude;
            
            // 初始化子弹速度，指向目标，并考虑初始旋转角度
            Vector3 initialDirection = (destination - origin).Yto0().normalized;
            float degrees = Rand.Range(0f - TrackingDef.initRotateAngle, TrackingDef.initRotateAngle);
            Vector2 v = new Vector2(initialDirection.x, initialDirection.z);
            v = v.RotatedBy(degrees);
            Vector3 rotatedDirection = new Vector3(v.x, 0f, v.y);
            curSpeed = rotatedDirection * def.projectile.SpeedTilesPerTick;
            
            ReflectInit();
            lastTickPosition = origin; // 初始化 lastTickPosition
        }

        protected void ReflectInit()
        {
            // 确保私有字段的访问
            if (!def.projectile.soundAmbient.NullOrUndefined())
            {
                // This line might cause issues if ambientSustainer is not directly settable or if Projectile type changes.
                // For simplicity, we might omit it for now or find a safer way.
                // ambientSustainer = (Sustainer)NonPublicFields.Projectile_AmbientSustainer.GetValue(this); 
            }
            // comps = (List<ThingComp>)NonPublicFields.ThingWithComps_comps.GetValue(this); // 如果需要CompTick，需要这个
        }

        public virtual void MovementTick()
        {
            Vector3 vect = ExactPosition + curSpeed;
            ShootLine shootLine = new ShootLine(ExactPosition.ToIntVec3(), vect.ToIntVec3());
            Vector3 vectorToTarget = (intendedTarget.Cell.ToVector3() - ExactPosition).Yto0();

            if (homing)
            {
                // 如果目标消失或距离太远，停止追踪
                if (!intendedTarget.IsValid || !intendedTarget.Thing.Spawned || (intendedTarget.Cell.ToVector3() - ExactPosition).magnitude > def.projectile.speed * 2f) // 假设2倍速度为最大追踪距离
                {
                    homing = false;
                    destroyTicksAfterLosingTrack = TrackingDef.destroyTicksAfterLosingTrack.RandomInRange; // 失去追踪后根据XML配置的范围自毁
                }
                else
                {
                    // 计算需要转向的方向
                    Vector3 desiredDirection = vectorToTarget.normalized;
                    Vector3 currentDirection = curSpeed.normalized;
                    
                    // 计算方向差异
                    Vector3 directionDifference = desiredDirection - currentDirection;
                    
                    // 如果方向差异过大，可能失去追踪，或者直接转向
                    if (directionDifference.sqrMagnitude > 0.001f) // 避免浮点数精度问题
                    {
                        // 调整当前速度，使其更接近目标方向
                        curSpeed += directionDifference * TrackingDef.homingSpeed * curSpeed.magnitude;
                        curSpeed = curSpeed.normalized * def.projectile.SpeedTilesPerTick; // 保持速度恒定
                    }
                }
            }
            
            exactPositionInt = ExactPosition + curSpeed; // 更新位置
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref exactPositionInt, "exactPosition");
            Scribe_Values.Look(ref curSpeed, "curSpeed");
            Scribe_Values.Look(ref homing, "homing", defaultValue: true);
            Scribe_Values.Look(ref destroyTicksAfterLosingTrack, "destroyTicksAfterLosingTrack", -1);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                ReflectInit();
                if (this.trackingDefInt == null)
                {
                    this.trackingDefInt = this.def.GetModExtension<TrackingBulletDef>();
                    if (this.trackingDefInt == null)
                    {
                        Log.ErrorOnce($"TrackingBulletDef is null for projectile {this.def.defName} after PostLoadInit. Creating a default instance.", this.thingIDNumber ^ 0x12345678);
                        this.trackingDefInt = new TrackingBulletDef();
                    }
                }
            }
        }

        protected override void Tick()
        {
            base.Tick(); // 调用父类Bullet的Tick，处理 ticksToImpact 减少和最终命中

            if (destroyTicksAfterLosingTrack > 0)
            {
                destroyTicksAfterLosingTrack--;
                if (destroyTicksAfterLosingTrack <= 0)
                {
                    Destroy(); // 如果自毁计时器归零，直接销毁
                    return;
                }
            }

            // 处理拖尾特效
            if (TrackingDef != null && TrackingDef.tailFleckDef != null)
            {
                Fleck_MakeFleckTick++;
                // 只有当达到延迟时间后才开始生成Fleck
                if (Fleck_MakeFleckTick >= TrackingDef.fleckDelayTicks)
                {
                    if (Fleck_MakeFleckTick >= (TrackingDef.fleckDelayTicks + TrackingDef.fleckMakeFleckTickMax))
                    {
                        Fleck_MakeFleckTick = TrackingDef.fleckDelayTicks; // 重置计时器，从延迟时间开始循环
                    }

                    Map map = base.Map;
                    int randomInRange = TrackingDef.fleckMakeFleckNum.RandomInRange;
                    Vector3 currentPosition = ExactPosition;
                    Vector3 previousPosition = lastTickPosition;

                    for (int i = 0; i < randomInRange; i++)
                    {
                        float num = (currentPosition - previousPosition).AngleFlat();
                        float velocityAngle = TrackingDef.fleckAngle.RandomInRange + num;
                        float randomInRange2 = TrackingDef.fleckScale.RandomInRange;
                        float randomInRange3 = TrackingDef.fleckSpeed.RandomInRange;
                        
                        FleckCreationData dataStatic = FleckMaker.GetDataStatic(currentPosition, map, TrackingDef.tailFleckDef, randomInRange2);
                        dataStatic.rotation = (currentPosition - previousPosition).AngleFlat();
                        dataStatic.rotationRate = TrackingDef.fleckRotation.RandomInRange;
                        dataStatic.velocityAngle = velocityAngle;
                        dataStatic.velocitySpeed = randomInRange3;
                        map.flecks.CreateFleck(dataStatic);
                    }
                }
            }
            lastTickPosition = ExactPosition; // 更新上一帧位置

            // 保存移动前的精确位置
            Vector3 exactPositionBeforeMove = exactPositionInt;

            MovementTick(); // 调用追踪移动逻辑，更新 exactPositionInt (即新的 ExactPosition)

            // 检查是否超出地图边界
            if (!ExactPosition.InBounds(base.Map))
            {
                // 如果超出地图，直接销毁，不触发 ImpactSomething()
                Destroy();
                return;
            }

            // 在调用 ProjectileCheckForFreeInterceptBetween 之前，添加近距离命中检测
            if (intendedTarget != null && intendedTarget.Thing != null && intendedTarget.Thing.Spawned)
            {
                float distanceToTarget = (ExactPosition - intendedTarget.Thing.DrawPos).magnitude;
                if (distanceToTarget <= TrackingDef.impactThreshold)
                {
                    Impact(intendedTarget.Thing); // 强制命中目标
                    return; // 命中后立即返回，不再执行后续逻辑
                }
            }

            // 检查是否有东西在路径上拦截
            // ProjectileCheckForFreeInterceptBetween 会在内部处理命中，并调用 ImpactSomething()
            // 所以这里不需要额外的 ImpactSomething() 调用
            object[] parameters = new object[2] { exactPositionBeforeMove, exactPositionInt }; // 传入移动前和移动后的位置
            
            // 调用 ProjectileCheckForFreeInterceptBetween
            // 如果它返回 true，说明有拦截，并且拦截逻辑已在内部处理。
            // 如果返回 false，说明没有拦截，子弹继续飞行。
            NonPublicFields.ProjectileCheckForFreeInterceptBetween.Invoke(this, parameters);
        }

        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            // 默认Impact逻辑，可以根据需要扩展
            base.Impact(hitThing, blockedByShield);
        }
    }
}