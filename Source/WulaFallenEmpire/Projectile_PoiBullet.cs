using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using System.Reflection;
using Verse.AI;

namespace WulaFallenEmpire
{
    public class Projectile_PoiBullet : Bullet
    {
        // Projectile_Homing 的字段
        private HomingProjectileDef homingDefInt;
        private Sustainer ambientSustainer;
        private List<ThingComp> comps;
        protected Vector3 exactPositionInt;
        public Vector3 curSpeed;
        public bool homing = true;
        private int Fleck_MakeFleckTick;
        private Vector3 lastTickPosition;

        // Projectile_Homing_Explosive 的字段
        private int ticksToDetonation;

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
            lastTickPosition = origin;
            Vector3 normalized = (destination - origin).Yto0().normalized;
            float degrees = Rand.Range(0f - HomingDef.initRotateAngle, HomingDef.initRotateAngle);
            Vector2 v = new Vector2(normalized.x, normalized.z);
            v = v.RotatedBy(degrees);
            Vector3 vector = new Vector3(v.x, 0f, v.y);
            if (!HomingDef.speedRangeOverride.HasValue)
            {
                curSpeed = vector * def.projectile.SpeedTilesPerTick;
            }
            else
            {
                curSpeed = vector * HomingDef.SpeedRangeTilesPerTickOverride.RandomInRange;
            }
            ReflectInit();

            // Projectile_PoiBullet 原始逻辑中的部分初始化
            this.flag2 = false; // 重置RandFactor的标志
            this.flag3 = true; // 重置CanHitTarget的标志
            this.CalHit = false; // 重置命中计算结果
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
            // Projectile_Homing 的 Tick 逻辑
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
            lastTickPosition = ExactPosition;

            // Projectile_Homing_Explosive 的 Tick 逻辑
            if (HomingDef.isExplosive && HomingDef.explosionDelay > 0)
            {
                if (ticksToDetonation > 0)
                {
                    ticksToDetonation--;
                    if (ticksToDetonation <= 0)
                    {
                        Explode();
                    }
                }
            }

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

            // Projectile_PoiBullet 原始逻辑中的部分Tick
            this.tickcount++;
            bool flag = this.flag3;
            if (flag)
            {
                this.CalHit = this.CanHitTarget_Poi(); // 使用重命名后的方法
                this.flag3 = false;
            }
            bool flag2 = !this.CalHit;
            if (flag2)
            {
                this.FindRandCell(this.intendedTarget.CenterVector3);
            }
            bool flag3_poi = this.intendedTarget.Thing != null;
            if (flag3_poi)
            {
                this.destination = this.intendedTarget.Thing.DrawPos;
            }
            this.Fleck_MakeFleckTick_Poi++; // 使用重命名后的字段
            bool flag4 = this.Fleck_MakeFleckTick_Poi >= this.Fleck_MakeFleckTickMax_Poi; // 使用重命名后的字段
            bool flag5 = flag4 && this.tickcount >= 8;
            if (flag5)
            {
                this.Fleck_MakeFleckTick_Poi = 0;
                Map map = base.Map;
                int randomInRange = this.Fleck_MakeFleckNum_Poi.RandomInRange;
                Vector3 vector = this.BPos(base.DistanceCoveredFraction - 0.01f);
                Vector3 vector2 = this.BPos(base.DistanceCoveredFraction - 0.02f);
                for (int i = 0; i < randomInRange; i++)
                {
                    float num = (vector - this.intendedTarget.CenterVector3).AngleFlat();
                    float velocityAngle = this.Fleck_Angle_Poi.RandomInRange + num;
                    float randomInRange2 = this.Fleck_Scale_Poi.RandomInRange;
                    float randomInRange3 = this.Fleck_Speed_Poi.RandomInRange;
                    float randomInRange4 = this.Fleck_Speed2_Poi.RandomInRange;
                    FleckCreationData dataStatic = FleckMaker.GetDataStatic(vector, map, this.FleckDef_Poi, randomInRange2);
                    FleckCreationData dataStatic2 = FleckMaker.GetDataStatic(vector2, map, this.FleckDef2_Poi, randomInRange2);
                    dataStatic.rotation = (vector - vector2).AngleFlat();
                    dataStatic.rotationRate = this.Fleck_Rotation_Poi.RandomInRange;
                    dataStatic.velocityAngle = velocityAngle;
                    dataStatic.velocitySpeed = randomInRange3;
                    dataStatic2.rotation = (vector - vector2).AngleFlat();
                    dataStatic2.rotationRate = this.Fleck_Rotation_Poi.RandomInRange;
                    dataStatic2.velocityAngle = velocityAngle;
                    dataStatic2.velocitySpeed = randomInRange4;
                    map.flecks.CreateFleck(dataStatic2);
                    map.flecks.CreateFleck(dataStatic);
                }
            }
            // 移除原始的 base.Tick(); 因为 Projectile_Homing 的 Tick 已经包含了其父类的逻辑
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

