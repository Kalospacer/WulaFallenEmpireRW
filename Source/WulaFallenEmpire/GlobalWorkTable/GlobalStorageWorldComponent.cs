using LudeonTK;
using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    public class GlobalStorageWorldComponent : WorldComponent
    {
        public Dictionary<ThingDef, int> inputStorage = new Dictionary<ThingDef, int>();
        public Dictionary<ThingDef, int> outputStorage = new Dictionary<ThingDef, int>();
        
        // 存储生产订单
        public List<GlobalProductionOrder> productionOrders = new List<GlobalProductionOrder>();
        
        public GlobalStorageWorldComponent(World world) : base(world) { }

        public override void ExposeData()
        {
            base.ExposeData();
            
            // 序列化输入存储
            Scribe_Collections.Look(ref inputStorage, "inputStorage", LookMode.Def, LookMode.Value);
            if (inputStorage == null) inputStorage = new Dictionary<ThingDef, int>();
            
            // 序列化输出存储  
            Scribe_Collections.Look(ref outputStorage, "outputStorage", LookMode.Def, LookMode.Value);
            if (outputStorage == null) outputStorage = new Dictionary<ThingDef, int>();
            
            // 序列化生产订单
            Scribe_Collections.Look(ref productionOrders, "productionOrders", LookMode.Deep);
            if (productionOrders == null) productionOrders = new List<GlobalProductionOrder>();
        }

        // 输入存储方法
        public void AddToInputStorage(ThingDef thingDef, int count)
        {
            if (inputStorage.ContainsKey(thingDef))
                inputStorage[thingDef] += count;
            else
                inputStorage[thingDef] = count;
        }

        public bool RemoveFromInputStorage(ThingDef thingDef, int count)
        {
            if (inputStorage.ContainsKey(thingDef) && inputStorage[thingDef] >= count)
            {
                inputStorage[thingDef] -= count;
                if (inputStorage[thingDef] <= 0)
                    inputStorage.Remove(thingDef);
                return true;
            }
            return false;
        }

        public int GetInputStorageCount(ThingDef thingDef)
        {
            return inputStorage.ContainsKey(thingDef) ? inputStorage[thingDef] : 0;
        }

        // 输出存储方法
        public void AddToOutputStorage(ThingDef thingDef, int count)
        {
            if (outputStorage.ContainsKey(thingDef))
                outputStorage[thingDef] += count;
            else
                outputStorage[thingDef] = count;
        }

        public bool RemoveFromOutputStorage(ThingDef thingDef, int count)
        {
            if (outputStorage.ContainsKey(thingDef) && outputStorage[thingDef] >= count)
            {
                outputStorage[thingDef] -= count;
                if (outputStorage[thingDef] <= 0)
                    outputStorage.Remove(thingDef);
                return true;
            }
            return false;
        }

        public int GetOutputStorageCount(ThingDef thingDef)
        {
            return outputStorage.ContainsKey(thingDef) ? outputStorage[thingDef] : 0;
        }

        // 生产订单管理
        public void AddProductionOrder(GlobalProductionOrder order)
        {
            productionOrders.Add(order);
        }

        public void RemoveProductionOrder(GlobalProductionOrder order)
        {
            productionOrders.Remove(order);
        }

        // 添加调试方法
        [DebugAction("WULA", "Add Test Resources", actionType = DebugActionType.Action)]
        public static void DebugAddTestResources()
        {
            var globalStorage = Find.World.GetComponent<GlobalStorageWorldComponent>();
            if (globalStorage != null)
            {
                globalStorage.AddToInputStorage(ThingDefOf.Steel, 200);
                globalStorage.AddToInputStorage(ThingDefOf.ComponentIndustrial, 100);
                Log.Message("Added test resources to global storage");
            }
        }
        [DebugAction("WULA", "Spawn All Products", actionType = DebugActionType.Action)]
        public static void DebugSpawnAllProducts()
        {
            var globalStorage = Find.World.GetComponent<GlobalStorageWorldComponent>();
            if (globalStorage != null)
            {
                // 查找任意工作台来生成物品
                var workTable = Find.CurrentMap?.listerBuildings?.allBuildingsColonist?
                    .FirstOrDefault(b => b is Building_GlobalWorkTable) as Building_GlobalWorkTable;

                if (workTable != null && workTable.Spawned)
                {
                    foreach (var kvp in globalStorage.outputStorage.ToList()) // 使用ToList避免修改时枚举
                    {
                        ThingDef thingDef = kvp.Key;
                        int count = kvp.Value;

                        while (count > 0)
                        {
                            int stackSize = Mathf.Min(count, thingDef.stackLimit);
                            Thing thing = ThingMaker.MakeThing(thingDef);
                            thing.stackCount = stackSize;

                            GenPlace.TryPlaceThing(thing, workTable.Position, workTable.Map, ThingPlaceMode.Near);

                            globalStorage.RemoveFromOutputStorage(thingDef, stackSize);
                            count -= stackSize;
                        }
                    }
                    Log.Message("Spawned all output products");
                }
            }
        }
    }
}
