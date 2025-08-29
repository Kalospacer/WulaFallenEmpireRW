using HarmonyLib;
using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    [HarmonyPatch(typeof(Pawn), "DropAndForbidEverything")]
public class Patch_WeaponDrop
{
	[HarmonyPrefix]
	private static bool PreFix(ref Pawn __instance, bool keepInventoryAndEquipmentIfInBed, bool rememberPrimary)
	{
		if (__instance.HasComp<CompMechWeapon>())
		{
			if (!__instance.InContainerEnclosed)
			{
				if (__instance.SpawnedOrAnyParentSpawned)
				{
					if (__instance.carryTracker?.CarriedThing != null)
					{
						__instance.carryTracker.TryDropCarriedThing(__instance.PositionHeld, ThingPlaceMode.Near, out var _);
					}
					if (!keepInventoryAndEquipmentIfInBed || !__instance.InBed())
					{
						__instance.equipment?.DropAllEquipment(__instance.PositionHeld, forbid: true, rememberPrimary);
						if (__instance.inventory != null && __instance.inventory.innerContainer.TotalStackCount > 0)
						{
							__instance.inventory.DropAllNearPawn(__instance.PositionHeld, forbid: true);
						}
					}
				}
				return false;
			}
			if (__instance.carryTracker?.CarriedThing != null)
			{
				__instance.carryTracker.innerContainer.TryTransferToContainer(__instance.carryTracker.CarriedThing, __instance.holdingOwner);
			}
			if (__instance.equipment?.Primary != null)
			{
				__instance.equipment.TryTransferEquipmentToContainer(__instance.equipment.Primary, __instance.holdingOwner);
			}
			Pawn_InventoryTracker inventory = __instance.inventory;
			if (inventory == null)
			{
				return false;
			}
			inventory.innerContainer.TryTransferAllToContainer(__instance.holdingOwner);
			return false;
		}
		return true;
	}
}
}