            // Projectile_Homing_Explosive 的 Impact 逻辑
            if (HomingDef.isExplosive)
            {
                bool flag = blockedByShield || HomingDef.explosionDelay == 0;
                if (flag)
                {
                    Explode();
                }
                else
                {
                    landed = true;
                    ticksToDetonation = HomingDef.explosionDelay;
                    GenExplosion.NotifyNearbyPawnsOfDangerousExplosive(this, def.projectile.damageDef, launcher.Faction, launcher);
                }
            }
            else // Projectile_Homing 的 Impact 逻辑
            {
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

            // Projectile_PoiBullet 原始逻辑中的 Impact
            bool flag_poi = this.intendedTarget.Thing is Pawn;
            if (flag_poi)
            {
                hitThing = this.intendedTarget.Thing;
            }
            // 原始的 base.Impact(hitThing, blockedByShield); 已经被上面的 Homing 和 Explosive 逻辑覆盖，需要确保正确调用或移除
            // 这里我们已经调用了 base.Impact(hitThing, blockedByShield); 在 Projectile_Homing 的 Impact 逻辑中，所以这里不再重复调用。

            BattleLogEntry_RangedImpact battleLogEntry_RangedImpact = new BattleLogEntry_RangedImpact(this.launcher, hitThing, this.intendedTarget.Thing, this.equipmentDef, this.def, this.targetCoverDef);
            Find.BattleLog.Add(battleLogEntry_RangedImpact);
            this.NotifyImpact_Poi(hitThing, map, position); // 使用重命名后的方法
            bool flag2 = hitThing != null && !blockedByShield;
            if (flag2)
            {
                Pawn pawn;
                bool instigatorGuilty = (pawn = (this.launcher as Pawn)) == null || !pawn.Drafted;
                DamageInfo dinfo = new DamageInfo(this.def.projectile.damageDef, (float)this.DamageAmount, this.ArmorPenetration, this.ExactRotation.eulerAngles.y, this.launcher, null, this.equipmentDef, DamageInfo.SourceCategory.ThingOrUnknown, this.intendedTarget.Thing, instigatorGuilty, true, QualityCategory.Normal, true);
                hitThing.TakeDamage(dinfo).AssociateWithLog(battleLogEntry_RangedImpact);
                Pawn pawn2 = hitThing as Pawn;
                bool flag3 = pawn2 != null && pawn2.stances != null;
                if (flag3)
                {
                    pawn2.stances.stagger.Notify_BulletImpact(this);
                }
                bool flag4 = this.def.projectile.extraDamages != null;
                if (flag4)
                {
                    foreach (ExtraDamage extraDamage in this.def.projectile.extraDamages)
                    {
                        bool flag5 = Rand.Chance(extraDamage.chance);
                        if (flag5)
                        {
                            DamageInfo dinfo2 = new DamageInfo(extraDamage.def, extraDamage.amount, extraDamage.AdjustedArmorPenetration(), this.ExactRotation.eulerAngles.y, this.launcher, null, this.equipmentDef, DamageInfo.SourceCategory.ThingOrUnknown, this.intendedTarget.Thing, instigatorGuilty, true, QualityCategory.Normal, true);
                            hitThing.TakeDamage(dinfo2).AssociateWithLog(battleLogEntry_RangedImpact);
                        }
                    }
                }
                bool flag6 = Rand.Chance(this.def.projectile.bulletChanceToStartFire) && (pawn2 == null || Rand.Chance(FireUtility.ChanceToAttachFireFromEvent(pawn2)));
                if (flag6)
                {
                    hitThing.TryAttachFire(this.def.projectile.bulletFireSizeRange.RandomInRange, this);
                }
            }
            else
            {
                bool flag7 = !blockedByShield;
                if (flag7)
                {
                    SoundDefOf.BulletImpact_Ground.PlayOneShot(new TargetInfo(base.Position, map, false));
                    bool takeSplashes = base.Position.GetTerrain(map).takeSplashes;
                    if (takeSplashes)
                    {
                        FleckMaker.WaterSplash(this.ExactPosition, map, Mathf.Sqrt((float)this.DamageAmount) * 1f, 4f);
                    }
                    else
                    {
                        FleckMaker.Static(this.ExactPosition, map, FleckDefOf.ShotHit_Dirt, 1f);
                    }
                }
                bool flag8 = Rand.Chance(this.def.projectile.bulletChanceToStartFire);
                if (flag8)
                {
                    FireUtility.TryStartFireIn(base.Position, map, this.def.projectile.bulletFireSizeRange.RandomInRange, this, null);
                }
            }
        }

