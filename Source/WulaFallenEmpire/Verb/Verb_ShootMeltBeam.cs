using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using WulaFallenEmpire;

namespace WulaFallenEmpire
{
	// 我们让它继承自我们自己的 Verb_ShootBeamExplosive 以便复用爆炸逻辑
	public class Verb_ShootMeltBeam : Verb
	{
		// --- 从 Verb_ShootBeamExplosive 复制过来的字段 ---
		private int explosionShotCounter = 0;
		private int mirroredExplosionShotCounter = 0;
		// ---------------------------------------------

		protected override int ShotsPerBurst
		{
			get
			{
				return this.verbProps.burstShotCount;
			}
		}

		public float ShotProgress
		{
			get
			{
				return (float)this.ticksToNextPathStep / (float)this.verbProps.ticksBetweenBurstShots;
			}
		}

		public Vector3 InterpolatedPosition
		{
			get
			{
				Vector3 b = base.CurrentTarget.CenterVector3 - this.initialTargetPosition;
				return Vector3.Lerp(this.path[this.burstShotsLeft], this.path[Mathf.Min(this.burstShotsLeft + 1, this.path.Count - 1)], this.ShotProgress) + b;
			}
		}
        
        // 为镜像光束添加一个计算位置的属性
        public Vector3 MirroredInterpolatedPosition
		{
			get
			{
				Vector3 b = base.CurrentTarget.CenterVector3 - this.initialTargetPosition;
				return Vector3.Lerp(this.mirroredPath[this.burstShotsLeft], this.mirroredPath[Mathf.Min(this.burstShotsLeft + 1, this.mirroredPath.Count - 1)], this.ShotProgress) + b;
			}
		}

		public override float? AimAngleOverride
		{
			get
			{
				return (this.state != VerbState.Bursting) ? null : new float?((this.InterpolatedPosition - this.caster.DrawPos).AngleFlat());
			}
		}

		public override void DrawHighlight(LocalTargetInfo target)
		{
				base.DrawHighlight(target);
				this.CalculatePath(target.CenterVector3, this.tmpPath, this.tmpPathCells, false);
				foreach (IntVec3 tmpPathCell in this.tmpPathCells)
				{
					ShootLine resultingLine;
					bool flag = this.TryFindShootLineFromTo(this.caster.Position, target, out resultingLine);
					if ((this.verbProps.stopBurstWithoutLos && !flag) || !this.TryGetHitCell(resultingLine.Source, tmpPathCell, out var hitCell))
					{
						continue;
					}
					this.tmpHighlightCells.Add(hitCell);
					if (!this.verbProps.beamHitsNeighborCells)
					{
						continue;
					}
					foreach (IntVec3 beamHitNeighbourCell in this.GetBeamHitNeighbourCells(resultingLine.Source, hitCell))
					{
						if (!this.tmpHighlightCells.Contains(beamHitNeighbourCell))
						{
							this.tmpSecondaryHighlightCells.Add(beamHitNeighbourCell);
						}
					}
				}
				this.tmpSecondaryHighlightCells.RemoveWhere((IntVec3 x) => this.tmpHighlightCells.Contains(x));
				if (this.tmpHighlightCells.Any())
				{
					GenDraw.DrawFieldEdges(this.tmpHighlightCells.ToList(), this.verbProps.highlightColor ?? Color.white);
				}
				if (this.tmpSecondaryHighlightCells.Any())
				{
					GenDraw.DrawFieldEdges(this.tmpSecondaryHighlightCells.ToList(), this.verbProps.secondaryHighlightColor ?? Color.white);
				}
				this.tmpHighlightCells.Clear();
				this.tmpSecondaryHighlightCells.Clear();
		}

