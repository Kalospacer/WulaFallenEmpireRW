using System;
using System.Linq;
using Verse;

namespace WulaFallenEmpire
{
    public static class WulaMapProtectionHelper
    {
        public static bool ShouldProtectMap(Map map)
        {
            if (map == null)
            {
                return false;
            }
            try
            {
                // 检查地图上是否存在一个口袋空间已经初始化的武装穿梭机
                return map.listerThings.AllThings.OfType<Building_ArmedShuttleWithPocket>()
                    .Any(shuttle => shuttle != null && shuttle.Spawned && shuttle.PocketMapGenerated);
            }
            catch (Exception arg)
            {
                Log.Error($"[WULA] Error in WulaMapProtectionHelper.ShouldProtectMap: {arg}");
                return false;
            }
        }
    }
}