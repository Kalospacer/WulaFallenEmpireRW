using System;
using Verse;

namespace WulaFallenEmpire
{
	public class HomingProjectileDef : DefModExtension
	{
		public float SpeedChangeTilesPerTickOverride
		{
			get
			{
				return this.speedChangePerTick / 100f;
			}
		}

		public FloatRange SpeedRangeTilesPerTickOverride
		{
			get
			{
				return this.speedRangeOverride.Value * 0.01f;
			}
		}

		public float hitChance = 0.5f;

		public float homingSpeed = 0.1f;

		public float initRotateAngle = 30f;

		public float proximityFuseRange = 0f;

		public IntRange destroyTicksAfterLosingTrack = new IntRange(60, 120);

		public ThingDef extraProjectile;

		public float speedChangePerTick;

		public FloatRange? speedRangeOverride;
		public FleckDef tailFleckDef;
	}
}