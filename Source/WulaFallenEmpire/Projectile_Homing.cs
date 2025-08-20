using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace WulaFallenEmpire
{
    public class Projectile_Homing : Bullet
    {
        private HomingProjectileDef homingDefInt;

        private Sustainer ambientSustainer;

        private List<ThingComp> comps;

        protected Vector3 exactPositionInt;

        public Vector3 curSpeed;

        public bool homing = true;
        private int Fleck_MakeFleckTick; // 拖尾特效的计时器
        private Vector3 lastTickPosition; // 记录上一帧的位置，用于计算移动方向

        private static class NonPublicFields
        {
            public static FieldInfo Projectile_AmbientSustainer = typeof(Projectile).GetField("ambientSustainer", BindingFlags.Instance | BindingFlags.NonPublic);
            public static FieldInfo ThingWithComps_comps = typeof(ThingWithComps).GetField("comps", BindingFlags.Instance | BindingFlags.NonPublic);
            public static MethodInfo ProjectileCheckForFreeInterceptBetween = typeof(Projectile).GetMethod("CheckForFreeInterceptBetween", BindingFlags.Instance | BindingFlags.NonPublic);
        }

        public HomingProjectileDef HomingDef
        {
            get
            {
                if (homingDefInt == null)
                {
                    homingDefInt = def.GetModExtension<HomingProjectileDef>();
                    if (homingDefInt == null)
                    {
                        Log.ErrorOnce($"HomingProjectileDef for {this.def.defName} is null. Creating a default instance.", this.thingIDNumber ^ 0x12345678);
                        this.homingDefInt = new HomingProjectileDef();
                    }
                }
                return homingDefInt;
            }
        }

        public override Vector3 ExactPosition => exactPositionInt;

        public override Quaternion ExactRotation => Quaternion.LookRotation(curSpeed);

        public override void Launch(Thing launcher, Vector3 origin, LocalTargetInfo usedTarget, LocalTargetInfo intendedTarget, ProjectileHitFlags hitFlags, bool preventFriendlyFire = false, Thing equipment = null, ThingDef targetCoverDef = null)
        {
            bool flag = false;
            if (usedTarget.HasThing && usedTarget.Thing is IAttackTarget)
            {
                if (Rand.Chance(GetHitChance(usedTarget.Thing)))
                {
                    hitFlags |= ProjectileHitFlags.IntendedTarget;
                    intendedTarget = usedTarget;
                    flag = true;
                }
            }
            else if (Rand.Chance(GetHitChance(intendedTarget.Thing)))
            {
                hitFlags |= ProjectileHitFlags.IntendedTarget;
                usedTarget = intendedTarget;
                flag = true;
            }
            if (flag)
            {
                hitFlags &= ~ProjectileHitFlags.IntendedTarget;
            }
            base.Launch(launcher, origin, usedTarget, intendedTarget, hitFlags, preventFriendlyFire, equipment, targetCoverDef);
            exactPositionInt = origin.Yto0() + Vector3.up * def.Altitude;
            lastTickPosition = origin; // 初始化 lastTickPosition
            Vector3 normalized = (destination - origin).Yto0().normalized;
            float degrees = Rand.Range(0f - HomingDef.initRotateAngle, HomingDef.initRotateAngle);
            Vector2 v = new Vector2(normalized.x, normalized.z);
            v = v.RotatedBy(degrees);
            Vector3 vector = new Vector3(v.x, 0f, v.y);
            // 检查 HomingDef.speedRangeOverride 是否有值
            if (!HomingDef.speedRangeOverride.HasValue) 
            {
                curSpeed = vector * def.projectile.SpeedTilesPerTick;
            }
            else
            {
                curSpeed = vector * HomingDef.SpeedRangeTilesPerTickOverride.RandomInRange;
            }
            ReflectInit();
        }

        protected void ReflectInit()
        {
            if (!def.projectile.soundAmbient.NullOrUndefined())
            {
                ambientSustainer = (Sustainer)NonPublicFields.Projectile_AmbientSustainer.GetValue(this);
            }
            comps = (List<ThingComp>)NonPublicFields.ThingWithComps_comps.GetValue(this);
        }

        public float GetHitChance(Thing thing)
        {
            if (this.HomingDef == null)
            {
                Log.ErrorOnce("HomingDef is null for projectile " + this.def.defName + ". Returning default hitChance.", this.thingIDNumber ^ 0x12345678);
                return 0.7f;
            }

            float hitChance = HomingDef.hitChance;
            if (thing == null)
            {
                return hitChance;
            }
            if (thing is Pawn pawn)
            {
                hitChance *= Mathf.Clamp(pawn.BodySize, 0.5f, 1.5f);
                if (pawn.GetPosture() != 0)
                {
                    hitChance *= 0.5f;
                }
                float num = 1f;
                switch (equipmentQuality)
                {
                case QualityCategory.Awful:
                    num = 0.5f;
                    break;
                case QualityCategory.Poor:
                    num = 0.75f;
                    break;
                case QualityCategory.Normal:
                    num = 1f;
                    break;
                case QualityCategory.Excellent:
                    num = 1.1f;
                    break;
                case QualityCategory.Masterwork:
                    num = 1.2f;
                    break;
                case QualityCategory.Legendary:
                    num = 1.3f;
                    break;
                default:
                    Log.Message("Unknown QualityCategory, returning default qualityFactor = 1");
                    break;
                }
                hitChance *= num;
            }
            else
            {
                hitChance *= 1.5f * thing.def.fillPercent;
            }
            return Mathf.Clamp(hitChance, 0f, 1f);
        }

        public virtual void MovementTick()
        {
            Vector3 vect = ExactPosition + curSpeed;
            ShootLine shootLine = new ShootLine(ExactPosition.ToIntVec3(), vect.ToIntVec3());
            Vector3 vector = (intendedTarget.Cell.ToVector3() - ExactPosition).Yto0();
            if (homing)
            {
                Vector3 vector2 = vector.normalized - curSpeed.normalized;
                if (vector2.sqrMagnitude >= 1.414f)
                {
                    homing = false;
                    lifetime = HomingDef.destroyTicksAfterLosingTrack.RandomInRange;
                    ticksToImpact = lifetime;
                    base.HitFlags &= ~ProjectileHitFlags.IntendedTarget;
                    base.HitFlags |= ProjectileHitFlags.NonTargetPawns;
                    base.HitFlags |= ProjectileHitFlags.NonTargetWorld;
                }
                else
                {
                    curSpeed += vector2 * HomingDef.homingSpeed * curSpeed.magnitude;
                }
            }
            foreach (IntVec3 item in shootLine.Points())
            {
                if (!((intendedTarget.Cell - item).SqrMagnitude <= HomingDef.proximityFuseRange * HomingDef.proximityFuseRange))
                {
                    continue;
                }
                homing = false;
                lifetime = HomingDef.destroyTicksAfterLosingTrack.RandomInRange;
                if ((base.HitFlags & ProjectileHitFlags.IntendedTarget) == ProjectileHitFlags.IntendedTarget || HomingDef.proximityFuseRange > 0f)
                {
                    lifetime = 0;
                    ticksToImpact = 0;
                    vect = item.ToVector3();
                    if (Find.TickManager.CurTimeSpeed == TimeSpeed.Normal && def.projectile.soundImpactAnticipate != null)
                    {
                        def.projectile.soundImpactAnticipate.PlayOneShot(this);
                    }
                }
            }
            exactPositionInt = vect;
            curSpeed *= (curSpeed.magnitude + HomingDef.SpeedChangeTilesPerTickOverride) / curSpeed.magnitude;
        }

        protected override void Tick()
        {
            ThingWithCompsTick();
            lifetime--;

            if (lifetime <= 0)
            {
                Destroy();
                return;
            }
            
            // 处理拖尾特效
            if (HomingDef != null && HomingDef.tailFleckDef != null)
            {
                Fleck_MakeFleckTick++;
                if (Fleck_MakeFleckTick >= HomingDef.fleckMakeFleckTickMax)
                {
                    Fleck_MakeFleckTick = 0;
                    Map map = base.Map;
                    int randomInRange = HomingDef.fleckMakeFleckNum.RandomInRange;
                    Vector3 currentPosition = ExactPosition;
                    Vector3 previousPosition = lastTickPosition;

                    for (int i = 0; i < randomInRange; i++)
                    {
                        float num = (currentPosition - previousPosition).AngleFlat();
                        float velocityAngle = HomingDef.fleckAngle.RandomInRange + num;
                        float randomInRange2 = HomingDef.fleckScale.RandomInRange;
                        float randomInRange3 = HomingDef.fleckSpeed.RandomInRange;
                        
                        FleckCreationData dataStatic = FleckMaker.GetDataStatic(currentPosition, map, HomingDef.tailFleckDef, randomInRange2);
                        dataStatic.rotation = (currentPosition - previousPosition).AngleFlat();
                        dataStatic.rotationRate = HomingDef.fleckRotation.RandomInRange;
                        dataStatic.velocityAngle = velocityAngle;
                        dataStatic.velocitySpeed = randomInRange3;
                        map.flecks.CreateFleck(dataStatic);
                    }
                }
            }
            lastTickPosition = ExactPosition; // 更新上一帧位置

            // 移除 if (landed) return; 以确保子弹落地后也能正常销毁
            Vector3 exactPosition = ExactPosition;
            ticksToImpact--;
            MovementTick();
            if (!ExactPosition.InBounds(base.Map))
            {
                base.Position = exactPosition.ToIntVec3();
                Destroy();
                return;
            }
            Vector3 exactPosition2 = ExactPosition;
            object[] parameters = new object[2] { exactPosition, exactPosition2 };
            if (!(bool)NonPublicFields.ProjectileCheckForFreeInterceptBetween.Invoke(this, parameters))
            {
                base.Position = ExactPosition.ToIntVec3();
                if (ticksToImpact == 60 && Find.TickManager.CurTimeSpeed == TimeSpeed.Normal && def.projectile.soundImpactAnticipate != null)
                {
                    def.projectile.soundImpactAnticipate.PlayOneShot(this);
                }
                if (ticksToImpact <= 0)
                {
                    ImpactSomething();
                }
                else if (ambientSustainer != null)
                {
                    ambientSustainer.Maintain();
                }
            }
        }

        private void ThingWithCompsTick()
        {
            if (comps != null)
            {
                int i = 0;
                for (int count = comps.Count; i < count; i++)
                {
                    comps[i].CompTick();
                }
            }
        }

        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            Map map = base.Map;
            IntVec3 position = base.Position;
            base.Impact(hitThing, blockedByShield);
            if (HomingDef.extraProjectile != null)
            {
                if (hitThing != null && hitThing.Spawned)
                {
                    ((Projectile)GenSpawn.Spawn(HomingDef.extraProjectile, base.Position, map, WipeMode.Vanish)).Launch(launcher, ExactPosition, hitThing, hitThing, ProjectileHitFlags.All, false, null, null);
                }
                else
                {
                    ((Projectile)GenSpawn.Spawn(HomingDef.extraProjectile, base.Position, map, WipeMode.Vanish)).Launch(launcher, ExactPosition, position, position, ProjectileHitFlags.All, false, null, null);
                }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref exactPositionInt, "exactPosition");
            Scribe_Values.Look(ref curSpeed, "curSpeed");
            Scribe_Values.Look(ref homing, "homing", defaultValue: false);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                ReflectInit();
                if (this.homingDefInt == null)
                {
                    this.homingDefInt = this.def.GetModExtension<HomingProjectileDef>();
                    if (this.homingDefInt == null)
                    {
                        Log.ErrorOnce($"HomingProjectileDef is null for projectile {this.def.defName} after PostLoadInit. Creating a default instance.", this.thingIDNumber ^ 0x12345678);
                        this.homingDefInt = new HomingProjectileDef();
                    }
                }
            }
        }
    }
}