        protected virtual void Explode()
        {
            Map map = base.Map;
            ModExtension_Cone modExtension = this.def.GetModExtension<ModExtension_Cone>();
            DoExplosion();
            if (modExtension != null)
            {
                ProjectileProperties projectile = this.def.projectile;
                ModExtension_Cone modExtension_Cone = modExtension;
                IntVec3 position = base.Position;
                Map map2 = map;
                Quaternion exactRotation = this.ExactRotation;
                DamageDef damageDef = projectile.damageDef;
                Thing launcher = base.Launcher;
                int damageAmount = this.DamageAmount;
                float armorPenetration = this.ArmorPenetration;
                SoundDef soundExplode = this.def.projectile.soundExplode;
                ThingDef equipmentDef = this.equipmentDef;
                ThingDef def = this.def;
                Thing thing = this.intendedTarget.Thing;
                ThingDef postExplosionSpawnThingDef = null;
                float postExplosionSpawnChance = 0f;
                int postExplosionSpawnThingCount = 1;
                float screenShakeFactor = this.def.projectile.screenShakeFactor;
                modExtension_Cone.DoConeExplosion(position, map2, exactRotation, damageDef, launcher, damageAmount, armorPenetration, soundExplode, equipmentDef, def, thing, postExplosionSpawnThingDef, postExplosionSpawnChance, postExplosionSpawnThingCount, null, null, 255, false, null, 0f, 1, 0f, false, null, null, 1f, 0f, null, screenShakeFactor, null, null);
            }
            if (this.def.projectile.explosionEffect != null)
            {
                Effecter effecter = this.def.projectile.explosionEffect.Spawn();
                if (this.def.projectile.explosionEffectLifetimeTicks != 0)
                {
                    map.effecterMaintainer.AddEffecterToMaintain(effecter, base.Position.ToVector3().ToIntVec3(), this.def.projectile.explosionEffectLifetimeTicks);
                }
                else
                {
                    effecter.Trigger(new TargetInfo(base.Position, map, false), new TargetInfo(base.Position, map, false), -1);
                    effecter.Cleanup();
                }
            }
            Destroy(DestroyMode.Vanish);
        }

