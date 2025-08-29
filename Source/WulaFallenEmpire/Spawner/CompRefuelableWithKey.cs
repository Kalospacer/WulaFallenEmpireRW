using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    // 1. New Properties class that adds the save key
    public class CompProperties_RefuelableWithKey : CompProperties_Refuelable
    {
        public string saveKeysPrefix;

        public CompProperties_RefuelableWithKey()
        {
            compClass = typeof(CompRefuelableWithKey);
        }
    }

    // 2. New Component class. It's empty for now.
    // Its purpose is to be a safe target for our Harmony patch.
    public class CompRefuelableWithKey : CompRefuelable
    {
        // We will override PostExposeData using a Harmony patch
        // to avoid re-implementing the entire class.
    }
}