		protected override bool TryCastShot()
		{
			bool flag = this.currentTarget.HasThing && this.currentTarget.Thing.Map != this.caster.Map;
			bool result;
			if (flag)
			{
				result = false;
			}
			else
			{
				ShootLine shootLine;
				bool flag2 = base.TryFindShootLineFromTo(this.caster.Position, this.currentTarget, out shootLine, false);
				bool flag3 = this.verbProps.stopBurstWithoutLos && !flag2;
				if (flag3)
				{
					result = false;
				}
				else
				{
					bool flag4 = base.EquipmentSource != null;
					if (flag4)
					{
						CompChangeableProjectile comp = base.EquipmentSource.GetComp<CompChangeableProjectile>();
						if (comp != null)
						{
							comp.Notify_ProjectileLaunched();
						}
						CompApparelReloadable comp2 = base.EquipmentSource.GetComp<CompApparelReloadable>();
						if (comp2 != null)
						{
							comp2.UsedOnce();
						}
					}
					this.lastShotTick = Find.TickManager.TicksGame;
					this.ticksToNextPathStep = this.verbProps.ticksBetweenBurstShots;
					IntVec3 targetCell = this.InterpolatedPosition.Yto0().ToIntVec3();
					IntVec3 intVec;
					bool flag5 = !this.TryGetHitCell(shootLine.Source, targetCell, out intVec);
					if (flag5)
					{
						result = true;
					}
					else
					{
						this.HitCell(intVec, shootLine.Source, 1f);
						bool beamHitsNeighborCells = this.verbProps.beamHitsNeighborCells;
						if (beamHitsNeighborCells)
						{
							this.hitCells.Add(intVec);
							foreach (IntVec3 intVec2 in this.GetBeamHitNeighbourCells(shootLine.Source, intVec))
							{
								bool flag6 = !this.hitCells.Contains(intVec2);
								if (flag6)
								{
									float damageFactor = this.pathCells.Contains(intVec2) ? 1f : 0.5f;
									this.HitCell(intVec2, shootLine.Source, damageFactor);
									this.hitCells.Add(intVec2);
								}
							}
						}
						IntVec3 targetCell2 = this.mirroredPath[Mathf.Min(this.burstShotsLeft, this.mirroredPath.Count - 1)].ToIntVec3();
						IntVec3 intVec3;
						bool flag7 = this.TryGetHitCell(shootLine.Source, targetCell2, out intVec3);
						if (flag7)
						{
							this.HitCell(intVec3, shootLine.Source, 1f);
							this.mirroredHitCells.Add(intVec3);
							bool beamHitsNeighborCells2 = this.verbProps.beamHitsNeighborCells;
							if (beamHitsNeighborCells2)
							{
								foreach (IntVec3 intVec4 in this.GetBeamHitNeighbourCells(shootLine.Source, intVec3))
								{
									bool flag8 = !this.mirroredHitCells.Contains(intVec4);
									if (flag8)
									{
										float damageFactor2 = this.mirroredPathCells.Contains(intVec4) ? 1f : 0.5f;
										this.HitCell(intVec4, shootLine.Source, damageFactor2);
										this.mirroredHitCells.Add(intVec4);
									}
								}
							}
						}

                        // --- 添加爆炸逻辑 ---
                        if (verbProps is VerbPropertiesExplosiveBeam explosiveProps && explosiveProps.enableExplosion)
                        {
                            explosionShotCounter++;
                            mirroredExplosionShotCounter++;

                            if (explosionShotCounter >= explosiveProps.explosionShotInterval)
                            {
                                explosionShotCounter = 0;
                                TriggerExplosion(explosiveProps, InterpolatedPosition);
                            }
                            if (mirroredExplosionShotCounter >= explosiveProps.explosionShotInterval)
                            {
                                mirroredExplosionShotCounter = 0;
                                TriggerExplosion(explosiveProps, MirroredInterpolatedPosition);
                            }
                        }
                        // ---------------------

						result = true;
					}
				}
			}
			return result;
		}

