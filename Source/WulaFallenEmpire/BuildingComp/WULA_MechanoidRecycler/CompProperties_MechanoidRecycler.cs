
// CompProperties_MechanoidRecycler.cs (简化)
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class InitialUnitConfig
    {
        public PawnKindDef pawnKindDef;
        public int count = 1;
    }

    public class CompProperties_MechanoidRecycler : CompProperties
    {
        public List<ThingDef> recyclableRaces = new List<ThingDef>();
        public int recycleRange = 15;
        public JobDef recycleJobDef;
        public int maxStorageCapacity = 5;

        public List<PawnKindDef> spawnablePawnKinds = new List<PawnKindDef>();
        public List<InitialUnitConfig> initialUnits = new List<InitialUnitConfig>();

        public CompProperties_MechanoidRecycler()
        {
            compClass = typeof(CompMechanoidRecycler);
        }
    }

    public class CompMechanoidRecycler : ThingComp
    {
        // 空组件，用于属性存储
    }
}