        protected void DoExplosion()
        {
            IntVec3 position = base.Position;
            float explosionRadius = this.def.projectile.explosionRadius;
            DamageDef damageDef = this.def.projectile.damageDef;
            Thing launcher = this.launcher;
            int damageAmount = this.DamageAmount;
            float armorPenetration = this.ArmorPenetration;
            SoundDef soundExplode = this.def.projectile.soundExplode;
            ThingDef equipmentDef = this.equipmentDef;
            ThingDef def = this.def;
            Thing thing = this.intendedTarget.Thing;
            ThingDef thingDef = this.def.projectile.postExplosionSpawnThingDef ?? this.def.projectile.filth;
            ThingDef postExplosionSpawnThingDefWater = this.def.projectile.postExplosionSpawnThingDefWater;
            float postExplosionSpawnChance = this.def.projectile.postExplosionSpawnChance;
            int postExplosionSpawnThingCount = this.def.projectile.postExplosionSpawnThingCount;
            GasType? postExplosionGasType = this.def.projectile.postExplosionGasType;
            ThingDef preExplosionSpawnThingDef = this.def.projectile.preExplosionSpawnThingDef;
            float preExplosionSpawnChance = this.def.projectile.preExplosionSpawnChance;
            int preExplosionSpawnThingCount = this.def.projectile.preExplosionSpawnThingCount;
            bool applyDamageToExplosionCellsNeighbors = this.def.projectile.applyDamageToExplosionCellsNeighbors;
            ThingDef preExplosionSpawnThingDef2 = preExplosionSpawnThingDef;
            float preExplosionSpawnChance2 = preExplosionSpawnChance;
            int preExplosionSpawnThingCount2 = preExplosionSpawnThingCount;
            float explosionChanceToStartFire = this.def.projectile.explosionChanceToStartFire;
            bool explosionDamageFalloff = this.def.projectile.explosionDamageFalloff;
            float? direction = new float?(this.origin.AngleToFlat(this.destination));
            FloatRange? affectedAngle = null;
            float expolosionPropagationSpeed = this.def.projectile.damageDef.expolosionPropagationSpeed;
            float screenShakeFactor = this.def.projectile.screenShakeFactor;
            IntVec3 center = position;
            Map map = base.Map;
            float radius = explosionRadius;
            DamageDef damType = damageDef;
            Thing instigator = launcher;
            int damAmount = damageAmount;
            float armorPenetration2 = armorPenetration;
            SoundDef explosionSound = soundExplode;
            ThingDef weapon = equipmentDef;
            ThingDef projectile = def;
            Thing intendedTarget = thing;
            ThingDef postExplosionSpawnThingDef = thingDef;
            float postExplosionSpawnChance2 = postExplosionSpawnChance;
            int postExplosionSpawnThingCount2 = postExplosionSpawnThingCount;
            GasType? postExplosionGasType2 = postExplosionGasType;
            bool doExplosionVFX = this.def.projectile.doExplosionVFX;
            ThingDef postExplosionSpawnThingDefWater2 = postExplosionSpawnThingDefWater;
            GenExplosion.DoExplosion(center, map, radius, damType, instigator, damAmount, armorPenetration2, explosionSound, weapon, projectile, intendedTarget, postExplosionSpawnThingDef, postExplosionSpawnChance2, postExplosionSpawnThingCount2, postExplosionGasType2, null, 255, applyDamageToExplosionCellsNeighbors, preExplosionSpawnThingDef2, preExplosionSpawnChance2, preExplosionSpawnThingCount2, explosionChanceToStartFire, explosionDamageFalloff, direction, null, affectedAngle, doExplosionVFX, expolosionPropagationSpeed, 0f, true, postExplosionSpawnThingDefWater2, screenShakeFactor, null, null, null, null);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref exactPositionInt, "exactPosition");
            Scribe_Values.Look(ref curSpeed, "curSpeed");
            Scribe_Values.Look(ref homing, "homing", defaultValue: false);
            Scribe_Values.Look(ref ticksToDetonation, "ticksToDetonation", 0, false); // 爆炸弹字段
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

        // Projectile_PoiBullet 原始逻辑
        private void RandFactor()
        {
            FloatRange floatRange = new FloatRange(-0.5f, 0.5f);
            FloatRange floatRange2 = new FloatRange(-0.5f, 0.5f);
            this.Randdd.x = floatRange.RandomInRange;
            this.Randdd.z = floatRange2.RandomInRange;
            this.flag2 = true;
        }

        public Vector3 BPos(float t)
        {
            bool flag = !this.flag2;
            if (flag)
            {
                this.RandFactor();
            }
            Vector3 origin = this.origin;
            Vector3 a = (this.origin + this.destination) / 2f;
            a += this.Randdd;
            a.y = this.destination.y;
            Vector3 destination = this.destination;
            return (1f - t) * (1f - t) * origin + 2f * t * (1f - t) * a + t * t * destination;
        }

        private void FindRandCell(Vector3 d)
        {
            IntVec3 center = IntVec3.FromVector3(d);
            this.intendedTarget = CellRect.CenteredOn(center, 2).RandomCell;
        }

