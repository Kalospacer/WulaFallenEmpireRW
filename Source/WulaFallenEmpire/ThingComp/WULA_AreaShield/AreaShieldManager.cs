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
            if (Find.TickManager.TicksGame - lastUpdateTick > UPDATE_INTERVAL_TICKS)
            {
                UpdateShieldCache();
                lastUpdateTick = Find.TickManager.TicksGame;
            }

            if (activeShieldsByMap.TryGetValue(map, out var shields))
            {
                foreach (var shield in shields)
                {
                    if (shield?.Active == true)
                        yield return shield;
                }
            }
        }

        private static void UpdateShieldCache()
        {
            activeShieldsByMap.Clear();

            foreach (var map in Find.Maps)
            {
                var shieldSet = new HashSet<ThingComp_AreaShield>();
                
                foreach (var pawn in map.mapPawns.AllPawnsSpawned)
                {
                    if (pawn.apparel != null)
                    {
                        foreach (var apparel in pawn.apparel.WornApparel)
                        {
                            // 同时支持普通护盾和反弹护盾
                            var shield = apparel.TryGetComp<ThingComp_AreaShield>();
                            if (shield != null && shield.Active)
                            {
                                shieldSet.Add(shield);
                            }
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
