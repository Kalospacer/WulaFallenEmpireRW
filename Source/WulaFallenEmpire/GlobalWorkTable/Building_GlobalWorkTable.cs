// Building_GlobalWorkTable.cs (修复版)
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace WulaFallenEmpire
{
    public class Building_GlobalWorkTable : Building_WorkTable
    {
        public GlobalProductionOrderStack globalOrderStack;
        
        private CompPowerTrader powerComp;
        private int lastProcessTick = -1;
        private const int ProcessInterval = 60; // 每60tick处理一次

        public Building_GlobalWorkTable()
        {
            globalOrderStack = new GlobalProductionOrderStack(this);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref globalOrderStack, "globalOrderStack", this);
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            powerComp = GetComp<CompPowerTrader>();
        }

        public override void Tick()
        {
            base.Tick();
            
            // 每60tick处理一次生产订单
            if (Find.TickManager.TicksGame % ProcessInterval == 0 && 
                Find.TickManager.TicksGame != lastProcessTick)
            {
                lastProcessTick = Find.TickManager.TicksGame;
                
                if (powerComp == null || powerComp.PowerOn)
                {
                    Log.Message($"[DEBUG] Processing orders at tick {Find.TickManager.TicksGame}");
                    globalOrderStack.ProcessOrders();
                }
                else
                {
                    Log.Message("[DEBUG] No power, skipping order processing");
                }
            }
        }

        public bool CurrentlyUsableForGlobalBills()
        {
            return (powerComp == null || powerComp.PowerOn) && 
                   (GetComp<CompBreakdownable>() == null || !GetComp<CompBreakdownable>().BrokenDown);
        }
    }
}
