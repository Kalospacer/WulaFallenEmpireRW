using System;
using System.Collections.Generic;
using RimWorld.Planet;
using Verse;

namespace WulaFallenEmpire
{
    public class DelayedActionManager : WorldComponent
    {
        private class DelayedAction
        {
            public int TicksRemaining;
            public Action Action;
        }

        private List<DelayedAction> actions = new List<DelayedAction>();

        public DelayedActionManager(World world) : base(world)
        {
        }

        public void AddAction(Action action, int delayTicks)
        {
            if (action == null || delayTicks <= 0)
            {
                return;
            }
            actions.Add(new DelayedAction { TicksRemaining = delayTicks, Action = action });
        }

        public override void WorldComponentTick()
        {
            base.WorldComponentTick();
            for (int i = actions.Count - 1; i >= 0; i--)
            {
                DelayedAction delayedAction = actions[i];
                delayedAction.TicksRemaining--;
                if (delayedAction.TicksRemaining <= 0)
                {
                    try
                    {
                        delayedAction.Action();
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"[WulaFallenEmpire] Error executing delayed action: {ex}");
                    }
                    actions.RemoveAt(i);
                }
            }
        }
        
        public override void ExposeData()
        {
            // This simple manager does not save scheduled actions across game loads.
            // If you need actions to persist, you would need a more complex system
            // to serialize the action's target and parameters.
            base.ExposeData();
        }
    }
}