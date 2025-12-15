using HarmonyLib;
using RimWorld.Planet;
using System.Linq;
using Verse;
using WulaFallenEmpire;

namespace WulaFallenEmpire
{
    [HarmonyPatch(typeof(MapParent), "CheckRemoveMapNow")]
    public static class MapParent_CheckRemoveMapNow_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(MapParent __instance)
        {
            // 如果该 MapParent 没有地图，则直接放行
            if (!__instance.HasMap)
            {
                return true;
            }

            try
            {
                // 检查是否有活跃的观察者正在监测这个地图
                bool isBeingObserved = Building_MapObserver.activeObservers
                    .Any(observer => observer.IsObservingMap(__instance));

                if (isBeingObserved)
                {
                    // 如果地图正在被监测，阻止地图被移除
                    WulaLog.Debug($"[MapObserver] 阻止地图移除: {__instance.Label} 正在被监测");
                    return false;
                }

                // 原有的穿梭机检查逻辑（保留你的原有功能）
                foreach (var shuttle in __instance.Map.listerBuildings.AllBuildingsColonistOfClass<Building_ArmedShuttleWithPocket>())
                {
                    if (shuttle != null && shuttle.PocketMapGenerated && shuttle.PocketMap != null && shuttle.PocketMap.mapPawns.AnyPawnBlockingMapRemoval)
                    {
                        WulaLog.Debug($"[WULA] 阻止地图移除: 穿梭机 '{shuttle.Label}' 的口袋维度中仍有生物");
                        return false;
                    }
                }
            }
            catch (System.Exception ex)
            {
                WulaLog.Debug($"[MapObserver] MapParent_CheckRemoveMapNow_Patch 错误: {ex}");
            }

            // 如果没有找到需要保护的情况，允许原方法继续执行
            return true;
        }
    }
}