		// --- 从 Verb_ShootBeamExplosive 复制过来的方法 ---
        private void TriggerExplosion(VerbPropertiesExplosiveBeam explosiveProps, Vector3 position)
        {
            IntVec3 explosionCell = position.ToIntVec3();
            
            if (!explosionCell.InBounds(caster.Map))
                return;

            // 播放爆炸音效
            if (explosiveProps.explosionSound != null)
            {
                explosiveProps.explosionSound.PlayOneShot(new TargetInfo(explosionCell, caster.Map));
            }

            // 生成爆炸
            GenExplosion.DoExplosion(
                center: explosionCell,
                map: caster.Map,
                radius: explosiveProps.explosionRadius,
                damType: explosiveProps.explosionDamageDef ?? DamageDefOf.Bomb,
                instigator: caster,
                damAmount: explosiveProps.explosionDamage > 0 ? explosiveProps.explosionDamage : verbProps.defaultProjectile?.projectile?.GetDamageAmount(EquipmentSource) ?? 20,
                armorPenetration: explosiveProps.explosionArmorPenetration >= 0 ? explosiveProps.explosionArmorPenetration : verbProps.defaultProjectile?.projectile?.GetArmorPenetration(EquipmentSource) ?? 0.3f,
                explosionSound: null, // 我们已经手动播放了音效
                weapon: base.EquipmentSource?.def,
                projectile: null,
                intendedTarget: currentTarget.Thing,
                postExplosionSpawnThingDef: explosiveProps.postExplosionSpawnThingDef,
                postExplosionSpawnChance: explosiveProps.postExplosionSpawnChance,
                postExplosionSpawnThingCount: explosiveProps.postExplosionSpawnThingCount,
                postExplosionGasType: explosiveProps.postExplosionGasType,
                applyDamageToExplosionCellsNeighbors: explosiveProps.applyDamageToExplosionCellsNeighbors,
                preExplosionSpawnThingDef: explosiveProps.preExplosionSpawnThingDef,
                preExplosionSpawnChance: explosiveProps.preExplosionSpawnChance,
                preExplosionSpawnThingCount: explosiveProps.preExplosionSpawnThingCount,
                chanceToStartFire: explosiveProps.chanceToStartFire,
                damageFalloff: explosiveProps.damageFalloff,
                direction: null,
                ignoredThings: null,
                affectedAngle: null,
                doVisualEffects: true,
                propagationSpeed: 0.6f,
                excludeRadius: 0f,
                doSoundEffects: false, // 我们手动处理音效
                screenShakeFactor: explosiveProps.screenShakeFactor // 新增：屏幕震动因子
            );

            // 生成额外的视觉效果
            if (explosiveProps.explosionEffecter != null)
            {
                Effecter effecter = explosiveProps.explosionEffecter.Spawn(explosionCell, caster.Map);
                effecter.Trigger(new TargetInfo(explosionCell, caster.Map), TargetInfo.Invalid);
                effecter.Cleanup();
            }
        }
        // ---------------------------------------------


		protected bool TryGetHitCell(IntVec3 source, IntVec3 targetCell, out IntVec3 hitCell)
		{
			IntVec3 intVec = GenSight.LastPointOnLineOfSight(source, targetCell, (IntVec3 c) => c.InBounds(this.caster.Map) && c.CanBeSeenOverFast(this.caster.Map), true);
			bool flag = this.verbProps.beamCantHitWithinMinRange && (double)intVec.DistanceTo(source) < (double)this.verbProps.minRange;
			bool result;
			if (flag)
			{
				hitCell = default(IntVec3);
				result = false;
			}
			else
			{
				hitCell = (intVec.IsValid ? intVec : targetCell);
				result = intVec.IsValid;
			}
			return result;
		}

		protected IntVec3 GetHitCell(IntVec3 source, IntVec3 targetCell)
		{
			IntVec3 result;
			this.TryGetHitCell(source, targetCell, out result);
			return result;
		}

		protected IEnumerable<IntVec3> GetBeamHitNeighbourCells(IntVec3 source, IntVec3 pos)
		{
			// 重写反编译的迭代器方法以修复编译错误
			for (int i = 0; i < GenAdj.AdjacentCells.Length; i++)
			{
				IntVec3 cell = pos + GenAdj.AdjacentCells[i];
				if (cell.InBounds(this.caster.Map))
				{
					yield return cell;
				}
			}
		}

		public override bool TryStartCastOn(LocalTargetInfo castTarg, LocalTargetInfo destTarg, bool surpriseAttack = false, bool canHitNonTargetPawns = true, bool preventFriendlyFire = false, bool nonInterruptingSelfCast = false)
		{
			return base.TryStartCastOn(this.verbProps.beamTargetsGround ? castTarg.Cell : castTarg, destTarg, surpriseAttack, canHitNonTargetPawns, preventFriendlyFire, nonInterruptingSelfCast);
		}

