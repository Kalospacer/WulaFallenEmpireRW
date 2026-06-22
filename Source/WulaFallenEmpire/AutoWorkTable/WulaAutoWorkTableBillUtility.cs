using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire.AutoWorkTable
{
    internal static class WulaAutoWorkTableBillUtility
    {
        private static readonly MethodInfo TryFindBestBillIngredientsInSetMethod =
            typeof(WorkGiver_DoBill).GetMethod(
                "TryFindBestBillIngredientsInSet",
                BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new[] { typeof(List<Thing>), typeof(Bill), typeof(List<ThingCount>), typeof(IntVec3), typeof(bool), typeof(List<IngredientCount>) },
                null);

        public static bool TryAutoStartBillFromInnerContainerAndBeacons(Building_WulaAutoWorkTable table)
        {
            if (table == null || !table.Spawned || table.Map == null || !table.CanWork() || table.ActiveBill != null || table.BillStack == null)
            {
                return false;
            }

            for (int i = 0; i < table.BillStack.Count; i++)
            {
                if (!(table.BillStack[i] is Bill_Autonomous autoBill) || !autoBill.ShouldDoNow())
                {
                    continue;
                }

                List<Thing> availableThings = GetAvailableThingsForAutoStart(table);
                List<ThingCount> chosen = new List<ThingCount>();
                if (!TryFindBestBillIngredientsInSetForAutoStart(availableThings, autoBill, chosen, table.InteractionCell))
                {
                    continue;
                }

                ConsumeSelectedBeaconIngredients(table, chosen);
                if (!AllChosenIngredientsInsideTable(table, chosen))
                {
                    continue;
                }

                autoBill.Notify_DoBillStarted(null);
                autoBill.Notify_BillWorkFinished(null);
                return true;
            }

            return false;
        }

        private static void ConsumeSelectedBeaconIngredients(Building_WulaAutoWorkTable table, List<ThingCount> chosenIngThings)
        {
            HashSet<IntVec3> beaconCells = GetPoweredBeaconCells(table.Map);
            if (beaconCells.Count == 0)
            {
                return;
            }

            for (int i = chosenIngThings.Count - 1; i >= 0; i--)
            {
                Thing thing = chosenIngThings[i].Thing;
                if (thing == null || thing.holdingOwner == table.innerContainer)
                {
                    continue;
                }

                if (!thing.Spawned || !beaconCells.Contains(thing.Position))
                {
                    continue;
                }

                int moveCount = Mathf.Min(chosenIngThings[i].Count, thing.stackCount);
                if (moveCount <= 0)
                {
                    continue;
                }

                Thing split = thing.SplitOff(moveCount);
                if (split == null || !table.innerContainer.TryAdd(split))
                {
                    split?.Destroy();
                    continue;
                }

                int remaining = chosenIngThings[i].Count - moveCount;
                if (remaining > 0)
                {
                    chosenIngThings[i] = new ThingCount(thing, remaining);
                }
                else
                {
                    chosenIngThings.RemoveAt(i);
                }
            }
        }

        private static List<Thing> GetAvailableThingsForAutoStart(Building_WulaAutoWorkTable table)
        {
            HashSet<Thing> seen = new HashSet<Thing>();
            List<Thing> result = new List<Thing>();

            for (int i = 0; i < table.innerContainer.Count; i++)
            {
                Thing thing = table.innerContainer[i];
                if (thing != null && seen.Add(thing))
                {
                    result.Add(thing);
                }
            }

            Faction playerFaction = Faction.OfPlayer;
            foreach (Building_OrbitalTradeBeacon beacon in Building_OrbitalTradeBeacon.AllPowered(table.Map))
            {
                foreach (IntVec3 cell in beacon.TradeableCells)
                {
                    List<Thing> things = cell.GetThingList(table.Map);
                    for (int i = 0; i < things.Count; i++)
                    {
                        Thing thing = things[i];
                        // 跳过被玩家禁用（forbid）的信标物资，保留 forbid 语义。
                        if (thing != null && !thing.IsForbidden(playerFaction) && seen.Add(thing))
                        {
                            result.Add(thing);
                        }
                    }
                }
            }

            return result;
        }

        private static bool TryFindBestBillIngredientsInSetForAutoStart(List<Thing> availableThings, Bill bill, List<ThingCount> chosen, IntVec3 rootCell)
        {
            if (TryFindBestBillIngredientsInSetMethod == null)
            {
                return false;
            }

            object result = TryFindBestBillIngredientsInSetMethod.Invoke(null, new object[]
            {
                availableThings,
                bill,
                chosen,
                rootCell,
                false,
                null
            });

            return result is bool success && success;
        }

        private static bool AllChosenIngredientsInsideTable(Building_WulaAutoWorkTable table, List<ThingCount> chosenIngThings)
        {
            for (int i = 0; i < chosenIngThings.Count; i++)
            {
                Thing thing = chosenIngThings[i].Thing;
                if (thing == null || thing.holdingOwner != table.innerContainer)
                {
                    return false;
                }
            }

            return true;
        }

        private static HashSet<IntVec3> GetPoweredBeaconCells(Map map)
        {
            HashSet<IntVec3> result = new HashSet<IntVec3>();
            foreach (Building_OrbitalTradeBeacon beacon in Building_OrbitalTradeBeacon.AllPowered(map))
            {
                foreach (IntVec3 cell in beacon.TradeableCells)
                {
                    result.Add(cell);
                }
            }

            return result;
        }
    }
}
