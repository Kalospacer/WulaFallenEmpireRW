using System.Linq;
using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace WulaFallenEmpire
{
    [HarmonyPatch(typeof(MapParent), "CheckRemoveMapNow")]
    public static class MapParent_CheckRemoveMapNow_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(MapParent __instance)
        {
            // 如果该 MapParent 没有地图，则直接放行，执行原方法（虽然原方法也会检查 HasMap，但这里提前返回更清晰）
            if (!__instance.HasMap)
            {
                return true;
            }

            try
            {
                // 在当前地图上查找所有武装穿梭机
                foreach (var shuttle in __instance.Map.listerBuildings.AllBuildingsColonistOfClass<Building_ArmedShuttleWithPocket>())
                {
                    // 检查穿梭机是否有已生成的口袋地图，并且该地图里是否有人
                    if (shuttle != null && shuttle.PocketMapGenerated && shuttle.PocketMap != null && shuttle.PocketMap.mapPawns.AnyPawnBlockingMapRemoval)
                    {
                        // 如果找到了这样的穿梭机，则阻止原方法 CheckRemoveMapNow 的执行，从而阻止地图被移除。
                        // Log.Message($"[WULA] Prevented removal of map '{__instance.Map}' because shuttle '{shuttle.Label}' still contains pawns in its pocket dimension.");
                        return false; // 返回 false 以跳过原方法的执行
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"[WULA] Error in MapParent_CheckRemoveMapNow_Patch Prefix: {ex}");
            }

            // 如果没有找到需要保护的穿梭机，则允许原方法 CheckRemoveMapNow 继续执行其正常的逻辑
            return true;
        }
    }
}