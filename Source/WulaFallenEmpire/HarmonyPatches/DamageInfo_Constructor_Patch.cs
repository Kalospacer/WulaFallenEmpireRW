using HarmonyLib;
using RimWorld;
using Verse;
using UnityEngine;

namespace WulaFallenEmpire.HarmonyPatches
{
    // ==========================================================================================
    // FINAL CORRECTED PATCH: Targets the DamageInfo constructor.
    //
    // REASONING: This is the universal "catch-all" solution. Almost all damage inflicted
    // in the game must first create a DamageInfo instance. By patching the constructor
    // with a Postfix, we can modify the damage value right at its source, immediately
    // after it's created. This ensures our logic applies universally (to melee, all
    // projectile types, abilities, etc.) and is not bypassed by specific implementations
    // like the one found in `Bullet.Impact`.
    // ==========================================================================================
    [HarmonyPatch(typeof(DamageInfo), MethodType.Constructor)]
    [HarmonyPatch(new[] {
        typeof(DamageDef),
        typeof(float),
        typeof(float),
        typeof(float),
        typeof(Thing),
        typeof(BodyPartRecord),
        typeof(ThingDef),
        typeof(DamageInfo.SourceCategory),
        typeof(Thing),
        typeof(bool),
        typeof(bool),
        typeof(QualityCategory),
        typeof(bool),
        typeof(bool)
    })]
    public static class DamageInfo_Constructor_Patch // Renamed class for ultimate clarity
    {
        public static void Postfix(ref DamageInfo __instance, Thing instigator, ThingDef weapon)
        {
            if (weapon == null)
            {
                return;
            }

            var psychicCompProps = weapon.GetCompProperties<CompProperties_PsychicScaling>();
            if (psychicCompProps == null)
            {
                return;
            }

            if (!(instigator is Pawn instigatorPawn))
            {
                return;
            }

            float psychicSensitivity = instigatorPawn.GetStatValue(StatDefOf.PsychicSensitivity);
            
            float damageMultiplier = 1f;
            if (psychicSensitivity > 1f)
            {
                damageMultiplier = 1f + (psychicSensitivity - 1f) * psychicCompProps.damageMultiplierPerSensitivityPoint;
            }
            else if (psychicSensitivity < 1f)
            {
                damageMultiplier = 1f - (1f - psychicSensitivity) * psychicCompProps.damageReductionMultiplierPerSensitivityPoint;
            }

            float originalAmount = __instance.Amount;
            float finalMultiplier = Mathf.Max(0f, damageMultiplier);
            float newAmount = originalAmount * finalMultiplier;
            
            __instance.SetAmount(newAmount);
        }
    }
}