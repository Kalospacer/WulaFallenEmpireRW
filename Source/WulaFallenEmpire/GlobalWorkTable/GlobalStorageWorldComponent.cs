// GlobalStorageWorldComponent.cs (移除材质相关存储)
using LudeonTK;
using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using Verse;
using UnityEngine;

namespace WulaFallenEmpire
{
    public class GlobalStorageWorldComponent : WorldComponent, IThingHolder
    {
        public Dictionary<ThingDef, int> inputStorage = new Dictionary<ThingDef, int>();
        public Dictionary<ThingDef, int> outputStorage = new Dictionary<ThingDef, int>();

        public ThingOwner<Thing> inputContainer;
        public ThingOwner<Thing> outputContainer;
        
        // 存储生产订单
        public List<GlobalProductionOrder> productionOrders = new List<GlobalProductionOrder>();
        
        public GlobalStorageWorldComponent(World world) : base(world)
        {
            inputContainer = new ThingOwner<Thing>(this);
            outputContainer = new ThingOwner<Thing>(this);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            
            // 序列化输入存储
            Scribe_Collections.Look(ref inputStorage, "inputStorage", LookMode.Def, LookMode.Value);
            if (inputStorage == null) inputStorage = new Dictionary<ThingDef, int>();
            
            // 序列化输出存储  
            Scribe_Collections.Look(ref outputStorage, "outputStorage", LookMode.Def, LookMode.Value);
            if (outputStorage == null) outputStorage = new Dictionary<ThingDef, int>();

            Scribe_Deep.Look(ref inputContainer, "inputContainer", this);
            Scribe_Deep.Look(ref outputContainer, "outputContainer", this);
            if (inputContainer == null) inputContainer = new ThingOwner<Thing>(this);
            if (outputContainer == null) outputContainer = new ThingOwner<Thing>(this);
            
            // 序列化生产订单
            Scribe_Collections.Look(ref productionOrders, "productionOrders", LookMode.Deep);
            if (productionOrders == null) productionOrders = new List<GlobalProductionOrder>();

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                MigrateLegacyDictionariesToThingOwnersIfNeeded();
            }
        }

        private void MigrateLegacyDictionariesToThingOwnersIfNeeded()
        {
            // 旧存档：只有按 ThingDef 计数的字典。尽量迁移成真实 Thing（旧存档本就不含品质/耐久等信息）。
            if (inputContainer != null && inputContainer.Any) return;
            if (outputContainer != null && outputContainer.Any) return;

            if (inputStorage != null)
            {
                foreach (var kvp in inputStorage.ToList())
                {
                    if (kvp.Key == null || kvp.Value <= 0) continue;
                    AddGeneratedToContainer(inputContainer, kvp.Key, kvp.Value);
                }
            }

            if (outputStorage != null)
            {
                foreach (var kvp in outputStorage.ToList())
                {
                    if (kvp.Key == null || kvp.Value <= 0) continue;
                    AddGeneratedToContainer(outputContainer, kvp.Key, kvp.Value);
                }
            }

            inputStorage?.Clear();
            outputStorage?.Clear();
        }

        private static void AddGeneratedToContainer(ThingOwner<Thing> container, ThingDef def, int count)
        {
            if (container == null || def == null || count <= 0) return;

            int remaining = count;
            while (remaining > 0)
            {
                if (def.race != null)
                {
                    PawnKindDef pawnKind = DefDatabase<PawnKindDef>.AllDefs.FirstOrDefault(k => k.race == def);
                    if (pawnKind == null) break;

                    Pawn pawn = PawnGenerator.GeneratePawn(pawnKind, Faction.OfPlayer);
                    if (pawn == null) break;

                    container.TryAdd(pawn, false);
                    remaining -= 1;
                    continue;
                }

                int stackCount = Mathf.Min(remaining, Mathf.Max(1, def.stackLimit));
                ThingDef stuff = null;
                if (def.MadeFromStuff)
                {
                    stuff = GenStuff.DefaultStuffFor(def) ?? GenStuff.AllowedStuffsFor(def).FirstOrDefault();
                    if (stuff == null) break;
                }

                Thing thing = ThingMaker.MakeThing(def, stuff);
                if (thing == null) break;

                thing.stackCount = stackCount;
                container.TryAdd(thing, true);
                remaining -= stackCount;
            }
        }

