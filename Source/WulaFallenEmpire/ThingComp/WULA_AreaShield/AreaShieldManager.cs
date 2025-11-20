using System.Collections.Generic;
using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public static class AreaShieldManager
    {
        private static Dictionary<Map, HashSet<ThingComp_AreaShield>> activeShieldsByMap = 
            new Dictionary<Map, HashSet<ThingComp_AreaShield>>();
        
        private static int lastUpdateTick = 0;
        private const int UPDATE_INTERVAL_TICKS = 60;

        public static IEnumerable<ThingComp_AreaShield> GetActiveShieldsForMap(Map map)
        {
            if (map == null)
                yield break;
            if (Find.TickManager.TicksGame - lastUpdateTick > UPDATE_INTERVAL_TICKS)
            {
                UpdateShieldCache();
                lastUpdateTick = Find.TickManager.TicksGame;
            }
            if (activeShieldsByMap.TryGetValue(map, out var shields))
            {
                foreach (var shield in shields)
                {
                    if (shield?.parent != null && !shield.parent.Destroyed && shield?.Active == true)
                        yield return shield;
                }
            }
        }
        private static void UpdateShieldCache()
        {
            activeShieldsByMap.Clear();
            foreach (var map in Find.Maps)
            {
                if (map == null) continue;
                var shieldSet = new HashSet<ThingComp_AreaShield>();
                foreach (var pawn in map.mapPawns.AllPawnsSpawned)
                {
                    if (pawn?.apparel == null || pawn.Destroyed)
                        continue;
                    foreach (var apparel in pawn.apparel.WornApparel)
                    {
                        if (apparel == null || apparel.Destroyed)
                            continue;
                        var shield = apparel.TryGetComp<ThingComp_AreaShield>();
                        // 修改：只有立定且激活的护盾才加入缓存
                        if (shield != null && shield.Active && !shield.IsWearerMoving)
                        {
                            shieldSet.Add(shield);
                        }
                    }
                }
                activeShieldsByMap[map] = shieldSet;
            }
        }

        public static void NotifyShieldStateChanged(ThingComp_AreaShield shield)
        {
            if (shield?.Wearer?.Map != null)
            {
                lastUpdateTick = 0;
            }
        }

        public static void Cleanup()
        {
            var mapsToRemove = new List<Map>();
            foreach (var map in activeShieldsByMap.Keys)
            {
                if (map == null || !Find.Maps.Contains(map))
                    mapsToRemove.Add(map);
            }
            
            foreach (var map in mapsToRemove)
            {
                activeShieldsByMap.Remove(map);
            }
        }
    }
}
