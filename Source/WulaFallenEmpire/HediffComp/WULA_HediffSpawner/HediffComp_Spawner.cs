using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace WulaFallenEmpire
{
	public class HediffComp_Spawner : HediffComp
	{
		public HediffCompProperties_Spawner Props
		{
			get
			{
				return (HediffCompProperties_Spawner)this.props;
			}
		}

		public override void CompExposeData()
		{
			Scribe_Values.Look<int>(ref this.ticksUntilSpawn, "ticksUntilSpawn", 0, false);
			Scribe_Values.Look<int>(ref this.initialTicksUntilSpawn, "initialTicksUntilSpawn", 0, false);
			Scribe_Values.Look<float>(ref this.calculatedMinDaysB4Next, "calculatedMinDaysB4Next", 0f, false);
			Scribe_Values.Look<float>(ref this.calculatedMaxDaysB4Next, "calculatedMaxDaysB4Next", 0f, false);
			Scribe_Values.Look<int>(ref this.calculatedQuantity, "calculatedQuantity", 0, false);
			Scribe_Values.Look<int>(ref this.graceTicks, "graceTicks", 0, false);
		}

		public override void CompPostMake()
		{
			this.myDebug = this.Props.debug;
			Tools.Warn(string.Concat(new string[]
			{
				">>> ",
				this.parent.pawn.Label,
				" - ",
				this.parent.def.defName,
				" - CompPostMake start"
			}), this.myDebug);
			this.TraceProps();
			this.CheckProps();
			this.CalculateValues();
			this.CheckCalculatedValues();
			this.TraceCalculatedValues();
			if (this.initialTicksUntilSpawn == 0)
			{
				Tools.Warn("Reseting countdown bc initialTicksUntilSpawn == 0 (comppostmake)", this.myDebug);
				this.ResetCountdown();
			}
		}

		public override void CompPostTick(ref float severityAdjustment)
		{
			this.pawn = this.parent.pawn;
			if (!Tools.OkPawn(this.pawn))
			{
				return;
			}
			if (this.blockSpawn)
			{
				return;
			}
			if (this.graceTicks > 0)
			{
				this.graceTicks--;
				return;
			}
			if (this.Props.hungerRelative && this.pawn.IsHungry(this.myDebug))
			{
				int num = (int)(this.RandomGraceDays() * 60000f);
				this.hungerReset++;
				this.graceTicks = num;
				return;
			}
			if (this.Props.healthRelative && this.pawn.IsInjured(this.myDebug))
			{
				int num2 = (int)(this.RandomGraceDays() * 60000f);
				this.healthReset++;
				this.graceTicks = num2;
				return;
			}
			this.hungerReset = (this.healthReset = 0);
			if (this.CheckShouldSpawn())
			{
				Tools.Warn("Reseting countdown bc spawned thing", this.myDebug);
				this.CalculateValues();
				this.CheckCalculatedValues();
				this.ResetCountdown();
				if (Rand.Chance(this.Props.randomGrace))
				{
					int num3 = (int)(this.RandomGraceDays() * 60000f);
					this.graceTicks = num3;
				}
			}
		}

		private void TraceProps()
		{
			Tools.Warn(string.Concat(new string[]
			{
				"Props => minDaysB4Next: ",
				this.Props.minDaysB4Next.ToString(),
				"; maxDaysB4Next: ",
				this.Props.maxDaysB4Next.ToString(),
				"; randomGrace: ",
				this.Props.randomGrace.ToString(),
				"; graceDays: ",
				this.Props.graceDays.ToString(),
				"; hungerRelative: ",
				this.Props.hungerRelative.ToString(),
				"; healthRelative: ",
				this.Props.healthRelative.ToString(),
				"; "
			}), this.myDebug);
			if (this.Props.animalThing)
			{
				Tools.Warn(string.Concat(new string[]
				{
					"animalThing: ",
					this.Props.animalThing.ToString(),
					"; animalName: ",
					this.Props.animalToSpawn.defName,
					"; factionOfPlayerAnimal: ",
					this.Props.factionOfPlayerAnimal.ToString(),
					"; "
				}), this.myDebug);
			}
			if (this.Props.ageWeightedQuantity)
			{
				Tools.Warn(string.Concat(new string[]
				{
					"ageWeightedQuantity:",
					this.Props.ageWeightedQuantity.ToString(),
					"; olderBiggerQuantity:",
					this.Props.olderBiggerQuantity.ToString(),
					"; ",
					this.myDebug.ToString()
				}), false);
				if (this.Props.exponentialQuantity)
				{
					Tools.Warn(string.Concat(new string[]
					{
						"exponentialQuantity:",
						this.Props.exponentialQuantity.ToString(),
						"; exponentialRatioLimit:",
						this.Props.exponentialRatioLimit.ToString(),
						"; "
					}), this.myDebug);
				}
			}
			Tools.Warn(string.Concat(new string[]
			{
				"ageWeightedPeriod:",
				this.Props.ageWeightedPeriod.ToString(),
				"; olderSmallerPeriod:",
				this.Props.olderSmallerPeriod.ToString(),
				"; ",
				this.myDebug.ToString()
			}), false);
		}

		private void CalculateValues()
		{
			float num = Tools.GetPawnAgeOverlifeExpectancyRatio(this.parent.pawn, this.myDebug);
			num = ((num > 1f) ? 1f : num);
			this.calculatedMinDaysB4Next = this.Props.minDaysB4Next;
			this.calculatedMaxDaysB4Next = this.Props.maxDaysB4Next;
			if (this.Props.ageWeightedPeriod)
			{
				float num2 = this.Props.olderSmallerPeriod ? (-num) : num;
				this.calculatedMinDaysB4Next = this.Props.minDaysB4Next * (1f + num2);
				this.calculatedMaxDaysB4Next = this.Props.maxDaysB4Next * (1f + num2);
				Tools.Warn(string.Concat(new string[]
				{
					" ageWeightedPeriod: ",
					this.Props.ageWeightedPeriod.ToString(),
					" ageRatio: ",
					num.ToString(),
					" minDaysB4Next: ",
					this.Props.minDaysB4Next.ToString(),
					" maxDaysB4Next: ",
					this.Props.maxDaysB4Next.ToString(),
					" daysAgeRatio: ",
					num2.ToString(),
					" calculatedMinDaysB4Next: ",
					this.calculatedMinDaysB4Next.ToString(),
					";  calculatedMaxDaysB4Next: ",
					this.calculatedMaxDaysB4Next.ToString(),
					"; "
				}), this.myDebug);
			}
			this.calculatedQuantity = this.Props.spawnCount;
			if (this.Props.ageWeightedQuantity)
			{
				float num3 = this.Props.olderBiggerQuantity ? num : (-num);
				Tools.Warn("quantityAgeRatio: " + num3.ToString(), this.myDebug);
				this.calculatedQuantity = (int)Math.Round((double)this.Props.spawnCount * (double)(1f + num3));
				if (this.Props.exponentialQuantity)
				{
					num3 = 1f - num;
					if (num3 == 0f)
					{
						Tools.Warn(">ERROR< quantityAgeRatio is f* up : " + num3.ToString(), this.myDebug);
						this.blockSpawn = true;
						Tools.DestroyParentHediff(this.parent, this.myDebug);
						return;
					}
					float num4 = this.Props.olderBiggerQuantity ? (1f / num3) : (num3 * num3);
					bool flag = false;
					bool flag2 = false;
					if (num4 > (float)this.Props.exponentialRatioLimit)
					{
						num4 = (float)this.Props.exponentialRatioLimit;
						flag = true;
					}
					this.calculatedQuantity = (int)Math.Round((double)this.Props.spawnCount * (double)num4);
					if (this.calculatedQuantity < 1)
					{
						this.calculatedQuantity = 1;
						flag2 = true;
					}
					Tools.Warn(string.Concat(new string[]
					{
						" exponentialQuantity: ",
						this.Props.exponentialQuantity.ToString(),
						"; expoFactor: ",
						num4.ToString(),
						"; gotLimited: ",
						flag.ToString(),
						"; gotAugmented: ",
						flag2.ToString()
					}), this.myDebug);
				}
				Tools.Warn("; Props.spawnCount: " + this.Props.spawnCount.ToString() + "; calculatedQuantity: " + this.calculatedQuantity.ToString(), this.myDebug);
			}
		}

		private void CheckCalculatedValues()
		{
			if (this.calculatedQuantity > this.errorSpawnCount)
			{
				Tools.Warn(string.Concat(new string[]
				{
					">ERROR< calculatedQuantity is too high: ",
					this.calculatedQuantity.ToString(),
					"(>",
					this.errorSpawnCount.ToString(),
					"), check and adjust your hediff props"
				}), this.myDebug);
				this.blockSpawn = true;
				Tools.DestroyParentHediff(this.parent, this.myDebug);
				return;
			}
			if (this.calculatedMinDaysB4Next < this.errorMinDaysB4Next)
			{
				this.calculatedMinDaysB4Next = this.errorMinDaysB4Next;
			}
			if (this.calculatedMaxDaysB4Next < this.errorMinDaysB4Next)
			{
				this.calculatedMaxDaysB4Next = this.errorMinDaysB4Next;
			}
		}

		private void TraceCalculatedValues()
		{
			Tools.Warn("calculatedMinDaysB4Next:" + this.calculatedMinDaysB4Next.ToString(), this.myDebug);
			Tools.Warn("calculatedMaxDaysB4Next:" + this.calculatedMaxDaysB4Next.ToString(), this.myDebug);
			Tools.Warn("calculatedQuantity:" + this.calculatedQuantity.ToString(), this.myDebug);
		}

		private void CheckProps()
		{
			if (this.Props.animalThing && this.Props.animalToSpawn == null)
			{
				Tools.Warn(this.parent.pawn.Label + " has a hediffcomp_spawner with animalflag but without animalToSpawn", true);
				this.blockSpawn = true;
				Tools.DestroyParentHediff(this.parent, this.myDebug);
				return;
			}
			if (this.Props.minDaysB4Next <= 0f)
			{
				Tools.Warn(this.parent.pawn.Label + " has a hediffcomp_spawner with null/negative minDaysB4Next", true);
				this.blockSpawn = true;
				Tools.DestroyParentHediff(this.parent, this.myDebug);
				return;
			}
			if (this.Props.maxDaysB4Next <= 0f)
			{
				Tools.Warn(this.parent.pawn.Label + " has a hediffcomp_spawner with null/negative maxDaysB4Next", true);
				this.blockSpawn = true;
				Tools.DestroyParentHediff(this.parent, this.myDebug);
				return;
			}
			if (this.Props.maxDaysB4Next < this.Props.minDaysB4Next)
			{
				Tools.Warn(this.parent.pawn.Label + " has a hediffcomp_spawner with maxDaysB4Next < minDaysB4Next", true);
				this.blockSpawn = true;
				Tools.DestroyParentHediff(this.parent, this.myDebug);
				return;
			}
			if (this.Props.spawnCount <= 0)
			{
				Tools.Warn(this.parent.pawn.Label + " has a hediffcomp_spawner with null/negative spawnCount", true);
				this.blockSpawn = true;
				Tools.DestroyParentHediff(this.parent, this.myDebug);
				return;
			}
			if (!this.Props.animalThing && this.Props.thingToSpawn == null)
			{
				Tools.Warn(this.parent.pawn.Label + " has a hediffcomp_spawner without thingToSpawn", true);
				this.blockSpawn = true;
				Tools.DestroyParentHediff(this.parent, this.myDebug);
				return;
			}
			if (this.Props.ageWeightedQuantity && this.Props.exponentialQuantity && this.Props.exponentialRatioLimit > this.errorExponentialLimit)
			{
				Tools.Warn(string.Concat(new string[]
				{
					this.parent.pawn.Label,
					" has a hediffcomp_spawner with exponentialRatioLimit>",
					this.errorExponentialLimit.ToString(),
					" this is not allowed. It will be set to ",
					this.errorExponentialLimit.ToString()
				}), true);
				this.Props.exponentialRatioLimit = this.errorExponentialLimit;
			}
		}

		private bool CheckShouldSpawn()
		{
			this.ticksUntilSpawn--;
			if (this.ticksUntilSpawn <= 0)
			{
				if (this.TryDoSpawn())
				{
					return true;
				}
				Tools.Warn("Did not spawn, reseting countdown", this.myDebug);
				this.ResetCountdown();
			}
			return false;
		}

		private PawnKindDef MyPawnKindDefNamed(string myDefName)
		{
			return DefDatabase<PawnKindDef>.GetNamed(myDefName, true);
		}

		public bool TryDoSpawn()
		{
			Pawn pawn = this.parent.pawn;
			if (this.Props.spawnMaxAdjacent > 0 && pawn.Map.mapPawns.AllPawns.Where(delegate(Pawn mP)
			{
				ThingDef defToCompare = this.Props.animalThing ? this.Props.animalToSpawn?.race : this.Props.thingToSpawn;
				if (defToCompare?.race == null)
				{
					return false;
				}
				return mP.def == defToCompare && mP.Position.InHorDistOf(pawn.Position, (float)this.Props.spawnMaxAdjacent);
			}).Count<Pawn>() >= this.Props.spawnMaxAdjacent)
			{
				return false;
			}
			if (this.Props.animalThing)
			{
				if (this.Props.animalToSpawn == null)
				{
					return false;
				}
				Faction faction = this.Props.factionOfPlayerAnimal ? Faction.OfPlayer : null;
				int i = 0;
				while (i < this.calculatedQuantity)
				{
					IntVec3 intVec;
					if (!this.TryFindSpawnCell(out intVec))
					{
						return false;
					}
					Pawn pawn2 = PawnGenerator.GeneratePawn(this.Props.animalToSpawn, faction);
					if (pawn2 == null)
					{
						return false;
					}
					GenSpawn.Spawn(pawn2, intVec, pawn.Map, WipeMode.Vanish);
					pawn2.SetFaction(faction, null);
					FilthMaker.TryMakeFilth(intVec, pawn.Map, ThingDefOf.Filth_AmnioticFluid, pawn.LabelIndefinite(), 5, FilthSourceFlags.None);
					if (!this.Props.spawnForbidden)
					{
						pawn2.playerSettings.Master = pawn;
						pawn2.training.Train(TrainableDefOf.Obedience, pawn, true);
					}
					i++;
					continue;
				}
				if (PawnUtility.ShouldSendNotificationAbout(pawn) || PawnUtility.ShouldSendNotificationAbout(pawn))
				{
					Messages.Message(this.Props.spawnVerb.Translate(pawn.Named("PAWN")), pawn, MessageTypeDefOf.PositiveEvent, true);
				}
				return true;
			}
			else
			{
				IntVec3 intVec2;
				if (!this.TryFindSpawnCell(out intVec2))
				{
					return false;
				}
				Thing thing = ThingMaker.MakeThing(this.Props.thingToSpawn, null);
				if (thing == null)
				{
					return false;
				}
				thing.stackCount = this.calculatedQuantity;
				if (this.Props.spawnForbidden)
				{
					thing.SetForbidden(true, true);
				}
				GenPlace.TryPlaceThing(thing, intVec2, pawn.Map, ThingPlaceMode.Direct, null, null, default(Rot4));
				if (PawnUtility.ShouldSendNotificationAbout(pawn))
				{
					Messages.Message(this.Props.spawnVerb.Translate(pawn.Named("PAWN"), thing.Named("THING")), thing, MessageTypeDefOf.PositiveEvent, true);
				}
				return true;
			}
		}

		private bool TryFindSpawnCell(out IntVec3 result)
		{
			result = IntVec3.Invalid;
			bool result2;
			if (this.pawn == null)
			{
				result2 = false;
			}
			else
			{
				Map map = this.pawn.Map;
				if (map == null)
				{
					result2 = false;
				}
				else
				{
					result = CellFinder.RandomClosewalkCellNear(this.pawn.Position, map, 5, null);
					result2 = true;
				}
			}
			return result2;
		}

		private void ResetCountdown()
		{
			this.ticksUntilSpawn = (int)(this.RandomDays2wait() * 60000f);
			this.initialTicksUntilSpawn = this.ticksUntilSpawn;
		}

		private float RandomDays2wait()
		{
			return Rand.Range(this.calculatedMinDaysB4Next, this.calculatedMaxDaysB4Next);
		}

		private float RandomGraceDays()
		{
			return Rand.Range(this.Props.graceDays / 2f, this.Props.graceDays);
		}

		public override string CompTipStringExtra
		{
			get
			{
				if (!this.myDebug)
				{
					return null;
				}
				string text = "ticksUntilSpawn: " + this.ticksUntilSpawn.ToString() + "\n";
				string text2 = text;
				text = string.Concat(new string[]
				{
					text2,
					"initialTicksUntilSpawn: ",
					this.initialTicksUntilSpawn.ToString(),
					"\n"
				});
				text2 = text;
				text = string.Concat(new string[]
				{
					text2,
					"graceTicks: ",
					this.graceTicks.ToString(),
					"\n"
				});
				text2 = text;
				text = string.Concat(new string[]
				{
					text2,
					"hunger resets: ",
					this.hungerReset.ToString(),
					"\n"
				});
				text2 = text;
				text = string.Concat(new string[]
				{
					text2,
					"health resets: ",
					this.healthReset.ToString(),
					"\n"
				});
				text2 = text;
				text = string.Concat(new string[]
				{
					text2,
					"calculatedMinDaysB4Next: ",
					this.calculatedMinDaysB4Next.ToString(),
					"\n"
				});
				text2 = text;
				text = string.Concat(new string[]
				{
					text2,
					"calculatedMaxDaysB4Next: ",
					this.calculatedMaxDaysB4Next.ToString(),
					"\n"
				});
				text2 = text;
				text = string.Concat(new string[]
				{
					text2,
					"calculatedQuantity: ",
					this.calculatedQuantity.ToString(),
					"\n"
				});
				return text + "blockSpawn: " + this.blockSpawn.ToString();
			}
		}

		private int ticksUntilSpawn;

		private int initialTicksUntilSpawn;

		private int hungerReset;

		private int healthReset;

		private int graceTicks;

		private Pawn pawn;

		private float calculatedMaxDaysB4Next = 2f;

		private float calculatedMinDaysB4Next = 1f;

		private int calculatedQuantity = 1;

		private bool blockSpawn;

		private bool myDebug;

		private readonly float errorMinDaysB4Next = 0.001f;

		private readonly int errorExponentialLimit = 20;

		private readonly int errorSpawnCount = 750;
	}
} 