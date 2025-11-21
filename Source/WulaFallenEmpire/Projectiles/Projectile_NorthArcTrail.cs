using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    public class Projectile_NorthArcTrail : Projectile_Explosive
    {
        // --- 弹道部分变量 ---
        public float northOffsetDistance = 0f;
        
        private Vector3 exactPositionInt;
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
        private Vector3 lastTickPosition;

        // 新增：绘制相关变量
        private float currentArcHeight;
        private const float DRAW_ALTITUDE_OFFSET = 15f; // 增加绘制高度偏移

        public TrackingBulletDef TrackingDef
        {
            get
            {
                if (trackingDefInt == null)
                {
                    trackingDefInt = def.GetModExtension<TrackingBulletDef>();
                    if (trackingDefInt == null)
                    {
                        trackingDefInt = new TrackingBulletDef();
                    }
                }
                return trackingDefInt;
            }
        }

        // 修改：重写绘制位置，确保在正确的高度
        public override Vector3 ExactPosition 
        { 
            get
            {
                if (!initialized)
                    return base.ExactPosition;
                    
                // 返回计算的位置，但保持Y轴为绘制高度
                Vector3 pos = exactPositionInt;
                pos.y = def.Altitude + currentArcHeight + DRAW_ALTITUDE_OFFSET;
                return pos;
            }
        }

        public override Quaternion ExactRotation => Quaternion.LookRotation(GetCurrentDirection());

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref originPos, "originPos");
            Scribe_Values.Look(ref destinationPos, "destinationPos");
            Scribe_Values.Look(ref bezierControlPoint, "bezierControlPoint");
            Scribe_Values.Look(ref ticksFlying, "ticksFlying", 0);
            Scribe_Values.Look(ref totalTicks, "totalTicks", 0);
            Scribe_Values.Look(ref initialized, "initialized", false);
            Scribe_Values.Look(ref northOffsetDistance, "northOffsetDistance", 0f);
            Scribe_Values.Look(ref exactPositionInt, "exactPositionInt", Vector3.zero);
            Scribe_Values.Look(ref curveSteepness, "curveSteepness", 1f);
            Scribe_Values.Look(ref currentArcHeight, "currentArcHeight", 0f);

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
                northOffsetDistance = def.projectile.arcHeightFactor * 3;
            }

            // --- 初始化弹道 ---
            originPos = origin;
            destinationPos = usedTarget.CenterVector3;

            float speed = def.projectile.speed;
            if (speed <= 0) speed = 1f;
            
            float distance = (originPos - destinationPos).MagnitudeHorizontal();
            totalTicks = Mathf.CeilToInt(distance / speed * 100f);
            if (totalTicks < 1) totalTicks = 1;
            
            ticksFlying = 0;

            // 贝塞尔曲线计算
            Vector3 midPoint = (originPos + destinationPos) / 2f;
            Vector3 apexPoint = midPoint + new Vector3(0, 0, northOffsetDistance);
            bezierControlPoint = 2f * apexPoint - midPoint;

            initialized = true;
            exactPositionInt = origin;
            lastTickPosition = origin;
            currentArcHeight = 0f;
        }

        protected override void Tick()
        {
            base.Tick();

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

            // 1. 计算当前帧的新位置
            float t = (float)ticksFlying / (float)totalTicks;
            if (t > 1f) t = 1f;

            float u = 1 - t;
            // 水平位移 (贝塞尔)
            Vector3 nextPos = (u * u * originPos) + (2 * u * t * bezierControlPoint) + (t * t * destinationPos);
            
            // 垂直高度 (抛物线)
            currentArcHeight = def.projectile.arcHeightFactor * GenMath.InverseParabola(t);
            nextPos.y = 0f; // 水平位置不包含高度

            if (!nextPos.ToIntVec3().InBounds(base.Map))
            {
                this.Destroy();
                return;
            }

            exactPositionInt = nextPos;
            
            // 2. 处理拖尾特效
            if (TrackingDef != null && TrackingDef.tailFleckDef != null)
            {
                Fleck_MakeFleckTick++;
                if (Fleck_MakeFleckTick >= TrackingDef.fleckDelayTicks)
                {
                    if (Fleck_MakeFleckTick >= (TrackingDef.fleckDelayTicks + TrackingDef.fleckMakeFleckTickMax))
                    {
                        Fleck_MakeFleckTick = TrackingDef.fleckDelayTicks;
                    }

                    Map map = base.Map;
                    if (map != null)
                    {
                        int count = TrackingDef.fleckMakeFleckNum.RandomInRange;
                        Vector3 currentPosition = this.ExactPosition; // 使用重写后的ExactPosition
                        Vector3 previousPosition = lastTickPosition;
                        
                        if ((currentPosition - previousPosition).MagnitudeHorizontalSquared() > 0.0001f)
                        {
                            float moveAngle = (currentPosition - previousPosition).AngleFlat();

                            for (int i = 0; i < count; i++)
                            {
                                float velocityAngle = TrackingDef.fleckAngle.RandomInRange + moveAngle;
                                
                                FleckCreationData dataStatic = FleckMaker.GetDataStatic(currentPosition, map, TrackingDef.tailFleckDef, TrackingDef.fleckScale.RandomInRange);
                                dataStatic.rotation = moveAngle;
                                dataStatic.rotationRate = TrackingDef.fleckRotation.RandomInRange;
                                dataStatic.velocityAngle = velocityAngle;
                                dataStatic.velocitySpeed = TrackingDef.fleckSpeed.RandomInRange;
                                map.flecks.CreateFleck(dataStatic);
                            }
                        }
                    }
                }
            }

            lastTickPosition = ExactPosition; // 使用重写后的ExactPosition

            if (ticksFlying >= totalTicks)
            {
                Impact(null);
                return;
            }
        }
        
        // 修改：重写绘制方法，确保正确绘制
        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            if (!initialized)
            {
                base.DrawAt(drawLoc, flip);
                return;
            }

            // 使用我们计算的位置进行绘制
            Vector3 finalDrawPos = ExactPosition;
            
            // 绘制阴影
            if (def.projectile.shadowSize > 0f)
            {
                DrawShadow(finalDrawPos, currentArcHeight);
            }

            Quaternion rotation = ExactRotation;
            if (def.projectile.spinRate != 0f)
            {
                float spinAngle = 60f / def.projectile.spinRate;
                rotation = Quaternion.AngleAxis((float)Find.TickManager.TicksGame % spinAngle / spinAngle * 360f, Vector3.up);
            }

            // 使用正确的绘制方法
            if (def.projectile.useGraphicClass)
            {
                Graphic.Draw(finalDrawPos, base.Rotation, this, rotation.eulerAngles.y);
            }
            else
            {
                Graphics.DrawMesh(MeshPool.GridPlane(def.graphicData.drawSize), finalDrawPos, rotation, DrawMat, 0);
            }
            
            Comps_PostDraw();
        }

        // 修改：重写阴影绘制，使用正确的高度
        private void DrawShadow(Vector3 drawLoc, float height)
        {
            Material shadowMat = MaterialPool.MatFrom("Things/Skyfaller/SkyfallerShadowCircle", ShaderDatabase.Transparent);
            if (shadowMat == null) return;

            float shadowSize = def.projectile.shadowSize * Mathf.Lerp(1f, 0.6f, height / (def.projectile.arcHeightFactor + 1f));
            Vector3 scale = new Vector3(shadowSize, 1f, shadowSize);
            Vector3 shadowOffset = new Vector3(0f, -0.01f, 0f);
            
            Matrix4x4 matrix = Matrix4x4.TRS(drawLoc + shadowOffset, Quaternion.identity, scale);
            Graphics.DrawMesh(MeshPool.plane10, matrix, shadowMat, 0);
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

            float u = 1 - t;
            Vector3 tangent = 2 * u * (bezierControlPoint - originPos) + 2 * t * (destinationPos - bezierControlPoint);
            
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
