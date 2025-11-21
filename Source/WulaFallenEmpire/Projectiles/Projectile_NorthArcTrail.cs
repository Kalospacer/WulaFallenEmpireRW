using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    public class Projectile_NorthArcTrail : Projectile_Explosive
    {
        // --- 弹道部分变量 ---
        // 通过ModExtension配置的向北偏移高度
        public float northOffsetDistance = 0f;
        
        private Vector3 exactPositionInt; // 用于存储我们自己计算的位置
        private float curveSteepness = 1f;

        private Vector3 originPos;
        private Vector3 destinationPos;
        private Vector3 bezierControlPoint;
        private int ticksFlying;
        private int totalTicks;
        private bool initialized = false;

        // --- 尾迹部分变量 ---
        private TrackingBulletDef trackingDefInt;
        private int Fleck_MakeFleckTick;
        private Vector3 lastTickPosition; // 记录上一帧位置用于计算拖尾方向

        // 获取 XML 中的扩展数据
        public TrackingBulletDef TrackingDef
        {
            get
            {
                if (trackingDefInt == null)
                {
                    trackingDefInt = def.GetModExtension<TrackingBulletDef>();
                    if (trackingDefInt == null)
                    {
                        // 如果没配置，给一个空的默认值防止报错，或者只报错一次
                        trackingDefInt = new TrackingBulletDef();
                    }
                }
                return trackingDefInt;
            }
        }

        public override Vector3 ExactPosition => exactPositionInt; // 重写属性，让游戏获取我们计算的位置
        public override Quaternion ExactRotation => Quaternion.LookRotation(GetCurrentDirection()); // 弹头朝向当前移动方向

        public override void ExposeData()
        {
            base.ExposeData();
            // 保存弹道数据
            Scribe_Values.Look(ref originPos, "originPos");
            Scribe_Values.Look(ref destinationPos, "destinationPos");
            Scribe_Values.Look(ref bezierControlPoint, "bezierControlPoint");
            Scribe_Values.Look(ref ticksFlying, "ticksFlying", 0);
            Scribe_Values.Look(ref totalTicks, "totalTicks", 0);
            Scribe_Values.Look(ref initialized, "initialized", false);
            Scribe_Values.Look(ref northOffsetDistance, "northOffsetDistance", 0f);
            Scribe_Values.Look(ref exactPositionInt, "exactPositionInt", Vector3.zero);
            Scribe_Values.Look(ref curveSteepness, "curveSteepness", 1f);

            // 保存尾迹数据
            Scribe_Values.Look(ref Fleck_MakeFleckTick, "Fleck_MakeFleckTick", 0);
            Scribe_Values.Look(ref lastTickPosition, "lastTickPosition", Vector3.zero);
        }

        public override void Launch(Thing launcher, Vector3 origin, LocalTargetInfo usedTarget, LocalTargetInfo intendedTarget, ProjectileHitFlags hitFlags, bool preventFriendlyFire = false, Thing equipment = null, ThingDef targetCoverDef = null)
        {
            base.Launch(launcher, origin, usedTarget, intendedTarget, hitFlags, preventFriendlyFire, equipment, targetCoverDef);

            // 获取北向偏移配置
            NorthArcModExtension arcExtension = def.GetModExtension<NorthArcModExtension>();
            if (arcExtension != null)
            {
                northOffsetDistance = arcExtension.northOffsetDistance;
                curveSteepness = arcExtension.curveSteepness;
            }
            else
            {
                // 如果没有配置，则使用默认值，或者从 projectile.arcHeightFactor 获取参考值
                northOffsetDistance = def.projectile.arcHeightFactor * 3; // 将arcHeightFactor转换为北向偏移距离
            }

            // --- 初始化弹道 ---
            originPos = origin;
            destinationPos = usedTarget.CenterVector3;

            float speed = def.projectile.speed;
            if (speed <= 0) speed = 1f;
            
            // 计算直线距离估算时间
            float distance = (originPos - destinationPos).MagnitudeHorizontal();
            totalTicks = Mathf.CeilToInt(distance / speed * 100f);
            if (totalTicks < 1) totalTicks = 1;
            
            ticksFlying = 0;

            // 贝塞尔曲线计算：
            // 中点
            Vector3 midPoint = (originPos + destinationPos) / 2f;
            // 顶点 (中点向北偏移 X 格)
            Vector3 apexPoint = midPoint + new Vector3(0, 0, northOffsetDistance);
            // 控制点 P1 = 2 * 顶点 - 中点
            bezierControlPoint = 2f * apexPoint - midPoint;

            initialized = true;
            
            // 初始化我们自己的位置
            exactPositionInt = origin;

            // --- 初始化尾迹 ---
            lastTickPosition = origin;
        }

        protected override void Tick()
        {
            // 首先调用base.Tick()，让它处理组件更新(比如拖尾特效)和ticksToImpact
            base.Tick();

            // 如果base.Tick()已经处理了撞击，我们就不再继续
            if (this.Destroyed)
            {
                return;
            }

            if (!initialized)
            {
                base.Tick();
                return;
            }

            ticksFlying++;

            // 1. 计算当前帧的新位置 (贝塞尔曲线)
            float t = (float)ticksFlying / (float)totalTicks;
            if (t > 1f) t = 1f;

            float u = 1 - t;
            // 水平位移 (贝塞尔)
            Vector3 nextPos = (u * u * originPos) + (2 * u * t * bezierControlPoint) + (t * t * destinationPos);
            // 垂直高度 (抛物线)
            float arcHeight = def.projectile.arcHeightFactor * GenMath.InverseParabola(t);
            nextPos.y = arcHeight;

            // 检查边界
            if (!nextPos.ToIntVec3().InBounds(base.Map))
            {
                this.Destroy();
                return;
            }

            // 更新我们自己的位置
            exactPositionInt = nextPos;
            
            // 2. 处理拖尾特效
            // 只有当这一帧移动了，且配置了 DefModExtension 时才生成
            if (TrackingDef != null && TrackingDef.tailFleckDef != null)
            {
                Fleck_MakeFleckTick++;
                // 检查生成间隔
                if (Fleck_MakeFleckTick >= TrackingDef.fleckDelayTicks)
                {
                    // 简单的循环计时重置逻辑
                    if (Fleck_MakeFleckTick >= (TrackingDef.fleckDelayTicks + TrackingDef.fleckMakeFleckTickMax))
                    {
                        Fleck_MakeFleckTick = TrackingDef.fleckDelayTicks;
                    }

                    Map map = base.Map;
                    // 只有当在地图内时才生成
                    if (map != null)
                    {
                        int count = TrackingDef.fleckMakeFleckNum.RandomInRange;
                        Vector3 currentPosition = this.ExactPosition;
                        Vector3 previousPosition = lastTickPosition;
                        
                        // 仅当有位移时才计算角度，防止原地鬼畜
                        if ((currentPosition - previousPosition).MagnitudeHorizontalSquared() > 0.0001f)
                        {
                            float moveAngle = (currentPosition - previousPosition).AngleFlat();

                            for (int i = 0; i < count; i++)
                            {
                                // 这里的逻辑完全照搬原来的 BulletWithTrail
                                float velocityAngle = TrackingDef.fleckAngle.RandomInRange + moveAngle;
                                
                                FleckCreationData dataStatic = FleckMaker.GetDataStatic(currentPosition, map, TrackingDef.tailFleckDef, TrackingDef.fleckScale.RandomInRange);
                                dataStatic.rotation = moveAngle; // 粒子朝向跟随移动方向
                                dataStatic.rotationRate = TrackingDef.fleckRotation.RandomInRange;
                                dataStatic.velocityAngle = velocityAngle;
                                dataStatic.velocitySpeed = TrackingDef.fleckSpeed.RandomInRange;
                                map.flecks.CreateFleck(dataStatic);
                            }
                        }
                    }
                }
            }

            // 3. 更新上一帧位置
            lastTickPosition = nextPos;

            // 4. 判定到达目标或倒计时爆炸
            if (ticksFlying >= totalTicks)
            {
                Impact(null);
                return;
            }
        }
        
        // 计算当前位置的切线方向
        private Vector3 GetCurrentDirection()
        {
            if (!initialized || totalTicks <= 0)
            {
                return destinationPos - originPos;
            }

            float t = (float)ticksFlying / (float)totalTicks;
            if (t > 1f) t = 1f;

            // 计算贝塞尔曲线的导数（切线向量）
            // 对于二次贝塞尔曲线 B(t) = (1-t)²P₀ + 2(1-t)tP₁ + t²P₂
            // 导数 B'(t) = 2(1-t)(P₁-P₀) + 2t(P₂-P₁)
            float u = 1 - t;
            Vector3 tangent = 2 * u * (bezierControlPoint - originPos) + 2 * t * (destinationPos - bezierControlPoint);
            
            // 如果切线向量为零，则使用默认方向
            if (tangent.MagnitudeHorizontalSquared() < 0.0001f)
            {
                return (destinationPos - originPos).normalized;
            }
            
            return tangent.normalized;
        }

        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            base.Impact(hitThing, blockedByShield);
        }
    }
}