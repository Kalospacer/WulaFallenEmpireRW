using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    // We patch the base class method
    [HarmonyPatch(typeof(CompRefuelable), "PostExposeData")]
    public static class Patch_CompRefuelableWithKey_PostExposeData
    {
        public static bool Prefix(CompRefuelable __instance)
        {
            // But we only act if the instance is our custom subclass
            if (!(__instance is CompRefuelableWithKey refuelableWithKey))
            {
                // If it's not our class, run the original method
                return true;
            }

            // Get the private fields from the base CompRefuelable class using reflection
            FieldInfo fuelField = AccessTools.Field(typeof(CompRefuelable), "fuel");
            FieldInfo configuredTargetFuelLevelField = AccessTools.Field(typeof(CompRefuelable), "configuredTargetFuelLevel");
            FieldInfo allowAutoRefuelField = AccessTools.Field(typeof(CompRefuelable), "allowAutoRefuel");

            // Get the props from our custom component
            var props = (CompProperties_RefuelableWithKey)refuelableWithKey.Props;
            string prefix = props.saveKeysPrefix;

            if (prefix.NullOrEmpty())
            {
                Log.ErrorOnce($"CompRefuelableWithKey on {refuelableWithKey.parent.def.defName} has a null or empty saveKeysPrefix. Defaulting to standard save.", refuelableWithKey.GetHashCode());
                // If no prefix, let the original method run
                return true; 
            }

            // Get current values from the instance
            float fuel = (float)fuelField.GetValue(refuelableWithKey);
            float configuredTargetFuelLevel = (float)configuredTargetFuelLevelField.GetValue(refuelableWithKey);
            bool allowAutoRefuel = (bool)allowAutoRefuelField.GetValue(refuelableWithKey);
            
            // Scribe the values with our prefix
            Scribe_Values.Look(ref fuel, prefix + "_fuel", 0f);
            Scribe_Values.Look(ref configuredTargetFuelLevel, prefix + "_configuredTargetFuelLevel", -1f);
            Scribe_Values.Look(ref allowAutoRefuel, prefix + "_allowAutoRefuel", true);

            // Set the new values back to the instance
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                fuelField.SetValue(refuelableWithKey, fuel);
                configuredTargetFuelLevelField.SetValue(refuelableWithKey, configuredTargetFuelLevel);
                allowAutoRefuelField.SetValue(refuelableWithKey, allowAutoRefuel);
            }

            // Prevent the original PostExposeData from running
            return false;
        }
    }
}