		private void UpdateBeamVisuals(List<Vector3> path, MoteDualAttached mote, ref Effecter endEffecter, Vector3 casterPos, IntVec3 casterCell, bool isMirrored = false)
		{
			Vector3 vector = path[Mathf.Min(this.burstShotsLeft, path.Count - 1)];
			Vector3 v = (vector - casterPos).Yto0();
			float num = v.MagnitudeHorizontal();
			Vector3 normalized = v.normalized;
			IntVec3 intVec = vector.ToIntVec3();
			IntVec3 b = GenSight.LastPointOnLineOfSight(casterCell, intVec, (IntVec3 c) => c.CanBeSeenOverFast(this.caster.Map), true);
			bool isValid = b.IsValid;
			if (isValid)
			{
				num -= (intVec - b).LengthHorizontal;
				vector = casterCell.ToVector3Shifted() + normalized * num;
				intVec = vector.ToIntVec3();
			}
			Vector3 offsetA = normalized * this.verbProps.beamStartOffset;
			Vector3 vector2 = vector - intVec.ToVector3Shifted();
			if (mote != null)
			{
				mote.UpdateTargets(new TargetInfo(casterCell, this.caster.Map, false), new TargetInfo(intVec, this.caster.Map, false), offsetA, vector2);
			}
			if (mote != null)
			{
				mote.Maintain();
			}
			bool flag = this.verbProps.beamGroundFleckDef != null && Rand.Chance(this.verbProps.beamFleckChancePerTick);
			if (flag)
			{
				FleckMaker.Static(vector, this.caster.Map, this.verbProps.beamGroundFleckDef, 1f);
			}
			bool flag2 = endEffecter == null && this.verbProps.beamEndEffecterDef != null;
			if (flag2)
			{
				endEffecter = this.verbProps.beamEndEffecterDef.Spawn(intVec, this.caster.Map, vector2, 1f);
			}
			bool flag3 = endEffecter != null;
			if (flag3)
			{
				endEffecter.offset = vector2;
				endEffecter.EffectTick(new TargetInfo(intVec, this.caster.Map, false), TargetInfo.Invalid);
				endEffecter.ticksLeft--;
			}
			bool flag4 = this.verbProps.beamLineFleckDef != null;
			if (flag4)
			{
				float num2 = num;
				int num3 = 0;
				while ((float)num3 < num2)
				{
					bool flag5 = Rand.Chance(this.verbProps.beamLineFleckChanceCurve.Evaluate((float)num3 / num2));
					if (flag5)
					{
						Vector3 loc = casterPos + (float)num3 * normalized - normalized * Rand.Value + normalized / 2f;
						FleckMaker.Static(loc, this.caster.Map, this.verbProps.beamLineFleckDef, 1f);
					}
					num3++;
				}
			}
		}

		public override void BurstingTick()
		{
			this.ticksToNextPathStep--;
			this.UpdateBeamVisuals(this.path, this.mote, ref this.endEffecter, this.caster.Position.ToVector3Shifted(), this.caster.Position, false);
			this.UpdateBeamVisuals(this.mirroredPath, this.mirroredMote, ref this.mirroredEndEffecter, this.caster.Position.ToVector3Shifted(), this.caster.Position, true);
			Sustainer sustainer = this.sustainer;
			if (sustainer != null)
			{
				sustainer.Maintain();
			}
		}

		public override void WarmupComplete()
		{
			this.burstShotsLeft = this.ShotsPerBurst;
			this.state = VerbState.Bursting;
			this.initialTargetPosition = this.currentTarget.CenterVector3;
			this.CalculatePath(this.currentTarget.CenterVector3, this.path, this.pathCells, true);
			Vector3 normalized = (this.currentTarget.CenterVector3 - this.caster.Position.ToVector3Shifted()).Yto0().normalized;
			float angle = 3f;
			Vector3 a = normalized.RotatedBy(angle);
			float magnitude = (this.currentTarget.CenterVector3 - this.caster.Position.ToVector3Shifted()).magnitude;
			Vector3 target = this.caster.Position.ToVector3Shifted() + a * magnitude;
			this.CalculatePath(target, this.mirroredPath, this.mirroredPathCells, true);
			this.mirroredPath.Reverse();
			this.hitCells.Clear();
			this.mirroredHitCells.Clear();
			bool flag = this.verbProps.beamMoteDef != null;
			if (flag)
			{
				this.mote = MoteMaker.MakeInteractionOverlay(this.verbProps.beamMoteDef, this.caster, new TargetInfo(this.path[0].ToIntVec3(), this.caster.Map, false));
			}
			bool flag2 = this.verbProps.beamMoteDef != null;
			if (flag2)
			{
				this.mirroredMote = MoteMaker.MakeInteractionOverlay(this.verbProps.beamMoteDef, this.caster, new TargetInfo(this.mirroredPath[0].ToIntVec3(), this.caster.Map, false));
			}
			base.TryCastNextBurstShot();
			this.ticksToNextPathStep = this.verbProps.ticksBetweenBurstShots;
			Effecter effecter = this.endEffecter;
			if (effecter != null)
			{
				effecter.Cleanup();
			}
			Effecter effecter2 = this.mirroredEndEffecter;
			if (effecter2 != null)
			{
				effecter2.Cleanup();
			}
			bool flag3 = this.verbProps.soundCastBeam == null;
			if (!flag3)
			{
				this.sustainer = this.verbProps.soundCastBeam.TrySpawnSustainer(SoundInfo.InMap(this.caster, MaintenanceType.PerTick));
			}
		}

