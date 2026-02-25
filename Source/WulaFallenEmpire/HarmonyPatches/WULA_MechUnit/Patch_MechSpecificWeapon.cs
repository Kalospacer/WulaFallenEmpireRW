// File: Patches/Patch_Wulamechunit.cs (修改EquipmentUtility_CanEquip_Patch部分)
using HarmonyLib;
using RimWorld;
using System;
using Verse;

namespace WulaFallenEmpire
{
    [HarmonyPatch(typeof(EquipmentUtility), nameof(EquipmentUtility.CanEquip), new Type[] { typeof(Thing), typeof(Pawn), typeof(string), typeof(bool) }, new ArgumentType[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out, ArgumentType.Normal })]
    public static class EquipmentUtility_CanEquip_Patch
    {
        [HarmonyPrefix]
        public static bool CanEquip_Prefix(Thing thing, Pawn pawn, out string cantReason, ref bool __result)
        {
            cantReason = null;

            try
            {
                // 检查是否有机甲专用武器组件
                var mechWeapon = thing?.TryGetComp<CompMechOnlyWeapon>();

                // 情况1：这是机甲专用武器
                if (mechWeapon != null)
                {
                    // 检查是否是机甲
                    if (pawn is Wulamechunit)
                    {
                        // 检查是否允许此机甲使用
                        if (mechWeapon.CanBeEquippedByMech(pawn))
                        {
                            // 机甲可以使用此专用武器
                            return true;
                        }
                        else
                        {
                            // 此机甲不在允许列表中
                            cantReason = "WULA_Equipment_For_Other_Mech".Translate();
                            __result = false;
                            return false;
                        }
                    }
                    else
                    {
                        // 非机甲尝试装备专用武器，禁止
                        cantReason = "WULA_Human_Cannot_Equip_Mech_Weapon".Translate();
                        __result = false;
                        return false;
                    }
                }
                // 情况2：这是普通武器
                else if (thing?.def?.IsWeapon == true)
                {
                    // 检查是否是机甲
                    if (pawn is Wulamechunit)
                    {
                        // 机甲不能装备普通武器
                        cantReason = "WULA_Equipment_Not_Allow_For_Mech".Translate();
                        __result = false;
                        return false;
                    }
                    // 非机甲可以装备普通武器，继续检查
                }

                // 情况3：不是武器或不是机甲，按原逻辑处理
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[WULA] CanEquip patch error: {ex}");
                return true;
            }
        }
    }
}