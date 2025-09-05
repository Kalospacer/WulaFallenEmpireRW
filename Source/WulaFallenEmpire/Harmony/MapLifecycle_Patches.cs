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
            // 如果游戏本来就不打算删除，或者地图不存在，我们什么都不做
            if (!__result || !__instance.HasMap)
            {
                return;
            }

            try
            {
                // 检查地图上是否存在一个“活着”的武装穿梭机
                if (WulaMapProtectionHelper.ShouldProtectMap(__instance.Map))
                {
                    // 游戏打算删除，但我们的逻辑说现在还不行，所以直接覆盖结果。
                    // 因为 ShouldRemoveMapNow 会被周期性调用，所以这是安全的。
                    __result = false;
                }
            }
            catch (Exception arg)
            {
                Log.Error($"[WULA] Error in MapParent_ShouldRemoveMapNow_Patch: {arg}");
            }
        }
    }
}
