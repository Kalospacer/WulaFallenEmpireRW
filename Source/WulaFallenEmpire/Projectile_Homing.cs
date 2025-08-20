using System;
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
		public HomingProjectileDef HomingDef
		{
			get
			{
				bool flag = this.homingDefInt == null;
				if (flag)
				{
					this.homingDefInt = this.def.GetModExtension<HomingProjectileDef>();
				}
				return this.homingDefInt;
			}
		}

		public override void Launch(Thing launcher, Vector3 origin, LocalTargetInfo usedTarget, LocalTargetInfo intendedTarget, ProjectileHitFlags hitFlags, bool preventFriendlyFire = false, Thing equipment = null, ThingDef targetCoverDef = null)
		{
			bool flag = false;
			bool flag2 = usedTarget.HasThing && usedTarget.Thing is IAttackTarget;
			if (flag2)
			{
				bool flag3 = Rand.Chance(this.GetHitChance(usedTarget.Thing));
				if (flag3)
				{
					hitFlags |= ProjectileHitFlags.IntendedTarget;
					intendedTarget = usedTarget;
					flag = true;
				}
			}
			else
			{
				bool flag4 = Rand.Chance(this.GetHitChance(intendedTarget.Thing));
				if (flag4)
				{
					hitFlags |= ProjectileHitFlags.IntendedTarget;
					usedTarget = intendedTarget;
					flag = true;
				}
			}
			bool flag5 = flag;
			if (flag5)
			{
				hitFlags &= ~ProjectileHitFlags.IntendedTarget;
			}
			base.Launch(launcher, origin, usedTarget, intendedTarget, hitFlags, preventFriendlyFire, equipment, targetCoverDef);
			this.exactPositionInt = origin.Yto0() + Vector3.up * this.def.Altitude;
			Vector3 normalized = (this.destination - origin).Yto0().normalized;
			float degrees = Rand.Range(-this.HomingDef.initRotateAngle, this.HomingDef.initRotateAngle);
			Vector2 vector = new Vector2(normalized.x, normalized.z);
			vector = vector.RotatedBy(degrees);
			Vector3 a = new Vector3(vector.x, 0f, vector.y);
			bool flag6 = this.HomingDef.speedRangeOverride == null;
			if (flag6)
			{
				this.curSpeed = a * this.def.projectile.SpeedTilesPerTick;
			}
			else
			{
				this.curSpeed = a * this.HomingDef.SpeedRangeTilesPerTickOverride.RandomInRange;
			}
			this.ticksToImpact = int.MaxValue;
			this.lifetime = int.MaxValue;
			this.ReflectInit();
		}

		protected void ReflectInit()
		{
			bool flag = !this.def.projectile.soundAmbient.NullOrUndefined();
			if (flag)
			{
				this.ambientSustainer = (Sustainer)NonPublicFields.Projectile_AmbientSustainer.GetValue(this);
			}
			this.comps = (List<ThingComp>)NonPublicFields.ThingWithComps_comps.GetValue(this);
		}

		public float GetHitChance(Thing thing)
		{
			float num = this.HomingDef.hitChance;
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

		public override Vector3 ExactPosition
		{
			get
			{
				return this.exactPositionInt;
			}
		}

		public override Quaternion ExactRotation
		{
			get
			{
				return Quaternion.LookRotation(this.curSpeed);
			}
		}

		public virtual void MovementTick()
		{
			Vector3 vect = this.ExactPosition + this.curSpeed;
			ShootLine shootLine = new ShootLine(this.ExactPosition.ToIntVec3(), vect.ToIntVec3());
			Vector3 vector = (this.intendedTarget.Cell.ToVector3() - this.ExactPosition).Yto0();
			bool flag = this.homing;
			if (flag)
			{
				Vector3 a = vector.normalized - this.curSpeed.normalized;
				bool flag2 = a.sqrMagnitude >= 1.414f;
				if (flag2)
				{
					this.homing = false;
					this.lifetime = this.HomingDef.destroyTicksAfterLosingTrack.RandomInRange;
					this.ticksToImpact = this.lifetime;
					base.HitFlags &= ~ProjectileHitFlags.IntendedTarget;
					base.HitFlags |= ProjectileHitFlags.NonTargetPawns;
					base.HitFlags |= ProjectileHitFlags.NonTargetWorld;
				}
				else
				{
					this.curSpeed += a * this.HomingDef.homingSpeed * this.curSpeed.magnitude;
				}
			}
			foreach (IntVec3 b in shootLine.Points())
			{
				bool flag3 = (this.intendedTarget.Cell - b).SqrMagnitude <= this.HomingDef.proximityFuseRange * this.HomingDef.proximityFuseRange;
				if (flag3)
				{
					this.homing = false;
					this.lifetime = this.HomingDef.destroyTicksAfterLosingTrack.RandomInRange;
					bool flag4 = (base.HitFlags & ProjectileHitFlags.IntendedTarget) == ProjectileHitFlags.IntendedTarget || this.HomingDef.proximityFuseRange > 0f;
					if (flag4)
					{
						this.lifetime = 0;
						this.ticksToImpact = 0;
						vect = b.ToVector3();
						bool flag5 = Find.TickManager.CurTimeSpeed == TimeSpeed.Normal && this.def.projectile.soundImpactAnticipate != null;
						if (flag5)
						{
							this.def.projectile.soundImpactAnticipate.PlayOneShot(this);
						}
					}
				}
			}
			this.exactPositionInt = vect;
			this.curSpeed *= (this.curSpeed.magnitude + this.HomingDef.SpeedChangeTilesPerTickOverride) / this.curSpeed.magnitude;
		}

		protected override void Tick()
		{
			this.ThingWithCompsTick();
			this.lifetime--;
			if (this.HomingDef.tailFleckDef != null)
			{
				FleckMaker.Static(this.ExactPosition, base.Map, this.HomingDef.tailFleckDef, 1f);
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
					bool flag2 = (bool)Projectile_Homing.ProjectileCheckForFreeInterceptBetween.Invoke(this, parameters);
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
							this.ImpactSomething();
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

		private void ThingWithCompsTick()
		{
			bool flag = this.comps != null;
			if (flag)
			{
				int i = 0;
				int count = this.comps.Count;
				while (i < count)
				{
					this.comps[i].CompTick();
					i++;
				}
			}
		}

		protected override void Impact(Thing hitThing, bool blockedByShield = false)
		{
			Map map = base.Map;
			IntVec3 position = base.Position;
			base.Impact(hitThing, blockedByShield);
			bool flag = this.HomingDef.extraProjectile != null;
			if (flag)
			{
				bool flag2 = hitThing != null && hitThing.Spawned;
				if (flag2)
				{
					((Projectile)GenSpawn.Spawn(this.HomingDef.extraProjectile, base.Position, map, WipeMode.Vanish)).Launch(this.launcher, this.ExactPosition, hitThing, hitThing, ProjectileHitFlags.All, false, null, null);
				}
				else
				{
					((Projectile)GenSpawn.Spawn(this.HomingDef.extraProjectile, base.Position, map, WipeMode.Vanish)).Launch(this.launcher, this.ExactPosition, position, position, ProjectileHitFlags.All, false, null, null);
				}
			}
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look<Vector3>(ref this.exactPositionInt, "exactPosition", default(Vector3), false);
			Scribe_Values.Look<Vector3>(ref this.curSpeed, "curSpeed", default(Vector3), false);
			Scribe_Values.Look<bool>(ref this.homing, "homing", false, false);
			bool flag = Scribe.mode == LoadSaveMode.PostLoadInit;
			if (flag)
			{
				this.ReflectInit();
			}
		}

		private HomingProjectileDef homingDefInt;

		private Sustainer ambientSustainer;

		private List<ThingComp> comps;

		protected Vector3 exactPositionInt;

		public Vector3 curSpeed;

		public bool homing = true;

		private static MethodInfo ProjectileCheckForFreeInterceptBetween = typeof(Projectile).GetMethod("CheckForFreeInterceptBetween", BindingFlags.Instance | BindingFlags.NonPublic);
	}
}