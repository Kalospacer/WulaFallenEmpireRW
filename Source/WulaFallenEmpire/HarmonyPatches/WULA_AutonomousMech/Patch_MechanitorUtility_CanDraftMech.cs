using HarmonyLib;
using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    [HarmonyPatch(typeof(Pawn_DraftController), "get_ShowDraftGizmo")]
    public static class Patch_Pawn_DraftController_ShowDraftGizmo
    {
        public static void Postfix(Pawn_DraftController __instance, ref bool __result)
        {
            Pawn pawn = __instance.pawn;
            
            if (!__result && pawn != null && pawn.IsColonyMech)
            {
                var comp = pawn.GetComp<CompAutonomousMech>();
                if (comp != null && comp.CanBeAutonomous)
                {
                    __result = true;
                }
            }
        }
    }

    [HarmonyPatch(typeof(MechanitorUtility), "CanDraftMech")]
    public static class Patch_MechanitorUtility_CanDraftMech
    {
        public static void Postfix(Pawn mech, ref AcceptanceReport __result)
        {
            if (!__result && mech != null && mech.IsColonyMech)
            {
                var comp = mech.GetComp<CompAutonomousMech>();
                if (comp != null && comp.CanBeAutonomous)
                {
                    __result = true;
                }
            }
        }
    }

    [HarmonyPatch(typeof(CompOverseerSubject), "CompInspectStringExtra")]
    public static class Patch_CompOverseerSubject_CompInspectStringExtra
    {
        public static void Postfix(CompOverseerSubject __instance, ref string __result)
        {
            Pawn mech = __instance.parent as Pawn;
            if (mech != null && mech.IsColonyMech)
            {
                var comp = mech.GetComp<CompAutonomousMech>();
                if (comp != null && comp.ShouldSuppressUncontrolledWarning)
                {
                    string autonomousStatus = comp.GetAutonomousStatusString();
                    if (!string.IsNullOrEmpty(autonomousStatus))
                    {
                        __result = autonomousStatus;
                    }
                    else
                    {
                        __result = "";
                    }
                }
            }
        }
    }
}