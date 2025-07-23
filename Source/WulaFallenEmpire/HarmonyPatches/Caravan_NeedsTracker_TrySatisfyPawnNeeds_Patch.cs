using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace WulaFallenEmpire.HarmonyPatches
{
    [HarmonyPatch(typeof(Caravan_NeedsTracker), "TrySatisfyPawnNeeds")]
    public static class Caravan_NeedsTracker_TrySatisfyPawnNeeds_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Caravan_NeedsTracker __instance, Pawn pawn)
        {
            // Check if the pawn is valid and has needs
            if (pawn == null || pawn.Dead || pawn.needs == null)
            {
                return;
            }

            // Try to get the custom energy need
            Need_WulaEnergy wulaEnergyNeed = pawn.needs.TryGetNeed<Need_WulaEnergy>();
            if (wulaEnergyNeed == null)
            {
                return;
            }

            // Get settings from XML
            WulaCaravanEnergyDef settings = DefDatabase<WulaCaravanEnergyDef>.GetNamed("WulaCaravanEnergySettings", false);
            if (settings == null)
            {
                // Log an error only once to avoid spamming the log
                Log.ErrorOnce("[WulaFallenEmpire] WulaCaravanEnergySettings Def not found. Caravan energy consumption will not work.", "WulaCaravanEnergySettingsNotFound".GetHashCode());
                return;
            }

            // Check if the pawn is already charging, if so, do nothing.
            if (pawn.health.hediffSet.HasHediff(HediffDef.Named(settings.hediffDefNameToAdd)))
            {
                return;
            }

            // Check if the pawn actually needs energy, based on the threshold from XML.
            if (wulaEnergyNeed.CurLevelPercentage >= settings.consumeThreshold)
            {
                return;
            }

            // Find an energy core in the caravan's inventory based on the defName from XML
            Thing energyCore = CaravanInventoryUtility.AllInventoryItems(__instance.caravan).FirstOrFallback((Thing t) => t.def.defName == settings.energyItemDefName);

            if (energyCore != null)
            {
                // "Ingest" the energy core by applying the hediff, mimicking the IngestionOutcomeDoer.
                Hediff hediff = HediffMaker.MakeHediff(HediffDef.Named(settings.hediffDefNameToAdd), pawn);
                hediff.Severity = 1.0f;
                pawn.health.AddHediff(hediff);

                // Instead of destroying the core, we replace it with a used one.
                
                // 1. Consume one energy core from the stack
                energyCore.SplitOff(1).Destroy();

                // 2. Add one used energy core to the caravan inventory
                Thing usedCore = ThingMaker.MakeThing(ThingDef.Named("WULA_Charge_Cube_No_Power"));
                usedCore.stackCount = 1;
                CaravanInventoryUtility.GiveThing(__instance.caravan, usedCore);
            }
        }
    }
}
