// File: Patches/ColonistBarMechPatch_Fixed.cs
using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    [HarmonyPatch(typeof(ColonistBar), "CheckRecacheEntries")]
    public static class Patch_ColonistBarMech_Fixed
    {
        [HarmonyPostfix]
        public static void Postfix(ref List<ColonistBar.Entry> ___cachedEntries)
        {
            // 安全检查：只在玩家派系存在时运行
            if (Faction.OfPlayer == null)
                return;
            
            try
            {
                // 建立机甲和驾驶员的映射关系
                var mechToPilots = new Dictionary<Pawn, List<Pawn>>();
                var pilotToMech = new Dictionary<Pawn, Pawn>();
                var mechEntries = new HashSet<Pawn>();
                
                // 只扫描玩家殖民地的地图
                foreach (var map in Find.Maps.Where(m => m.IsPlayerHome))
                {
                    foreach (var pawn in map.mapPawns.AllPawnsSpawned)
                    {
                        // 只处理玩家殖民地的机甲（Wulamechunit）
                        if (pawn is Wulamechunit mech && 
                            (mech.Faction == Faction.OfPlayer || mech.HostFaction == Faction.OfPlayer))
                        {
                            var pilotComp = mech.TryGetComp<CompMechPilotHolder>();
                            if (pilotComp != null && pilotComp.HasPilots)
                            {
                                var pilots = pilotComp.GetPilots()
                                    .Where(p => p.Faction == Faction.OfPlayer || p.HostFaction == Faction.OfPlayer)
                                    .ToList();
                                
                                if (pilots.Count > 0)
                                {
                                    mechToPilots[mech] = pilots;
                                    foreach (var pilot in pilots)
                                    {
                                        pilotToMech[pilot] = mech;
                                    }
                                }
                            }
                        }
                    }
                }
                
                // 如果没有找到有机甲驾驶员的机甲，直接返回
                if (mechToPilots.Count == 0)
                    return;
                
                // 创建新的条目列表
                var newEntries = new List<ColonistBar.Entry>();
                
                // 第一轮：处理原始条目，隐藏驾驶员
                foreach (var entry in ___cachedEntries)
                {
                    var pawn = entry.pawn;
                    if (pawn == null)
                    {
                        // 保留空条目
                        newEntries.Add(entry);
                        continue;
                    }
                    
                    // 如果是驾驶员，跳过（隐藏）
                    if (pilotToMech.ContainsKey(pawn))
                    {
                        continue;
                    }
                    
                    // 如果是机甲
                    if (mechToPilots.ContainsKey(pawn))
                    {
                        // 确保机甲还没有被添加，并且有驾驶员
                        if (!mechEntries.Contains(pawn) && mechToPilots[pawn].Count > 0)
                        {
                            newEntries.Add(entry);
                            mechEntries.Add(pawn);
                        }
                    }
                    else
                    {
                        // 普通殖民者（不属于任何机甲的驾驶员）
                        newEntries.Add(entry);
                    }
                }
                
                // 第二轮：确保有机甲驾驶员的机甲被添加到列表
                foreach (var mech in mechToPilots.Keys)
                {
                    // 如果机甲还没有被添加，且有驾驶员
                    if (!mechEntries.Contains(mech) && mechToPilots[mech].Count > 0)
                    {
                        // 需要创建一个新的Entry
                        var map = mech.MapHeld;
                        if (map != null && map.IsPlayerHome)
                        {
                            // 计算group：需要找到地图对应的group
                            int group = GetGroupForMap(map, ___cachedEntries);
                            newEntries.Add(new ColonistBar.Entry(mech, map, group));
                            mechEntries.Add(mech);
                        }
                        else if (mechToPilots[mech].Count > 0)
                        {
                            // 如果机甲不在任何地图上（可能被携带等），但仍有驾驶员
                            // 使用第一个驾驶员的地图和group作为参考
                            var pilot = mechToPilots[mech].First();
                            var pilotMap = pilot.MapHeld;
                            if (pilotMap != null && pilotMap.IsPlayerHome)
                            {
                                int group = GetGroupForMap(pilotMap, ___cachedEntries);
                                newEntries.Add(new ColonistBar.Entry(mech, pilotMap, group));
                                mechEntries.Add(mech);
                            }
                        }
                    }
                }
                
                // 替换原列表
                ___cachedEntries = newEntries;
            }
            catch (System.Exception ex)
            {
                Log.Error($"[WULA] Error in fixed ColonistBar patch: {ex}");
                // 出错时不改变原列表
            }
        }
        
        // 辅助方法：获取地图对应的group
        private static int GetGroupForMap(Map map, List<ColonistBar.Entry> originalEntries)
        {
            if (map == null)
                return 0;
                
            // 在原始条目中查找第一个属于该地图的条目，返回它的group
            var entry = originalEntries.FirstOrDefault(e => e.map == map && e.pawn != null);
            if (entry.pawn != null)
            {
                return entry.group;
            }
            
            // 如果没有找到有pawn的条目，尝试查找任何属于该地图的条目（包括空条目）
            entry = originalEntries.FirstOrDefault(e => e.map == map);
            if (entry.map != null)
            {
                return entry.group;
            }
            
            // 如果还是找不到，返回0作为默认值
            return 0;
        }
        
        // 备用方案：使用更简单的方法计算group
        private static int CalculateGroupForMap(Map map)
        {
            if (map == null)
                return 0;
                
            // 简单的计算：玩家殖民地地图的索引
            // 注意：这可能不完全准确，但通常工作
            var playerMaps = Find.Maps.Where(m => m.IsPlayerHome).ToList();
            int index = playerMaps.IndexOf(map);
            
            // 如果找不到，返回0
            if (index < 0)
                return 0;
                
            return index;
        }
    }
}
