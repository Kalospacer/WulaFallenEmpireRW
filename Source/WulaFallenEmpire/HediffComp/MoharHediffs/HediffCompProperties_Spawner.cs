using System;
using Verse;

namespace WulaFallenEmpire.MoharHediffs
{
	public class HediffCompProperties_Spawner : HediffCompProperties
	{
		public HediffCompProperties_Spawner()
		{
			this.compClass = typeof(HediffComp_Spawner);
		}

		/// <summary>
		/// 要生成的物品的ThingDef。如果animalThing为false，则使用此项。
		/// </summary>
		public ThingDef thingToSpawn;

		/// <summary>
		/// 每次生成的基础物品数量。
		/// </summary>
		public int spawnCount = 1;

		/// <summary>
		/// 如果为true，则生成一个Pawn（动物）。如果为false，则生成一个Thing。
		/// </summary>
		public bool animalThing;

		/// <summary>
		/// 要生成的动物的PawnKindDef。如果animalThing为true，则使用此项。
		/// </summary>
		public PawnKindDef animalToSpawn;

		/// <summary>
		/// 如果为true，生成的动物将属于玩家派系。
		/// </summary>
		public bool factionOfPlayerAnimal;

		/// <summary>
		/// 下一次生成事件发生前的最少天数。
		/// </summary>
		public float minDaysB4Next = 1f;

		/// <summary>
		/// 下一次生成事件发生前的最大天数。
		/// </summary>
		public float maxDaysB4Next = 2f;

		/// <summary>
		/// 生成后进入宽限期（延迟下一次生成）的几率（0.0到1.0）。
		/// </summary>
		public float randomGrace;

		/// <summary>
		/// 如果触发，宽限期的持续时间（天）。
		/// </summary>
		public float graceDays = 0.5f;

		/// <summary>
		/// 附近允许的相同Pawn的最大数量。如果超过该数量，则暂停生成。-1为禁用。
		/// </summary>
		public int spawnMaxAdjacent = -1;

		/// <summary>
		/// 如果为true，生成的物品将被禁用。
		/// </summary>
		public bool spawnForbidden;

		/// <summary>
		/// 如果为true，当宿主Pawn饥饿时，生成将暂停。
		/// </summary>
		public bool hungerRelative;

		/// <summary>
		/// 如果为true，当宿主Pawn受伤时，生成将暂停。
		/// </summary>
		public bool healthRelative;

		/// <summary>
		/// 如果为true，生成数量将根据宿主的年龄进行调整。
		/// </summary>
		public bool ageWeightedQuantity;

		/// <summary>
		/// 如果为true，生成周期（两次生成之间的时间）将根据宿主的年龄进行调整。
		/// </summary>
		public bool ageWeightedPeriod;

		/// <summary>
		/// 如果为true且ageWeightedPeriod为true，则随着宿主年龄增长，生成周期变短。如果为false，则变长。
		/// </summary>
		public bool olderSmallerPeriod;

		/// <summary>
		/// 如果为true且ageWeightedQuantity为true，则随着宿主年龄增长，生成数量变多。如果为false，则变少。
		/// </summary>
		public bool olderBiggerQuantity;

		/// <summary>
		/// 如果为true且ageWeightedQuantity为true，则随年龄增长的数量缩放将是指数性的而非线性的。
		/// </summary>
		public bool exponentialQuantity;

		/// <summary>
		/// 指数级数量缩放的最大乘数，以防止出现荒谬的数字。
		/// </summary>
		public int exponentialRatioLimit = 15;

		/// <summary>
		/// 生成时显示的消息的翻译键（例如，“{PAWN}下了一个蛋。”）。
		/// </summary>
		public string spawnVerb = "delivery";

		/// <summary>
		/// 如果为true，则为此组件启用详细的调试日志记录。
		/// </summary>
		public bool debug;
	}
} 