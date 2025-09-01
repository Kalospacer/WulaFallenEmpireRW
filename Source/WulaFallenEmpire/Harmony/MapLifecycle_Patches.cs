using System;
using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace WulaFallenEmpire
{
    [HarmonyPatch(typeof(MapParent), "ShouldRemoveMapNow")]
    [HarmonyPriority(600)]
    public static class MapParent_ShouldRemoveMapNow_Patch
    {
        public static void Postfix(ref bool __result, MapParent __instance)
        {
            if (!__result)
            {
                return;
            }
            try
            {
                if (__instance.HasMap && WulaMapProtectionHelper.ShouldProtectMap(__instance.Map))
                {
                    __result = false;
                }
            }
            catch (Exception arg)
            {
                Log.Error($"[WULA] Error in MapParent_ShouldRemoveMapNow_Patch: {arg}");
            }
        }
    }

    [HarmonyPatch(typeof(Game), "DeinitAndRemoveMap")]
    [HarmonyPatch(new Type[] { typeof(Map), typeof(bool) })]
    [HarmonyPriority(600)]
    public static class Game_DeinitAndRemoveMap_Patch
    {
        [HarmonyPrefix]
        private static bool PreventMapRemoval(Map map)
        {
            if (WulaMapProtectionHelper.ShouldProtectMap(map))
            {
                Log.Message("[WULA] Map destruction prevented by WulaMapProtectionHelper at Game.DeinitAndRemoveMap level.");
                return false; // 返回 false 来阻止原始方法的执行
            }
            return true; // 返回 true 来继续执行原始方法
        }
    }
}