		private void CalculatePath(Vector3 target, List<Vector3> pathList, HashSet<IntVec3> pathCellsList, bool addRandomOffset = true)
		{
			pathList.Clear();
			Vector3 vector = (target - this.caster.Position.ToVector3Shifted()).Yto0();
			float magnitude = vector.magnitude;
			Vector3 normalized = vector.normalized;
			Vector3 a = normalized.RotatedBy(-90f);
			float num = ((double)this.verbProps.beamFullWidthRange > 0.0) ? Mathf.Min(magnitude / this.verbProps.beamFullWidthRange, 1f) : 1f;
			float d = (this.verbProps.beamWidth + 1f) * num / (float)this.ShotsPerBurst;
			Vector3 vector2 = target.Yto0() - a * this.verbProps.beamWidth / 2f * num;
			pathList.Add(vector2);
			for (int i = 0; i < this.ShotsPerBurst; i++)
			{
				Vector3 a2 = normalized * (Rand.Value * this.verbProps.beamMaxDeviation) - normalized / 2f;
				Vector3 vector3 = Mathf.Sin((float)(((double)i / (double)this.ShotsPerBurst + 0.5) * 3.1415927410125732 * 57.295780181884766)) * this.verbProps.beamCurvature * -normalized - normalized * this.verbProps.beamMaxDeviation / 2f;
				if (addRandomOffset)
				{
					pathList.Add(vector2 + (a2 + vector3) * num);
				}
				else
				{
					pathList.Add(vector2 + vector3 * num);
				}
				vector2 += a * d;
			}
			pathCellsList.Clear();
			foreach (Vector3 vect in pathList)
			{
				pathCellsList.Add(vect.ToIntVec3());
			}
		}

		private bool CanHit(Thing thing)
		{
			return thing.Spawned && !CoverUtility.ThingCovered(thing, this.caster.Map);
		}

		private void HitCell(IntVec3 cell, IntVec3 sourceCell, float damageFactor = 1f)
		{
			bool flag = !cell.InBounds(this.caster.Map);
			if (!flag)
			{
				this.ApplyDamage(VerbUtility.ThingsToHit(cell, this.caster.Map, new Func<Thing, bool>(this.CanHit)).RandomElementWithFallback(null), sourceCell, damageFactor);
				bool flag2 = !this.verbProps.beamSetsGroundOnFire || !Rand.Chance(this.verbProps.beamChanceToStartFire);
				if (!flag2)
				{
					FireUtility.TryStartFireIn(cell, this.caster.Map, 1f, this.caster, null);
				}
			}
		}

