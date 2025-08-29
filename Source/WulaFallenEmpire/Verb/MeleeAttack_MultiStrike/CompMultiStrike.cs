using Verse;

namespace WulaFallenEmpire
{
    public class CompProperties_MultiStrike : CompProperties
    {
        public IntRange strikeCount = new IntRange(3, 3);
        public float damageMultiplierPerStrike = 0.5f;

        public CompProperties_MultiStrike()
        {
            compClass = typeof(CompMultiStrike);
        }
    }

    public class CompMultiStrike : ThingComp
    {
        public CompProperties_MultiStrike Props => (CompProperties_MultiStrike)props;
    }
}