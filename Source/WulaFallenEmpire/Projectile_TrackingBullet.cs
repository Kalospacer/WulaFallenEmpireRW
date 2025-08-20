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
            
            exactPositionInt = ExactPosition + curSpeed; // 更新位置
        }

        protected override void Tick()
        {
            base.Tick(); // 调用父类Bullet的Tick，处理一些基本逻辑，如lifetime, ticksToImpact

            MovementTick(); // 调用追踪移动逻辑

            // 检查是否撞到东西或超出地图
            Vector3 exactPosition = ExactPosition; // 之前的ExactPosition
            ticksToImpact--; // 减少impact计时器

            if (!ExactPosition.InBounds(base.Map)) // 超出地图边界
            {
                base.Position = exactPosition.ToIntVec3(); // 设回旧位置，然后销毁
                Destroy();
                return;
            }

            // 检查是否有东西在路径上拦截
            Vector3 exactPositionAfterMove = ExactPosition; // 移动后的ExactPosition
            object[] parameters = new object[2] { exactPosition, exactPositionAfterMove };
            if (!(bool)NonPublicFields.ProjectileCheckForFreeInterceptBetween.Invoke(this, parameters))
            {
                base.Position = ExactPosition.ToIntVec3(); // 更新位置到当前精确位置
                if (ticksToImpact <= 0) // 达到impact时间
                {
                    ImpactSomething(); // 触发Impact
                }
            }
        }

        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            // 默认Impact逻辑，可以根据需要扩展
            base.Impact(hitThing, blockedByShield);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref exactPositionInt, "exactPosition");
            Scribe_Values.Look(ref curSpeed, "curSpeed");
            Scribe_Values.Look(ref homing, "homing", defaultValue: true);
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
    }
}