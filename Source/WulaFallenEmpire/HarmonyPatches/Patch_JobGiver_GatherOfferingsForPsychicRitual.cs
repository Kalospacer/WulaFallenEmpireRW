using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using UnityEngine;

namespace WulaFallenEmpire.HarmonyPatches
{
    [HarmonyPatch(typeof(JobGiver_GatherOfferingsForPsychicRitual), "TryGiveJob")]
    public static class Patch_JobGiver_GatherOfferingsForPsychicRitual_TryGiveJob
    {
        [HarmonyPrefix]
        public static bool Prefix(Pawn pawn, ref Job __result)
        {
            Lord lord = pawn.GetLord();
            if (lord == null)
            {
                return true; // Continue to original method
            }

            if (!(lord.CurLordToil is LordToil_PsychicRitual lordToil_PsychicRitual))
            {
                return true; // Continue to original method
            }

            var ritualDef = lordToil_PsychicRitual.RitualData.psychicRitual.def as PsychicRitualDef_WulaBase;
            if (ritualDef == null)
            {
                return true; // Not our custom ritual, continue to original method
            }

            if (ritualDef.RequiredOffering == null)
            {
                __result = null;
                return false; // Stop original method
            }

            PsychicRitual psychicRitual = lordToil_PsychicRitual.RitualData.psychicRitual;
            PsychicRitualRoleDef psychicRitualRoleDef = psychicRitual.assignments.RoleForPawn(pawn);
            if (psychicRitualRoleDef == null)
            {
                __result = null;
                return false; // Stop original method
            }

            float num = PsychicRitualToil_GatherOfferings.PawnsOfferingCount(psychicRitual.assignments.AssignedPawns(psychicRitualRoleDef), ritualDef.RequiredOffering);
            int needed = Mathf.CeilToInt(ritualDef.RequiredOffering.GetBaseCount() - num);
            if (needed == 0)
            {
                __result = null;
                return false; // Stop original method
            }

            Thing thing2 = GenClosest.ClosestThingReachable(pawn.PositionHeld, pawn.MapHeld, ThingRequest.ForGroup(ThingRequestGroup.HaulableAlways), PathEndMode.Touch, TraverseParms.For(pawn), 9999f, delegate (Thing thing)
            {
                if (!ritualDef.RequiredOffering.filter.Allows(thing))
                {
                    return false;
                }
                if (thing.IsForbidden(pawn))
                {
                    return false;
                }
                int stackCount = Mathf.Min(needed, thing.stackCount);
                return pawn.CanReserve(thing, 10, stackCount);
            });

            if (thing2 == null)
            {
                __result = null;
                return false; // Stop original method
            }

            Job job = JobMaker.MakeJob(JobDefOf.TakeCountToInventory, thing2);
            job.count = Mathf.Min(needed, thing2.stackCount);
            __result = job;
            
            return false; // Stop original method, we've provided the result
        }
    }
}