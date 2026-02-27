using HarmonyLib;
using Verse;
using RimWorld;
using Verse.AI;

namespace WulaFallenEmpire
{
    [HarmonyPatch]
    public static class FlightHarmonyPatches
    {
        // Patch 1: Override fly animation selection for pawns with CompPawnFlight.
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Pawn_FlightTracker), "GetBestFlyAnimation")]
        public static bool GetBestFlyAnimation_Prefix(Pawn pawn, ref AnimationDef __result)
        {
            var flightComp = pawn?.TryGetComp<CompPawnFlight>();
            if (flightComp == null)
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

        // Patch 2: Override flight start logic — only decides WHEN TO START flying.
        // Landing is handled by FlightTick_Postfix via posture check.
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
            else if (compProps.flightCondition == FlightCondition.DraftedAndMove && (___pawn.Drafted || ___pawn.pather.MovingNow))
            {
                shouldBeFlying = true;
            }
            else if (compProps.flightCondition == FlightCondition.Drafted && ___pawn.Drafted)
            {
                shouldBeFlying = true;
            }

            if (shouldBeFlying && !__instance.Flying)
            {
                __instance.StartFlying();
            }
            job.flying = shouldBeFlying;
            return false;
        }

        // Patch 3: Posture-based landing guard — only decides WHEN TO LAND.
        // If the pawn is not standing (lying in bed, downed on ground, etc.), force land.
        // This is a universal check that works with all vanilla and modded jobs
        // without needing to maintain a job blacklist.
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Pawn_FlightTracker), "FlightTick")]
        public static void FlightTick_Postfix(Pawn_FlightTracker __instance, Pawn ___pawn)
        {
            if (!__instance.Flying) return;

            var flightComp = ___pawn?.TryGetComp<CompPawnFlight>();
            if (flightComp == null) return;

            if (___pawn.GetPosture() != PawnPosture.Standing)
            {
                __instance.ForceLand();
                if (___pawn.CurJob != null)
                    ___pawn.CurJob.flying = false;
            }
        }
    }
}