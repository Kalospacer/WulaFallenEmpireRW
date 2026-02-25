using HarmonyLib;
using Verse;
using RimWorld;
using Verse.AI;

namespace WulaFallenEmpire
{
    [HarmonyPatch]
    public static class FlightHarmonyPatches
    {
        // Corrected Patch 1: The method signature now correctly matches the static target method.
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Pawn_FlightTracker), "GetBestFlyAnimation")]
        public static bool GetBestFlyAnimation_Prefix(Pawn pawn, ref AnimationDef __result) // Correct parameters: Pawn pawn, not __instance and ___pawn
        {
            var flightComp = pawn?.TryGetComp<CompPawnFlight>();
            if (flightComp == null) // No props check needed, as the crash was due to wrong signature
            {
                return true; 
            }

            var compProps = flightComp.Props;
            AnimationDef selectedAnim = null;

            if (pawn.gender == Gender.Female && compProps.flyingAnimationNorthFemale != null)
            {
                switch (pawn.Rotation.AsInt)
                {
                    case 0: selectedAnim = compProps.flyingAnimationNorthFemale; break;
                    case 1: selectedAnim = compProps.flyingAnimationEastFemale; break;
                    case 2: selectedAnim = compProps.flyingAnimationSouthFemale; break;
                    case 3: selectedAnim = compProps.flyingAnimationEastFemale ?? compProps.flyingAnimationEast; break;
                }
            }
            else
            {
                switch (pawn.Rotation.AsInt)
                {
                    case 0: selectedAnim = compProps.flyingAnimationNorth; break;
                    case 1: selectedAnim = compProps.flyingAnimationEast; break;
                    case 2: selectedAnim = compProps.flyingAnimationSouth; break;
                    case 3: selectedAnim = compProps.flyingAnimationEast; break;
                }
            }

            if (selectedAnim != null)
            {
                __result = selectedAnim;
                return false;
            }
            return true;
        }

        // Patch 2 remains correct as Notify_JobStarted is a non-static method.
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Pawn_FlightTracker), "Notify_JobStarted")]
        public static bool Notify_JobStarted_Prefix(Job job, Pawn_FlightTracker __instance, Pawn ___pawn)
        {
            var flightComp = ___pawn?.TryGetComp<CompPawnFlight>();
            if (flightComp == null || __instance == null || !__instance.CanEverFly || ___pawn == null || ___pawn.Dead)
            {
                return true;
            }

            bool shouldBeFlying = false;
            var compProps = flightComp.Props;
            if (compProps.flightCondition == FlightCondition.Always)
            {
                shouldBeFlying = true;
            }
            else if (compProps.flightCondition == FlightCondition.DraftedAndMove && ___pawn.Drafted || ___pawn.pather.MovingNow)
            {
                shouldBeFlying = true;
            }
            else if (compProps.flightCondition == FlightCondition.Drafted && ___pawn.Drafted)
            {
                shouldBeFlying = true;
            }

            if (shouldBeFlying)
            {
                if (!__instance.Flying) __instance.StartFlying();
                job.flying = true;
            }
            else
            {
                if (__instance.Flying) __instance.ForceLand();
                job.flying = false;
            }
            return false;
        }
    }
}