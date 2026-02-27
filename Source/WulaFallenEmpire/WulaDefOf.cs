using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    
    [DefOf]
    public static class Wula_ThingDefOf
    {
        public static ThingDef WULA_MaintenancePod;
        public static ThingDef WULA_Charging_Station_Synth;
        public static ThingDef WULA_PocketMapExit;
        public static ThingDef Hyperweave;
        
        static Wula_ThingDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(Wula_ThingDefOf));
        }
    }
    
    [DefOf]
    public static class Wula_JobDefOf
    {
        public static JobDef WULA_EnterMaintenancePod;
        public static JobDef WULA_HaulToMaintenancePod;
        public static JobDef WULA_InspectBuilding;
        public static JobDef WULA_Launch_Proj;
        public static JobDef WULA_EnterMech;
        public static JobDef WULA_RefuelMech;
        public static JobDef WULA_RepairMech;
        public static JobDef WULA_ForceEjectPilot;
        public static JobDef WULA_CarryToMech;
        public static JobDef WULA_TransformPawn;
        
        static Wula_JobDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(Wula_JobDefOf));
        }
    }
    
    [DefOf]
    public static class Wula_StatDefOf
    {
        public static StatDef WulaEnergyMaxLevelOffset;
        public static StatDef WulaEnergyFallRateFactor;

        static Wula_StatDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(Wula_StatDefOf));
        }
    }
    
    [DefOf]
    public static class Wula_NeedDefOf
    {
        public static NeedDef WULA_Energy;
        
        static Wula_NeedDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(Wula_NeedDefOf));
        }
    }


    [DefOf]
    public static class Wula_StatCategoryDefOf
    {
        public static StatCategoryDef WULA_Synth;

        static Wula_StatCategoryDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(Wula_StatCategoryDefOf));
        }
    }

    [DefOf]
    public static class Wula_DamageDefOf
    {
        public static DamageDef Wula_Dark_Matter_Flame;

        static Wula_DamageDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(Wula_DamageDefOf));
        }
    }

    [DefOf]
    public static class WULA_MentalStateDefOf
    {
        public static MentalStateDef WULA_MechNoPilot;

        static WULA_MentalStateDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(WULA_MentalStateDefOf));
        }
    }

    [DefOf] 
    public static class WulaDefOf
    {
        // public static PawnTableDef WULA_AutonomousMechs;

        static WulaDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(WulaDefOf));
        }
    }
}