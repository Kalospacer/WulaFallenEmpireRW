using RimWorld;
using System.Collections.Generic;
using Verse.Sound;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    public class CruiseMissileProperties : DefModExtension
    {
        public DamageDef customDamageDef;
        public int customDamageAmount = 5;
        public float customExplosionRadius = 1.1f;
        public SoundDef customSoundExplode;

        public bool useSubExplosions = true;
        public int subExplosionCount = 3;
        public float subExplosionRadius = 1.9f;
        public int subExplosionDamage = 30;
        public float subExplosionSpread = 6f;
        public DamageDef subDamageDef;
        public SoundDef subSoundExplode;
        public FleckDef tailFleckDef; // 用于配置拖尾特效的 FleckDef
        public float homingSpeed = 0.1f;
        public float initRotateAngle = 30f;
        public IntRange destroyTicksAfterLosingTrack = new IntRange(60, 120);
        public float speedChangePerTick;
        public FloatRange? speedRangeOverride;
        public float proximityFuseRange = 0f;
    }

    public class Projectile_CruiseMissile : Projectile_Explosive
    {
        private CruiseMissileProperties settings;
        protected Vector3 exactPositionInt;
        public Vector3 curSpeed;
        public bool homing = true;
        private Sustainer ambientSustainer;
        private List<ThingComp> comps;
        private int ticksToDestroy = -1;

        // Launch 方法的参数作为字段

        // 拖尾特效相关字段
        private int Fleck_MakeFleckTick;
        public int Fleck_MakeFleckTickMax = 1;
        public IntRange Fleck_MakeFleckNum = new IntRange(1, 1);
        public FloatRange Fleck_Angle = new FloatRange(-180f, 180f);
        public FloatRange Fleck_Scale = new FloatRange(1f, 1f);
        public FloatRange Fleck_Speed = new FloatRange(0f, 0f);
        public FloatRange Fleck_Rotation = new FloatRange(-180f, 180f); 

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            settings = def.GetModExtension<CruiseMissileProperties>() ?? new CruiseMissileProperties();
            this.ReflectInit();
        }

        public override void Launch(Thing launcherParam, Vector3 originParam, LocalTargetInfo usedTargetParam, LocalTargetInfo intendedTargetParam, ProjectileHitFlags hitFlagsParam, bool preventFriendlyFireParam = false, Thing equipmentParam = null, ThingDef targetCoverDefParam = null)
        {
            this.launcher = launcherParam;
            this.origin = originParam;
            this.usedTarget = usedTargetParam;
            this.intendedTarget = intendedTargetParam;
            this.HitFlags = hitFlagsParam;
            this.preventFriendlyFire = preventFriendlyFireParam;
            this.equipment = equipmentParam;
            this.targetCoverDef = targetCoverDefParam;

            this.exactPositionInt = origin.Yto0() + Vector3.up * this.def.Altitude;
            Vector3 normalized = (this.destination - origin).Yto0().normalized;
            float degrees = Rand.Range(-this.settings.initRotateAngle, this.settings.initRotateAngle);
            Vector2 vector = new Vector2(normalized.x, normalized.z);
            vector = vector.RotatedBy(degrees);
            Vector3 a = new Vector3(vector.x, 0f, vector.y);
            bool flag6 = this.settings.speedRangeOverride == null;
            if (flag6)
            {
                this.curSpeed = a * this.def.projectile.SpeedTilesPerTick;
            }
            else
            {
                this.curSpeed = a * this.settings.speedRangeOverride.Value.RandomInRange;
            }
            this.ticksToImpact = int.MaxValue;
            this.lifetime = int.MaxValue;
        }

        protected void ReflectInit()
        {
            if (NonPublicFields.Projectile_AmbientSustainer == null)
            {
                NonPublicFields.Projectile_AmbientSustainer = typeof(Projectile).GetField("ambientSustainer", BindingFlags.Instance | BindingFlags.NonPublic);
            }
            if (NonPublicFields.ThingWithComps_comps == null)
            {
                NonPublicFields.ThingWithComps_comps = typeof(ThingWithComps).GetField("comps", BindingFlags.Instance | BindingFlags.NonPublic);
            }
            if (NonPublicFields.ProjectileCheckForFreeInterceptBetween == null)
            {
                NonPublicFields.ProjectileCheckForFreeInterceptBetween = typeof(Projectile).GetMethod("CheckForFreeInterceptBetween", BindingFlags.Instance | BindingFlags.NonPublic);
            }

            bool flag = !this.def.projectile.soundAmbient.NullOrUndefined();
            if (flag)
            {
                this.ambientSustainer = (Sustainer)NonPublicFields.Projectile_AmbientSustainer.GetValue(this);
            }
            this.comps = (List<ThingComp>)NonPublicFields.ThingWithComps_comps.GetValue(this);
        }

        public float GetHitChance(Thing thing)
        {
            float num = this.settings.homingSpeed; 
            bool flag = thing == null;
            float result;
            if (flag)
            {
                result = num;
            }
            else
            {
                Pawn pawn = thing as Pawn;
                bool flag2 = pawn != null;
                if (flag2)
                {
                    num *= Mathf.Clamp(pawn.BodySize, 0.5f, 1.5f);
                    bool flag3 = pawn.GetPosture() > PawnPosture.Standing;
                    if (flag3)
                    {
                        num *= 0.5f;
                    }
                    float num2 = 1f;
                    switch (this.equipmentQuality)
                    {
                        case QualityCategory.Awful:
                            num2 = 0.5f;
                            goto IL_DD;
                        case QualityCategory.Poor:
                            num2 = 0.75f;
                            goto IL_DD;
                        case QualityCategory.Normal:
                            num2 = 1f;
                            goto IL_DD;
                        case QualityCategory.Excellent:
                            num2 = 1.1f;
                            goto IL_DD;
                        case QualityCategory.Masterwork:
                            num2 = 1.2f;
                            goto IL_DD;
                        case QualityCategory.Legendary:
                            num2 = 1.3f;
                            goto IL_DD;
                    }
                    Log.Message("Unknown QualityCategory, returning default qualityFactor = 1");
                    IL_DD:
                    num *= num2;
                }
                else
                {
                    num *= 1.5f * thing.def.fillPercent;
                }
                result = Mathf.Clamp(num, 0f, 1f);
            }
            return result;
        }

        private IEnumerable<IntVec3> GetValidCells(Map map)
        {
            if (map == null || settings == null) yield break;

            var cells = GenRadial.RadialCellsAround(
                base.Position,
                settings.subExplosionSpread,
                false
            ).Where(c => c.InBounds(map));

            var randomizedCells = cells.InRandomOrder().Take(settings.subExplosionCount);

            foreach (var cell in randomizedCells)
            {
                yield return cell;
            }
        }

        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            var map = base.Map;
            base.Impact(hitThing, blockedByShield);

            DoExplosion(
                base.Position,
                map,
                settings.customExplosionRadius,
                settings.customDamageDef,
                settings.customDamageAmount,
                settings.customSoundExplode
            );

            if (settings.useSubExplosions)
            {
                foreach (var cell in GetValidCells(map))
                {
                    DoExplosion(
                        cell,
                        map,
                        settings.subExplosionRadius,
                        settings.subDamageDef,
                        settings.subExplosionDamage,
                        settings.subSoundExplode
                    );
                }
            }
        }

        private void DoExplosion(IntVec3 pos, Map map, float radius, DamageDef dmgDef, int dmgAmount, SoundDef sound)
        {
            GenExplosion.DoExplosion(
                pos,
                map,
                radius,
                dmgDef,
                launcher,
                dmgAmount,
                ArmorPenetration,
                sound
            );
        }

        public override Quaternion ExactRotation
        {
            get
            {
                return Quaternion.LookRotation(this.curSpeed);
            }
        }
        public override Vector3 ExactPosition
        {
            get
            {
                return this.exactPositionInt;
            }
        }

        protected override void Tick()
        {
            this.ThingWithCompsTick();
            this.lifetime--;
            if (this.settings.tailFleckDef != null)
            {
                this.Fleck_MakeFleckTick++;
                if (this.Fleck_MakeFleckTick >= this.Fleck_MakeFleckTickMax)
                {
                    this.Fleck_MakeFleckTick = 0;
                    for (int i = 0; i < this.Fleck_MakeFleckNum.RandomInRange; i++)
                    {
                        FleckMaker.Static(this.ExactPosition + Gen.RandomHorizontalVector(this.Fleck_Scale.RandomInRange / 2f), base.Map, this.settings.tailFleckDef, this.Fleck_Scale.RandomInRange);
                    }
                }
            }

            bool landed = this.landed;
            if (!landed)
            {
                Vector3 exactPosition = this.ExactPosition;
                this.ticksToImpact--;
                this.MovementTick();
                bool flag = !this.ExactPosition.InBounds(base.Map);
                if (flag)
                {
                    base.Position = exactPosition.ToIntVec3();
                    this.Destroy(DestroyMode.Vanish);
                }
                else
                {
                    Vector3 exactPosition2 = this.ExactPosition;
                    object[] parameters = new object[]
                    {
                        exactPosition,
                        exactPosition2
                    };
                    bool flag2 = (bool)NonPublicFields.ProjectileCheckForFreeInterceptBetween.Invoke(this, parameters);
                    if (!flag2)
                    {
                        base.Position = this.ExactPosition.ToIntVec3();
                        bool flag3 = this.ticksToImpact == 60 && Find.TickManager.CurTimeSpeed == TimeSpeed.Normal && this.def.projectile.soundImpactAnticipate != null;
                        if (flag3)
                        {
                            this.def.projectile.soundImpactAnticipate.PlayOneShot(this);
                        }
                        bool flag4 = this.ticksToImpact <= 0;
                        if (flag4)
                        {
                            this.Impact(null);
                        }
                        else
                        {
                            bool flag5 = this.ambientSustainer != null;
                            if (flag5)
                            {
                                this.ambientSustainer.Maintain();
                            }
                        }
                    }
                }
            }
        }

        private void MovementTick()
        {
            if (this.homing)
            {
                if (this.intendedTarget != null && this.intendedTarget.Thing != null)
                {
                    Vector3 vector = (this.intendedTarget.Thing.DrawPos - this.exactPositionInt).normalized;
                    this.curSpeed = Vector3.RotateTowards(this.curSpeed, vector * this.curSpeed.magnitude, this.settings.homingSpeed, 0f);
                }
                else if (this.ticksToDestroy == -1)
                {
                    this.ticksToDestroy = this.settings.destroyTicksAfterLosingTrack.RandomInRange;
                }
            }
            if (this.ticksToDestroy > 0)
            {
                this.ticksToDestroy--;
                if (this.ticksToDestroy == 0)
                {
                    this.Destroy(DestroyMode.Vanish);
                    return;
                }
            }
            if (this.settings.speedChangePerTick != 0f)
            {
                this.curSpeed = this.curSpeed.normalized * (this.curSpeed.magnitude + this.settings.speedChangePerTick);
            }
            if (this.settings.proximityFuseRange > 0f)
            {
                if (this.intendedTarget != null && this.intendedTarget.Thing != null && (this.intendedTarget.Thing.DrawPos - this.exactPositionInt).magnitude < this.settings.proximityFuseRange)
                {
                    this.Impact(null);
                    return;
                }
            }

            this.exactPositionInt += this.curSpeed;
        }

        protected void ThingWithCompsTick()
        {
            if (this.comps != null)
            {
                for (int i = 0; i < this.comps.Count; i++)
                {
                    this.comps[i].CompTick();
                }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look<Vector3>(ref this.exactPositionInt, "exactPosition", default(Vector3), false);
            Scribe_Values.Look<Vector3>(ref this.curSpeed, "curSpeed", default(Vector3), false);
            Scribe_Values.Look<bool>(ref this.homing, "homing", true, false);
            Scribe_Values.Look<int>(ref this.ticksToDestroy, "ticksToDestroy", -1, false);
        }
    }

    public static class NonPublicFields
    {
        public static FieldInfo Projectile_AmbientSustainer;
        public static FieldInfo ThingWithComps_comps;
        public static MethodInfo ProjectileCheckForFreeInterceptBetween;
    }
}