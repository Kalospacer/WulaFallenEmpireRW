using Verse;
using RimWorld;

namespace WulaFallenEmpire
{
    public class HediffCompProperties_WulaCharging : HediffCompProperties
    {
        public float energyPerTick = 0.001f; // 每tick补充的能量量
        public int durationTicks = 600; // 持续时间，例如600ticks = 10秒

        public HediffCompProperties_WulaCharging()
        {
            this.compClass = typeof(HediffComp_WulaCharging);
        }
    }

    public class HediffComp_WulaCharging : HediffComp
    {
        public HediffCompProperties_WulaCharging Props => (HediffCompProperties_WulaCharging)this.props;

        private int ticksPassed = 0;
        private Thing sourceThing; // 新增字段，用于存储能量核心物品

        public void SetSourceThing(Thing thing)
        {
            this.sourceThing = thing;
        }

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);

            ticksPassed++;
            if (ticksPassed >= Props.durationTicks)
            {
                // 持续时间结束，移除Hediff
                this.parent.pawn.health.RemoveHediff(this.parent);
                return;
            }

            Need_WulaEnergy energyNeed = this.parent.pawn.needs.TryGetNeed<Need_WulaEnergy>();
            if (energyNeed != null)
            {
                // 从sourceThing的ThingDefExtension_EnergySource获取能量量
                ThingDefExtension_EnergySource ext = sourceThing?.def.GetModExtension<ThingDefExtension_EnergySource>();
                if (ext != null)
                {
                    energyNeed.CurLevel += ext.energyAmount / Props.durationTicks; // 将总能量量分摊到每个tick
                }
                else
                {
                    // 如果没有找到能量来源扩展，则使用默认的energyPerTick
                    energyNeed.CurLevel += Props.energyPerTick;
                }
            }
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref ticksPassed, "ticksPassed", 0);
            Scribe_References.Look(ref sourceThing, "sourceThing"); // 保存sourceThing
        }
    }
}
