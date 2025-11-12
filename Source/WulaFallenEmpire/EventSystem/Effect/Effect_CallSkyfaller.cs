using System.Collections.Generic;
using Verse;
using RimWorld;
using UnityEngine;

namespace WulaFallenEmpire
{
    public class Effect_CallSkyfaller : EffectBase
    {
        public ThingDef skyfallerDef;
        public int delayTicks = 120;
        public bool checkClearance = true;
        public int clearanceRadius = 3;
        public string letterLabel;
        public string letterText;
        public LetterDef letterDef;

        public override void Execute(Window dialog = null)
        {
            Map currentMap = Find.CurrentMap;
            if (currentMap == null)
            {
                Log.Error("[WulaFallenEmpire] Effect_CallSkyfaller cannot execute without a current map.");
                return;
            }

            if (skyfallerDef == null)
            {
                Log.Error("[WulaFallenEmpire] Effect_CallSkyfaller has a null skyfallerDef.");
                return;
            }

            // 寻找合适的掉落点
            IntVec3 dropCenter;
            if (checkClearance)
            {
                dropCenter = FindDropSpotWithClearance(currentMap, clearanceRadius);
            }
            else
            {
                dropCenter = DropCellFinder.RandomDropSpot(currentMap);
            }

            if (!dropCenter.IsValid)
            {
                Log.Error("[WulaFallenEmpire] Effect_CallSkyfaller could not find a valid drop spot.");
                return;
            }

            // 创建延时召唤
            CallSkyfallerDelayed(dropCenter, currentMap);

            // 发送通知信件
            if (!string.IsNullOrEmpty(letterLabel) && !string.IsNullOrEmpty(letterText))
            {
                Find.LetterStack.ReceiveLetter(letterLabel, letterText, letterDef ?? LetterDefOf.NeutralEvent);
            }

            Log.Message($"[WulaFallenEmpire] Scheduled skyfaller '{skyfallerDef.defName}' at {dropCenter} with {delayTicks} ticks delay");
        }

        private IntVec3 FindDropSpotWithClearance(Map map, int radius)
        {
            // 优先在殖民地附近寻找
            IntVec3 result;
            if (RCellFinder.TryFindRandomCellNearTheCenterOfTheMapWith(
                (IntVec3 c) => IsValidDropSpotWithClearance(c, map, radius) && map.reachability.CanReachColony(c),
                map, out result))
            {
                return result;
            }

            // 如果找不到，放宽条件
            if (CellFinder.TryFindRandomCellNear(map.Center, map, Mathf.Max(map.Size.x / 4, 10), 
                (IntVec3 c) => IsValidDropSpotWithClearance(c, map, radius), out result))
            {
                return result;
            }

            // 最后尝试任何有效位置
            if (CellFinder.TryFindRandomCellNear(map.Center, map, map.Size.x / 2, 
                (IntVec3 c) => IsValidDropSpotWithClearance(c, map, radius), out result))
            {
                return result;
            }

            return IntVec3.Invalid;
        }

        private bool IsValidDropSpotWithClearance(IntVec3 center, Map map, int radius)
        {
            // 检查中心点是否有效
            if (!center.IsValid || !center.InBounds(map) || !center.Standable(map) || center.Fogged(map))
                return false;

            // 检查指定半径内的所有单元格
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, radius, true))
            {
                if (!cell.InBounds(map) || !cell.Walkable(map) || cell.Fogged(map))
                    return false;

                // 检查是否有建筑物阻挡
                Building building = cell.GetEdifice(map);
                if (building != null && building.def.passability == Traversability.Impassable)
                    return false;

                // 检查是否有屋顶（可选，根据需求调整）
                if (cell.Roofed(map))
                    return false;
            }

            return true;
        }

        private void CallSkyfallerDelayed(IntVec3 targetCell, Map map)
        {
            // 获取或创建延时组件
            var delayedComponent = map.GetComponent<MapComponent_SkyfallerDelayed>();
            if (delayedComponent == null)
            {
                delayedComponent = new MapComponent_SkyfallerDelayed(map);
                map.components.Add(delayedComponent);
            }

            // 安排延时召唤
            delayedComponent.ScheduleSkyfaller(skyfallerDef, targetCell, delayTicks);
        }
    }
}