		private void ApplyDamage(Thing thing, IntVec3 sourceCell, float damageFactor = 1f)
		{
			IntVec3 intVec = this.InterpolatedPosition.Yto0().ToIntVec3();
			IntVec3 intVec2 = GenSight.LastPointOnLineOfSight(sourceCell, intVec, (IntVec3 c) => c.InBounds(this.caster.Map) && c.CanBeSeenOverFast(this.caster.Map), true);
			bool isValid = intVec2.IsValid;
			if (isValid)
			{
				intVec = intVec2;
			}
			Map map = this.caster.Map;
			bool flag = thing == null || this.verbProps.beamDamageDef == null;
			if (!flag)
			{
				Pawn pawn = thing as Pawn;
				bool flag2 = pawn != null && pawn.Faction == this.Caster.Faction;
				if (!flag2)
				{
					float angleFlat = (this.currentTarget.Cell - this.caster.Position).AngleFlat;
					BattleLogEntry_RangedImpact log = new BattleLogEntry_RangedImpact(this.caster, thing, this.currentTarget.Thing, base.EquipmentSource.def, null, null);
					DamageInfo dinfo = ((double)this.verbProps.beamTotalDamage <= 0.0) ? new DamageInfo(this.verbProps.beamDamageDef, (float)this.verbProps.beamDamageDef.defaultDamage * damageFactor, this.verbProps.beamDamageDef.defaultArmorPenetration, angleFlat, this.caster, null, base.EquipmentSource.def, DamageInfo.SourceCategory.ThingOrUnknown, this.currentTarget.Thing, true, true, QualityCategory.Normal, true, false) : new DamageInfo(this.verbProps.beamDamageDef, this.verbProps.beamTotalDamage / (float)this.pathCells.Count * damageFactor, this.verbProps.beamDamageDef.defaultArmorPenetration, angleFlat, this.caster, null, base.EquipmentSource.def, DamageInfo.SourceCategory.ThingOrUnknown, this.currentTarget.Thing, true, true, QualityCategory.Normal, true, false);
					thing.TakeDamage(dinfo).AssociateWithLog(log);
					bool flag3 = thing.CanEverAttachFire();
					if (flag3)
					{
						bool flag4 = !Rand.Chance((this.verbProps.flammabilityAttachFireChanceCurve == null) ? this.verbProps.beamChanceToAttachFire : this.verbProps.flammabilityAttachFireChanceCurve.Evaluate(thing.GetStatValue(StatDefOf.Flammability, true, -1)));
						if (flag4)
						{
							return;
						}
						thing.TryAttachFire(this.verbProps.beamFireSizeRange.RandomInRange, this.caster);
					}
					else
					{
						bool flag5 = !Rand.Chance(this.verbProps.beamChanceToStartFire);
						if (flag5)
						{
							return;
						}
						FireUtility.TryStartFireIn(intVec, map, this.verbProps.beamFireSizeRange.RandomInRange, this.caster, this.verbProps.flammabilityAttachFireChanceCurve);
					}
                    // 移除了热射病和蒸发逻辑
				}
			}
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Collections.Look<Vector3>(ref this.path, "path", LookMode.Value, Array.Empty<object>());
			Scribe_Values.Look<int>(ref this.ticksToNextPathStep, "ticksToNextPathStep", 0, false);
			Scribe_Values.Look<Vector3>(ref this.initialTargetPosition, "initialTargetPosition", default(Vector3), false);
			Scribe_Collections.Look<Vector3>(ref this.mirroredPath, "mirroredPath", LookMode.Value, Array.Empty<object>());
            // --- 添加爆炸计数器的保存 ---
            Scribe_Values.Look(ref explosionShotCounter, "explosionShotCounter", 0);
            Scribe_Values.Look(ref mirroredExplosionShotCounter, "mirroredExplosionShotCounter", 0);
            // -------------------------
			bool flag = Scribe.mode == LoadSaveMode.PostLoadInit;
			if (flag)
			{
				bool flag2 = this.path == null;
				if (flag2)
				{
					this.path = new List<Vector3>();
				}
				bool flag3 = this.mirroredPath == null;
				if (flag3)
				{
					this.mirroredPath = new List<Vector3>();
				}
			}
		}

		private List<Vector3> path = new List<Vector3>();

		private List<Vector3> tmpPath = new List<Vector3>();

		private int ticksToNextPathStep;

		private Vector3 initialTargetPosition;

		private MoteDualAttached mote;

		private Effecter endEffecter;


		private Sustainer sustainer;

		private HashSet<IntVec3> pathCells = new HashSet<IntVec3>();

		private HashSet<IntVec3> tmpPathCells = new HashSet<IntVec3>();

		private HashSet<IntVec3> tmpHighlightCells = new HashSet<IntVec3>();

		private HashSet<IntVec3> tmpSecondaryHighlightCells = new HashSet<IntVec3>();

		private HashSet<IntVec3> hitCells = new HashSet<IntVec3>();

		private const int NumSubdivisionsPerUnitLength = 1;

		private List<Vector3> mirroredPath = new List<Vector3>();

		private HashSet<IntVec3> mirroredPathCells = new HashSet<IntVec3>();

		private HashSet<IntVec3> mirroredHitCells = new HashSet<IntVec3>();

		private MoteDualAttached mirroredMote;

		private Effecter mirroredEndEffecter;
	}
}