        public ThingOwner GetDirectlyHeldThings()
        {
            return inputContainer;
        }

        public IThingHolder ParentHolder => null;

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, inputContainer);
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, outputContainer);
        }

        // 输入存储方法
        public void AddToInputStorage(ThingDef thingDef, int count)
        {
            AddGeneratedToContainer(inputContainer, thingDef, count);
        }

        public bool AddToInputStorage(Thing thing, bool canMergeWithExistingStacks = true)
        {
            if (thing == null) return false;
            if (thing.Spawned) thing.DeSpawn();
            return inputContainer.TryAdd(thing, canMergeWithExistingStacks);
        }

        public bool RemoveFromInputStorage(ThingDef thingDef, int count)
        {
            return TryConsumeFromContainer(inputContainer, thingDef, count);
        }

        public int GetInputStorageCount(ThingDef thingDef)
        {
            return CountInContainer(inputContainer, thingDef);
        }

        // 输出存储方法
        public void AddToOutputStorage(ThingDef thingDef, int count)
        {
            AddGeneratedToContainer(outputContainer, thingDef, count);
        }

        public bool AddToOutputStorage(Thing thing, bool canMergeWithExistingStacks = true)
        {
            if (thing == null) return false;
            if (thing.Spawned) thing.DeSpawn();
            return outputContainer.TryAdd(thing, canMergeWithExistingStacks);
        }

        public bool RemoveFromOutputStorage(ThingDef thingDef, int count)
        {
            return TryConsumeFromContainer(outputContainer, thingDef, count);
        }

        public int GetOutputStorageCount(ThingDef thingDef)
        {
            return CountInContainer(outputContainer, thingDef);
        }

        public int GetInputStorageTotalCount()
        {
            return inputContainer?.Sum(t => t?.stackCount ?? 0) ?? 0;
        }

        public int GetOutputStorageTotalCount()
        {
            return outputContainer?.Sum(t => t?.stackCount ?? 0) ?? 0;
        }

        private static int CountInContainer(ThingOwner<Thing> container, ThingDef def)
        {
            if (container == null || def == null) return 0;
            int count = 0;
            for (int i = 0; i < container.Count; i++)
            {
                Thing t = container[i];
                if (t != null && t.def == def)
                {
                    count += t.stackCount;
                }
            }
            return count;
        }

        private static bool TryConsumeFromContainer(ThingOwner<Thing> container, ThingDef def, int count)
        {
            if (container == null || def == null || count <= 0) return false;

            if (CountInContainer(container, def) < count) return false;

            int remaining = count;
            for (int i = container.Count - 1; i >= 0 && remaining > 0; i--)
            {
                Thing t = container[i];
                if (t == null || t.def != def) continue;

                int take = Mathf.Min(t.stackCount, remaining);
                Thing taken = t.SplitOff(take);
                if (taken.holdingOwner != null)
                {
                    taken.holdingOwner.Remove(taken);
                }
                taken.Destroy(DestroyMode.Vanish);
                remaining -= take;
            }

            return remaining <= 0;
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
                WulaLog.Debug("Added test resources to global storage");
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
                    for (int i = globalStorage.outputContainer.Count - 1; i >= 0; i--)
                    {
                        Thing thing = globalStorage.outputContainer[i];
                        if (thing == null) continue;

                        globalStorage.outputContainer.Remove(thing);
                        GenPlace.TryPlaceThing(thing, workTable.Position, workTable.Map, ThingPlaceMode.Near);
                    }
                    WulaLog.Debug("Spawned all output products");
                }
            }
        }
    }
}
