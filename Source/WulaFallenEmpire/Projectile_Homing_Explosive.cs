using System;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
	public class Projectile_Homing_Explosive : Projectile_Homing
	{
		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look<int>(ref this.ticksToDetonation, "ticksToDetonation", 0, false);
		}

		protected override void Tick()
		{
			base.Tick();
			bool flag = this.ticksToDetonation > 0;
			if (flag)
			{
				this.ticksToDetonation--;
				bool flag2 = this.ticksToDetonation <= 0;
				if (flag2)
				{
					this.Explode();
				}
			}
		}

		protected override void Impact(Thing hitThing, bool blockedByShield = false)
		{
			bool flag = blockedByShield || this.def.projectile.explosionDelay == 0;
			if (flag)
			{
				this.Explode();
			}
			else
			{
				this.landed = true;
				this.ticksToDetonation = this.def.projectile.explosionDelay;
				GenExplosion.NotifyNearbyPawnsOfDangerousExplosive(this, this.def.projectile.damageDef, this.launcher.Faction, this.launcher);
			}
		}

		protected virtual void Explode()
		{
			Map map = base.Map;
			ModExtension_Cone modExtension = this.def.GetModExtension<ModExtension_Cone>();
			this.DoExplosion();
			bool flag = modExtension != null;
			if (flag)
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
			bool flag2 = this.def.projectile.explosionEffect != null;
			if (flag2)
			{
				Effecter effecter = this.def.projectile.explosionEffect.Spawn();
				bool flag3 = this.def.projectile.explosionEffectLifetimeTicks != 0;
				if (flag3)
				{
					map.effecterMaintainer.AddEffecterToMaintain(effecter, base.Position.ToVector3().ToIntVec3(), this.def.projectile.explosionEffectLifetimeTicks);
				}
				else
				{
					effecter.Trigger(new TargetInfo(base.Position, map, false), new TargetInfo(base.Position, map, false), -1);
					effecter.Cleanup();
				}
			}
			this.Destroy(DestroyMode.Vanish);
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

		private int ticksToDetonation;
	}
}