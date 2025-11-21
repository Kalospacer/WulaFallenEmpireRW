using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    public class Projectile_NorthArcTrail : Projectile_Explosive
    {
        // --- 弹道部分变量 ---
        // 定义向北偏移的高度（格数），建议在XML中通过 <projectile> 参数无法直接传参时硬编码或扩展 ThingDef
        // 这里为了方便，设定默认值为 10，你可以根据需要修改
        public float northOffsetDistance = 10f; 

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
            Scribe_Values.Look(ref northOffsetDistance, "northOffsetDistance", 10f);

            // 保存尾迹数据
            Scribe_Values.Look(ref Fleck_MakeFleckTick, "Fleck_MakeFleckTick", 0);
            Scribe_Values.Look(ref lastTickPosition, "lastTickPosition", Vector3.zero);
        }

        public override void Launch(Thing launcher, Vector3 origin, LocalTargetInfo usedTarget, LocalTargetInfo intendedTarget, ProjectileHitFlags hitFlags, bool preventFriendlyFire = false, Thing equipment = null, ThingDef targetCoverDef = null)
        {
            base.Launch(launcher, origin, usedTarget, intendedTarget, hitFlags, preventFriendlyFire, equipment, targetCoverDef);

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

            // --- 初始化尾迹 ---
            lastTickPosition = origin;
        }

        protected override void Tick()
        {
            // 注意：这里不直接调用 base.Tick() 的物理位移部分，因为我们要自己控制位置
            // 但我们需要 Projectile_Explosive 的倒计时逻辑，所以我们在方法末尾调用 TickInterval

            if (!initialized)
            {
                base.Tick();
                return;
            }

            ticksFlying++;

            // 1. 计算当前帧的新位置 (贝塞尔曲线 + 高度)
            float t = (float)ticksFlying / (float)totalTicks;
            if (t > 1f) t = 1f;

            float u = 1 - t;
            // 水平位移 (贝塞尔)
            Vector3 nextPos = (u * u * originPos) + (2 * u * t * bezierControlPoint) + (t * t * destinationPos);
            // 垂直高度 (抛物线)
            float arcHeight = def.projectile.arcHeightFactor * GenMath.InverseParabola(t);
            nextPos.y = arcHeight;

            // 设置物体确切位置
            this.Position = nextPos.ToIntVec3();

            // 2. 处理拖尾特效 (合并的代码)
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

            // 3. 更新旋转角度 (Visual) 和 记录上一帧位置
            if (lastTickPosition != nextPos)
            {
                 if ((nextPos - lastTickPosition).MagnitudeHorizontalSquared() > 0.001f)
                 {
                     this.Rotation = Rot4.FromAngleFlat((nextPos - lastTickPosition).AngleFlat());
                 }
            }
            lastTickPosition = nextPos;

            // 4. 判定到达目标或倒计时爆炸
            if (ticksFlying >= totalTicks)
            {
                Impact(null); 
                return;
            }

            // 5. 处理 Projectile_Explosive 的内部倒计时 (如果 ticksToDetonation 被设置了)
            base.TickInterval(1);
        }

        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            base.Impact(hitThing, blockedByShield);
        }
    }
}