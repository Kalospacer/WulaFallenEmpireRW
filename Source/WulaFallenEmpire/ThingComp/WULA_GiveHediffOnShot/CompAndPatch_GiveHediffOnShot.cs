using HarmonyLib;
using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class CompProperties_GiveHediffOnShot : CompProperties
    {
        public HediffDef hediffDef;
        public float severityToAdd = 0.1f;

        public CompProperties_GiveHediffOnShot()
        {
            compClass = typeof(CompGiveHediffOnShot);
        }
    }

    public class CompGiveHediffOnShot : ThingComp
    {
        public CompProperties_GiveHediffOnShot Props => (CompProperties_GiveHediffOnShot)props;
    }

    // Patch 1: For all standard projectile verbs.
    [HarmonyPatch(typeof(Verb_LaunchProjectile), "TryCastShot")]
    public static class Patch_Verb_LaunchProjectile_TryCastShot
    {
        public static void Postfix(Verb_LaunchProjectile __instance, bool __result)
        {
            if (!__result) return;
            if (__instance.CasterPawn == null || __instance.EquipmentSource == null) return;

            CompGiveHediffOnShot comp = __instance.EquipmentSource.GetComp<CompGiveHediffOnShot>();
            if (comp == null || comp.Props.hediffDef == null) return;

            Hediff hediff = __instance.CasterPawn.health.GetOrAddHediff(comp.Props.hediffDef);
            hediff.Severity += comp.Props.severityToAdd;

            var disappearsComp = hediff.TryGetComp<HediffComp_Disappears>();
            disappearsComp?.ResetElapsedTicks();
        }
    }

    // Patch 2: Specifically for Verb_ShootWithOffset.
    [HarmonyPatch(typeof(Verb_Shoot), "TryCastShot")]
    public static class Patch_Verb_Shoot_TryCastShot
    {
        public static void Postfix(Verb_Shoot __instance, bool __result)
        {
            if (!__result) return;
            if (__instance.CasterPawn == null || __instance.EquipmentSource == null) return;

            CompGiveHediffOnShot comp = __instance.EquipmentSource.GetComp<CompGiveHediffOnShot>();
            if (comp == null || comp.Props.hediffDef == null) return;

            Hediff hediff = __instance.CasterPawn.health.GetOrAddHediff(comp.Props.hediffDef);
            hediff.Severity += comp.Props.severityToAdd;

            var disappearsComp = hediff.TryGetComp<HediffComp_Disappears>();
            disappearsComp?.ResetElapsedTicks();
        }


        // Patch 2: Specifically for Verb_ShootWithOffset.
        [HarmonyPatch(typeof(Verb_ShootWithOffset), "TryCastShot")]
        public static class Patch_ShootWithOffset_TryCastShot
        {
            public static void Postfix(Verb_ShootWithOffset __instance, bool __result)
            {
                if (!__result) return;
                if (__instance.CasterPawn == null || __instance.EquipmentSource == null) return;

                CompGiveHediffOnShot comp = __instance.EquipmentSource.GetComp<CompGiveHediffOnShot>();
                if (comp == null || comp.Props.hediffDef == null) return;

                Hediff hediff = __instance.CasterPawn.health.GetOrAddHediff(comp.Props.hediffDef);
                hediff.Severity += comp.Props.severityToAdd;

                var disappearsComp = hediff.TryGetComp<HediffComp_Disappears>();
                disappearsComp?.ResetElapsedTicks();
            }
        }
    }
}