        protected override void DrawAt(Vector3 position, bool flip = false)
        {
            Vector3 b = this.BPos(base.DistanceCoveredFraction - 0.01f);
            position = this.BPos(base.DistanceCoveredFraction);
            Quaternion rotation = Quaternion.LookRotation(position - b);
            bool flag = this.tickcount >= 4;
            if (flag)
            {
                Vector3 position2 = position;
                position2.y = AltitudeLayer.Projectile.AltitudeFor();
                Graphics.DrawMesh(MeshPool.GridPlane(this.def.graphicData.drawSize), position2, rotation, this.DrawMat, 0);
                base.Comps_PostDraw();
            }
        }

        private bool CanHitTarget_Poi() // 重命名以避免冲突
        {
            bool flag = this.launcher is Pawn;
            bool result;
            if (flag)
            {
                float num = this.Hitchance_Poi(); // 使用重命名后的方法
                bool flag2 = (float)Rand.RangeInclusive(0, 100) <= num * 100f;
                Pawn pawn = this.intendedTarget.Thing as Pawn;
                bool flag3 = pawn != null;
                if (flag3)
                {
                    bool downed = pawn.Downed;
                    if (downed)
                    {
                        flag2 = (Rand.RangeInclusive(0, 100) <= 30);
                    }
                }
                result = flag2;
            }
            else
            {
                result = (Rand.RangeInclusive(0, 100) <= 85);
            }
            return result;
        }

        public float Hitchance_Poi() // 重命名以避免冲突
        {
            Pawn pawn = this.launcher as Pawn;
            bool flag = pawn != null;
            float result;
            if (flag)
            {
                SkillDef named = DefDatabase<SkillDef>.GetNamed("Intellectual", true);
                SkillRecord skill = pawn.skills.GetSkill(named);
                bool flag2 = skill != null;
                if (flag2)
                {
                    int level = skill.GetLevel(true);
                    float num = Mathf.Min(1f, (float)level * 0.05f);
                    result = num;
                }
                else
                {
                    result = 0.5f;
                }
            }
            else
            {
                result = 0.2f;
            }
            return result;
        }

        private void NotifyImpact_Poi(Thing hitThing, Map map, IntVec3 position) // 重命名以避免冲突
        {
            BulletImpactData impactData = new BulletImpactData
            {
                bullet = this,
                hitThing = hitThing,
                impactPosition = position
            };
            bool flag = hitThing != null;
            if (flag)
            {
                hitThing.Notify_BulletImpactNearby(impactData);
            }
            int num = 9;
            for (int i = 0; i < num; i++)
            {
                IntVec3 c = position + GenRadial.RadialPattern[i];
                bool flag2 = c.InBounds(map);
                if (flag2)
                {
                    List<Thing> thingList = c.GetThingList(map);
                    for (int j = 0; j < thingList.Count; j++)
                    {
                        bool flag3 = thingList[j] != hitThing;
                        if (flag3)
                        {
                            thingList[j].Notify_BulletImpactNearby(impactData);
                        }
                    }
                }
            }
        }

        private bool flag2 = false;
        private bool flag3 = true;
        private bool CalHit = false;
        private Vector3 Randdd;
        private int tickcount;

        // Projectile_PoiBullet 原始的 Fleck 字段，重命名以避免冲突
        public FleckDef FleckDef_Poi = DefDatabase<FleckDef>.GetNamed("CMC_SparkFlash_Blue_Small", true);
        public FleckDef FleckDef2_Poi = DefDatabase<FleckDef>.GetNamed("CMC_SparkFlash_Blue_LongLasting_Small", true);
        public int Fleck_MakeFleckTickMax_Poi = 1;
        public IntRange Fleck_MakeFleckNum_Poi = new IntRange(2, 2);
        public FloatRange Fleck_Angle_Poi = new FloatRange(-180f, 180f);
        public FloatRange Fleck_Scale_Poi = new FloatRange(1.6f, 1.7f);
        public FloatRange Fleck_Speed_Poi = new FloatRange(5f, 7f);
        public FloatRange Fleck_Speed2_Poi = new FloatRange(0.1f, 0.2f);
        public FloatRange Fleck_Rotation_Poi = new FloatRange(-180f, 180f);
        public int Fleck_MakeFleckTick_Poi;
    }
}