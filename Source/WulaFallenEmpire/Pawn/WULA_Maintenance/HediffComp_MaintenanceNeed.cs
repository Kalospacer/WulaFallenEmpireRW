using Verse;

namespace WulaFallenEmpire
{
    public class HediffCompProperties_MaintenanceNeed : HediffCompProperties
    {
        public float severityPerDayBeforeThreshold = 0.0f;
        public float severityPerDayAfterThreshold = 0.0f;
        public float thresholdDays = 0.0f;

        public HediffCompProperties_MaintenanceNeed()
        {
            compClass = typeof(HediffComp_MaintenanceNeed);
        }
    }

    public class HediffComp_MaintenanceNeed : HediffComp
    {
        private HediffCompProperties_MaintenanceNeed Props => (HediffCompProperties_MaintenanceNeed)props;

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);
            
            // We adjust severity once per game day (60000 ticks)
            if (parent.ageTicks % 60000 == 0)
            {
                float ageInDays = (float)parent.ageTicks / 60000f;
                if (ageInDays < Props.thresholdDays)
                {
                    severityAdjustment += Props.severityPerDayBeforeThreshold;
                }
                else
                {
                    severityAdjustment += Props.severityPerDayAfterThreshold;
                }
            }
        }
    }
}