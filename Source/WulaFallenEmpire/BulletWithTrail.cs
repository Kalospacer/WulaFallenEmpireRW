using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace WulaFallenEmpire
{
    public class BulletWithTrail : Bullet
    {
        private TrackingBulletDef trackingDefInt;
        private int Fleck_MakeFleckTick;
        private Vector3 lastTickPosition;

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

        public override void Launch(Thing launcher, Vector3 origin, LocalTargetInfo usedTarget, LocalTargetInfo intendedTarget, ProjectileHitFlags hitFlags, bool preventFriendlyFire = false, Thing equipment = null, ThingDef targetCoverDef = null)
        {
            base.Launch(launcher, origin, usedTarget, intendedTarget, hitFlags, preventFriendlyFire, equipment, targetCoverDef);
            lastTickPosition = origin;
        }

        protected override void Tick()
        {
            base.Tick();

            // 处理拖尾特效
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
                    int randomInRange = TrackingDef.fleckMakeFleckNum.RandomInRange;
                    Vector3 currentPosition = base.ExactPosition;
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
            lastTickPosition = base.ExactPosition;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref Fleck_MakeFleckTick, "Fleck_MakeFleckTick", 0);
            Scribe_Values.Look(ref lastTickPosition, "lastTickPosition", Vector3.zero);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
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