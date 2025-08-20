using System;
using Verse;

namespace WulaFallenEmpire
{
	public class HomingProjectileDef : DefModExtension
	{
		public float SpeedChangeTilesPerTickOverride;
		public FloatRange SpeedRangeTilesPerTickOverride;

		public float hitChance = 0.5f;

		public float homingSpeed = 0.1f;

		public float initRotateAngle = 30f;

		public float proximityFuseRange = 0.5f; // 调整默认值，使其在接近目标时能正确触发引信

		public IntRange destroyTicksAfterLosingTrack = new IntRange(60, 120);

		public ThingDef extraProjectile;

		public float speedChangePerTick;

		public FloatRange? speedRangeOverride;
		public FleckDef tailFleckDef;
		// 拖尾特效的详细配置参数
		public int fleckMakeFleckTickMax = 1;
		public IntRange fleckMakeFleckNum = new IntRange(1, 1);
		public FloatRange fleckAngle = new FloatRange(-180f, 180f);
		public FloatRange fleckScale = new FloatRange(1f, 1f);
		public FloatRange fleckSpeed = new FloatRange(0f, 0f);
		public FloatRange fleckRotation = new FloatRange(-180f, 180f);
	}
}