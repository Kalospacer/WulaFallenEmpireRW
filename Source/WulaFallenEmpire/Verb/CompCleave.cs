using Verse;

namespace WulaFallenEmpire
{
    public class CompProperties_Cleave : CompProperties
    {
        public float cleaveAngle = 90f;
        public float cleaveRange = 2.9f;
        public float cleaveDamageFactor = 0.7f;
        public bool damageDowned = false;
        public DamageDef explosionDamageDef = null;

        public CompProperties_Cleave()
        {
            this.compClass = typeof(CompCleave);
        }
    }

    public class CompCleave : ThingComp
    {
        public CompProperties_Cleave Props => (CompProperties_Cleave)this.props;
    }
}