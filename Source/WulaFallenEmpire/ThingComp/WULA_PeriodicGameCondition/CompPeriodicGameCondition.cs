using RimWorld;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    public class CompPeriodicGameCondition : ThingComp
    {
        private int ticksUntilNextCondition;
        
        public CompProperties_PeriodicGameCondition Props => (CompProperties_PeriodicGameCondition)props;

        public override void Initialize(CompProperties props)
        {
            base.Initialize(props);
            ticksUntilNextCondition = GetIntervalTicks();
        }

        public override void CompTick()
        {
            base.CompTick();
            
            if (!parent.Spawned || parent.Map == null)
                return;

            ticksUntilNextCondition--;
            if (ticksUntilNextCondition <= 0)
            {
                TryMakeGameCondition();
                ticksUntilNextCondition = GetIntervalTicks();
            }
        }

        private int GetIntervalTicks()
        {
            return Mathf.RoundToInt(Props.intervalDays * 60000f); // 1天 = 60000 ticks
        }

        private int GetDurationTicks()
        {
            return Mathf.RoundToInt(Props.durationDays * 60000f);
        }

        private void TryMakeGameCondition()
        {
            Map map = parent.Map;
            if (map == null)
                return;

            // 检查是否可以触发
            if (!CanMakeGameCondition(map))
                return;

            // 创建游戏条件
            GameCondition condition = GameConditionMaker.MakeCondition(Props.gameConditionDef, GetDurationTicks());
            map.gameConditionManager.RegisterCondition(condition);

            // 发送通知
            if (Props.sendLetter)
            {
                TrySendLetter(map, condition);
            }
        }

        private bool CanMakeGameCondition(Map map)
        {
            if (Props.gameConditionDef == null)
                return false;

            GameConditionManager conditionManager = map.gameConditionManager;
            if (conditionManager == null)
                return false;

            // 检查是否被其他条件阻止
            if (Props.checkPreventIncidents)
            {
                foreach (GameCondition activeCondition in conditionManager.ActiveConditions)
                {
                    if (activeCondition.def.preventIncidents)
                    {
                        return false;
                    }
                }
            }

            // 检查是否已存在相同条件
            if (Props.checkAlreadyActive && conditionManager.ConditionIsActive(Props.gameConditionDef))
            {
                return false;
            }

            // 检查是否可以共存
            if (Props.checkCanCoexist)
            {
                foreach (GameCondition activeCondition in conditionManager.ActiveConditions)
                {
                    if (!Props.gameConditionDef.CanCoexistWith(activeCondition.def))
                    {
                        return false;
                    }
                }
            }

            // 特殊条件：检查水域是否有鱼
            if (Props.requireFish && ModsConfig.OdysseyActive)
            {
                if (map.waterBodyTracker == null || !map.waterBodyTracker.AnyBodyContainsFish)
                {
                    return false;
                }
            }

            return true;
        }

        private void TrySendLetter(Map map, GameCondition condition)
        {
            if (condition.HiddenByOtherCondition(map))
                return;

            // 检查是否有地图会受到这个条件影响
            bool anyMapAffected = false;
            foreach (Map currentMap in Find.Maps)
            {
                if (condition.CanApplyOnMap(currentMap))
                {
                    anyMapAffected = true;
                    break;
                }
            }

            if (!anyMapAffected)
                return;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref ticksUntilNextCondition, "ticksUntilNextCondition", GetIntervalTicks());
        }

        /// <summary>
        /// 手动触发一次游戏条件（用于调试或特殊事件）
        /// </summary>
        public void TriggerNow()
        {
            ticksUntilNextCondition = 0;
        }

        /// <summary>
        /// 获取下一次触发的时间信息
        /// </summary>
        public string GetNextTriggerInfo()
        {
            if (!parent.Spawned)
                return "Not spawned";

            float daysUntilNext = ticksUntilNextCondition / 60000f;
            return $"Next trigger in: {daysUntilNext:F1} days";
        }
    }
}
