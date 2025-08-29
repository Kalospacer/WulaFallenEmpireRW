using HarmonyLib;
using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    [HarmonyPatch(typeof(MechRepairUtility), "IsMissingWeapon")]
public class Patch_MissingWeapon
{
	[HarmonyPostfix]
	private static void PostFix(ref bool __result, Pawn mech)
	{
		if (mech.HasComp<CompMechWeapon>())
		{
			__result = false;
		}
		}
	}
}