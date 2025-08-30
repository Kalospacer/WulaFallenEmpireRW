using RimWorld;
using Verse;
using System.Collections.Generic;

namespace WulaFallenEmpire.Quests
{
    public class GameComponent_WulaQuests : GameComponent
    {
        // key: 任务物品, value: 对应的回收穿梭机groupID
        private Dictionary<Thing, int> activeRecoveryQuests = new Dictionary<Thing, int>();

        public GameComponent_WulaQuests(Game game)
        {
        }
        
        public void RegisterRecoveryQuest(Thing item, int shuttleGroupID)
        {
            if (!activeRecoveryQuests.ContainsKey(item))
            {
                activeRecoveryQuests.Add(item, shuttleGroupID);
            }
        }
        
        public void UnregisterRecoveryQuest(Thing item)
        {
            if (activeRecoveryQuests.ContainsKey(item))
            {
                activeRecoveryQuests.Remove(item);
            }
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();

            if (Find.TickManager.TicksGame % 60 != 0) return;

            var questsToCheck = new Dictionary<Thing, int>(activeRecoveryQuests);

            foreach (var entry in questsToCheck)
            {
                var item = entry.Key;
                var shuttleGroupID = entry.Value;

                if (item == null || item.Destroyed)
                {
                    UnregisterRecoveryQuest(item);
                    continue;
                }

                if (item.Map != null && item.Map.IsPlayerHome)
                {
                    Find.SignalManager.SendSignal(new Signal("WulaFallenEmpire.Quest.RecoverItem.ItemRecoveredToHome", new NamedArgument(item, "SUBJECT")));
                }
                
                if (item.ParentHolder is CompTransporter transporter && transporter.groupID == shuttleGroupID)
                {
                    Find.SignalManager.SendSignal(new Signal("WulaFallenEmpire.Quest.RecoverItem.ItemLoadedOnShuttle", new NamedArgument(item, "SUBJECT")));
                    UnregisterRecoveryQuest(item);
                }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref activeRecoveryQuests, "activeRecoveryQuests", LookMode.Reference, LookMode.Value);
        }
    }
}