using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    
    [DefOf]
    public static class ThingDefOf_WULA
    {
        public static ThingDef WULA_MaintenancePod;
        public static ThingDef WULA_Charging_Station_Synth;
        public static ThingDef WULA_PocketMapExit;
        public static ThingDef Hyperweave;
        
        static ThingDefOf_WULA()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(ThingDefOf_WULA));
        }
    }
    
    [DefOf]
    public static class JobDefOf_WULA
    {
        public static JobDef WULA_EnterMaintenancePod;
        public static JobDef WULA_HaulToMaintenancePod;
        public static JobDef WULA_InspectBuilding;

        static JobDefOf_WULA()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(JobDefOf_WULA));
        }
    }
    
    [DefOf]
    public static class WulaStatDefOf
    {
        public static StatDef WulaEnergyMaxLevelOffset;
        public static StatDef WulaEnergyFallRateFactor;

        static WulaStatDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(WulaStatDefOf));
        }
    }
    
    [DefOf]
    public static class WulaNeedDefOf
    {
        public static NeedDef WULA_Energy;
        
        static WulaNeedDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(WulaNeedDefOf));
        }
    }


    [DefOf]
    public static class WulaStatCategoryDefOf
    {
        public static StatCategoryDef WULA_Synth;

        static WulaStatCategoryDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(WulaStatCategoryDefOf));
        }
    }

    [DefOf]
    public static class WulaDamageDefOf
    {
        public static DamageDef Wula_Dark_Matter_Flame;

        static WulaDamageDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(WulaDamageDefOf));
        }
    }

    [DefOf]
    public static class WulaDefOf
    {
        public static DroneWorkModeDef Work;
        public static DroneWorkModeDef Recharge;
        public static DroneWorkModeDef Shutdown;
        //public static DroneWorkModeDef AutoFight;
        // public static PawnTableDef WULA_AutonomousMechs;

        static WulaDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(WulaDefOf));
        }
    }
}