// Building_GlobalWorkTable.cs (修改版本)
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;
using UnityEngine;

namespace WulaFallenEmpire
{
    public class Building_GlobalWorkTable : Building_WorkTable, IThingHolder
    {
        public GlobalProductionOrderStack globalOrderStack;
        public ThingOwner innerContainer; // 用于存储待上传的原材料
        
        private CompPowerTrader powerComp;
        private CompBreakdownable breakdownableComp;
        private int lastProcessTick = -1;
        private const int ProcessInterval = 1;

        // 材质映射定义
        private static readonly Dictionary<StuffCategoryDef, ThingDef> StuffCategoryMapping = new Dictionary<StuffCategoryDef, ThingDef>
        {
            { StuffCategoryDefOf.Metallic, ThingDefOf.Plasteel },     // 金属类 -> 玻璃钢
            { StuffCategoryDefOf.Fabric, ThingDefOf_WULA.Hyperweave }      // 布革类 -> 超织物
        };

        public Building_GlobalWorkTable()
        {
            globalOrderStack = new GlobalProductionOrderStack(this);
            innerContainer = new ThingOwner<Thing>(this, false);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref globalOrderStack, "globalOrderStack", this);
            Scribe_Deep.Look(ref innerContainer, "innerContainer", this);
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            powerComp = GetComp<CompPowerTrader>();
            breakdownableComp = GetComp<CompBreakdownable>();
        }

        protected override void Tick()
        {
            base.Tick();

            if (Find.TickManager.TicksGame % 60 == 0 &&
                Find.TickManager.TicksGame != lastProcessTick)
            {
                lastProcessTick = Find.TickManager.TicksGame;

                if (CurrentlyUsableForGlobalBills())
                {
                    TryAutoGatherFromBeaconsAndContainer();
                    globalOrderStack.ProcessOrders();
                }
            }
        }

        internal void TryAutoGatherFromBeaconsAndContainer()
        {
            var order = globalOrderStack?.orders?.FirstOrDefault(o =>
                o != null &&
                !o.paused &&
                o.state == GlobalProductionOrder.ProductionState.Gathering);
            if (order == null) return;

            var storage = Find.World.GetComponent<GlobalStorageWorldComponent>();
            if (storage == null) return;

            Dictionary<ThingDef, int> required = GetRequiredMaterialsForOrder(order);
            if (required.Count == 0) return;

            bool changed = false;
            foreach (var kvp in required)
            {
                ThingDef thingDef = kvp.Key;
                int need = kvp.Value;
                if (need <= 0) continue;

                int inCloud = storage.GetInputStorageCount(thingDef);
                int missing = need - inCloud;
                if (missing <= 0) continue;

                int uploadedFromBeacons = UploadFromPoweredTradeBeacons(storage, thingDef, missing);
                if (uploadedFromBeacons > 0)
                {
                    changed = true;
                    missing -= uploadedFromBeacons;
                }

                if (missing <= 0) continue;

                int uploadedFromContainer = UploadFromInnerContainer(storage, thingDef, missing);
                if (uploadedFromContainer > 0)
                {
                    changed = true;
                }
            }

            if (changed)
            {
                order.UpdateState();
            }
        }

        internal Dictionary<ThingDef, int> GetRequiredMaterialsForOrder(GlobalProductionOrder order)
        {
            var required = order.GetProductCostList();
            if (required.Count > 0) return required;

            required = new Dictionary<ThingDef, int>();
            if (order.recipe?.ingredients == null) return required;

            foreach (var ingredient in order.recipe.ingredients)
            {
                ThingDef def = ingredient.filter?.AllowedThingDefs?.FirstOrDefault();
                if (def == null) continue;

                int count = ingredient.CountRequiredOfFor(def, order.recipe);
                if (count <= 0) continue;

                if (required.ContainsKey(def)) required[def] += count;
                else required[def] = count;
            }

            return required;
        }

        internal int UploadFromInnerContainer(GlobalStorageWorldComponent storage, ThingDef def, int count)
        {
            if (count <= 0) return 0;

            int remaining = count;
            int uploaded = 0;

            while (remaining > 0)
            {
                Thing thing = innerContainer?.FirstOrDefault(t => t.def == def);
                if (thing == null) break;

                int take = Mathf.Min(thing.stackCount, remaining);
                Thing split = thing.SplitOff(take);
                split.Destroy(DestroyMode.Vanish);

                storage.AddToInputStorage(def, take);

                uploaded += take;
                remaining -= take;
            }

            return uploaded;
        }

        internal int UploadFromPoweredTradeBeacons(GlobalStorageWorldComponent storage, ThingDef def, int count)
        {
            if (count <= 0) return 0;
            if (Map == null) return 0;

            int remaining = count;
            int uploaded = 0;

            foreach (var beacon in Building_OrbitalTradeBeacon.AllPowered(Map))
            {
                foreach (var cell in beacon.TradeableCells)
                {
                    if (remaining <= 0) break;

                    List<Thing> things = cell.GetThingList(Map);
                    for (int i = things.Count - 1; i >= 0; i--)
                    {
                        if (remaining <= 0) break;

                        Thing t = things[i];
                        if (t?.def != def) continue;

                        int take = Mathf.Min(t.stackCount, remaining);
                        Thing split = t.SplitOff(take);
                        split.Destroy(DestroyMode.Vanish);

                        storage.AddToInputStorage(def, take);
                        uploaded += take;
                        remaining -= take;
                    }
                }

                if (remaining <= 0) break;
            }

            return uploaded;
        }

        public bool CurrentlyUsableForGlobalBills()
        {
            if (powerComp != null && !powerComp.PowerOn)
                return false;
                
            if (breakdownableComp != null && breakdownableComp.BrokenDown)
                return false;
                
            return true;
        }

        // 新增：获取空投扩展参数
        public GlobalWorkTableAirdropExtension AirdropExtension => 
            def.GetModExtension<GlobalWorkTableAirdropExtension>();

        // 修改：在 GetGizmos 方法中添加白银转移按钮
        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo g in base.GetGizmos())
            {
                yield return g;
            }

            yield return new Command_Action
            {
                action = OpenGlobalStorageTransferDialog,
                defaultLabel = "WULA_AccessGlobalStorage".Translate(),
                defaultDesc = "WULA_AccessGlobalStorageDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Trade", true),
            };

            // 白银转移按钮 - 检查输入端是否有白银
            var globalStorage = Find.World.GetComponent<GlobalStorageWorldComponent>();
            int silverAmount = globalStorage?.GetInputStorageCount(ThingDefOf.Silver) ?? 0;
            bool hasSilver = silverAmount > 0;
            if (hasSilver)
            {
                yield return new Command_Action
                {
                    action = TransferSilverToOutput,
                    defaultLabel = "WULA_TransferSilver".Translate(),
                    defaultDesc = "WULA_TransferSilverDesc".Translate(silverAmount),
                    icon = ContentFinder<Texture2D>.Get("Wula/UI/Commands/WULA_SilverTransfer"),
                };
            }
            // 原有的空投按钮逻辑保持不变
            bool hasOutputItems = globalStorage != null && globalStorage.outputStorage.Any(kvp => kvp.Value > 0);
            bool hasFactoryFlyOver = HasFactoryFacilityFlyOver();
            if (hasOutputItems && hasFactoryFlyOver)
            {
                yield return new Command_Action
                {
                    action = StartAirdropTargeting,
                    defaultLabel = "WULA_AirdropProducts".Translate(),
                    defaultDesc = "WULA_AirdropProductsDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("Wula/UI/Commands/WULA_AirdropProducts"),
                };
            }
            else if (hasOutputItems && !hasFactoryFlyOver)
            {
                yield return new Command_Action
                {
                    action = () => Messages.Message("WULA_NoFactoryFlyOver".Translate(), MessageTypeDefOf.RejectInput),
                    defaultLabel = "WULA_AirdropProducts".Translate(),
                    defaultDesc = "WULA_NoFactoryFlyOverDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("Wula/UI/Commands/WULA_AirdropProducts"),
                };
            }
        }

        private void OpenGlobalStorageTransferDialog()
        {
            if (Map == null)
                return;

            if (!Building_OrbitalTradeBeacon.AllPowered(Map).Any())
            {
                Messages.Message("WULA_NoPoweredTradeBeacon".Translate(), this, MessageTypeDefOf.RejectInput);
                return;
            }

            Pawn negotiator = Map.mapPawns?.FreeColonistsSpawned?.FirstOrDefault();
            if (negotiator == null)
            {
                Messages.Message("WULA_NoNegotiator".Translate(), this, MessageTypeDefOf.RejectInput);
                return;
            }

            Find.WindowStack.Add(new Dialog_GlobalStorageTransfer(this, negotiator));
        }

        // 新增：将输入端白银转移到输出端的方法
        private void TransferSilverToOutput()
        {
            var globalStorage = Find.World.GetComponent<GlobalStorageWorldComponent>();
            if (globalStorage == null)
            {
                Messages.Message("WULA_NoGlobalStorage".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }
            int silverAmount = globalStorage.GetInputStorageCount(ThingDefOf.Silver);

            if (silverAmount <= 0)
            {
                Messages.Message("WULA_NoSilverToTransfer".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }
            // 确认对话框
            Find.WindowStack.Add(new Dialog_MessageBox(
                "WULA_ConfirmTransferSilver".Translate(silverAmount),
                "Confirm".Translate(),
                () => ExecuteSilverTransfer(globalStorage, silverAmount),
                "Cancel".Translate(),
                null,
                "WULA_TransferSilver".Translate(),
                false,
                null,
                null
            ));
        }
        // 新增：执行白银转移
        private void ExecuteSilverTransfer(GlobalStorageWorldComponent globalStorage, int silverAmount)
        {
            try
            {
                // 从输入端移除白银
                if (globalStorage.RemoveFromInputStorage(ThingDefOf.Silver, silverAmount))
                {
                    // 添加到输出端
                    globalStorage.AddToOutputStorage(ThingDefOf.Silver, silverAmount);

                    // 显示成功消息
                    Messages.Message("WULA_SilverTransferred".Translate(silverAmount), MessageTypeDefOf.PositiveEvent);

                    Log.Message($"[WULA] Transferred {silverAmount} silver from input to output storage");
                }
                else
                {
                    Messages.Message("WULA_TransferFailed".Translate(), MessageTypeDefOf.RejectInput);
                    Log.Error("[WULA] Failed to remove silver from input storage during transfer");
                }
            }
            catch (System.Exception ex)
            {
                Messages.Message("WULA_TransferError".Translate(), MessageTypeDefOf.RejectInput);
                Log.Error($"[WULA] Error during silver transfer: {ex}");
            }
        }

        // 新增：检查是否有拥有FactoryFacility设施的飞行器
        private bool HasFactoryFacilityFlyOver()
        {
            // 系统禁用，但是保留代码
            return true;
            //Map map = Map;
            //if (map == null) return false;

            //try
            //{
            //    // 检查所有FlyOver类型的物体
            //    var allFlyOvers = new List<Thing>();
            //    var dynamicObjects = map.dynamicDrawManager.DrawThings;
            //    foreach (var thing in dynamicObjects)
            //    {
            //        if (thing is FlyOver)
            //        {
            //            allFlyOvers.Add(thing);
            //        }
            //    }

            //    foreach (var thing in allFlyOvers)
            //    {
            //        if (thing is FlyOver flyOver && !flyOver.Destroyed)
            //        {
            //            // 检查设施
            //            var facilitiesComp = flyOver.GetComp<CompFlyOverFacilities>();
            //            if (facilitiesComp == null)
            //            {
            //                continue;
            //            }
                        
            //            if (facilitiesComp.HasFacility("FactoryFacility"))
            //            {
            //                return true;
            //            }
            //        }
            //    }

            //    return false;
            //}
            //catch (System.Exception ex)
            //{
            //    Log.Error($"[FactoryFacility Check] Error in HasFactoryFacilityFlyOver: {ex}");
            //    return false;
            //}
        }

        // 新增：开始空投目标选择
        private void StartAirdropTargeting()
        {
            // 检查是否有输出物品
            var globalStorage = Find.World.GetComponent<GlobalStorageWorldComponent>();
            if (globalStorage == null || !globalStorage.outputStorage.Any(kvp => kvp.Value > 0))
            {
                Messages.Message("WULA_NoProductsToAirdrop".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            // 检查是否有工厂设施的飞行器
            if (!HasFactoryFacilityFlyOver())
            {
                Messages.Message("WULA_NoFactoryFlyOver".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            // 启动目标选择
            Find.Targeter.BeginTargeting(new TargetingParameters
            {
                canTargetLocations = true,
                canTargetPawns = false,
                canTargetBuildings = false,
                canTargetItems = false
            }, OnAirdropTargetSelected, null, OnAirdropTargetingCancelled);
        }

        private void OnAirdropTargetSelected(LocalTargetInfo target)
        {
            ExecuteAirdrop(target.Cell);
        }

        private void OnAirdropTargetingCancelled()
        {
            // 目标选择取消，不做任何操作
        }

        // 新增：执行空投逻辑
        private void ExecuteAirdrop(IntVec3 targetCell)
        {
            var globalStorage = Find.World.GetComponent<GlobalStorageWorldComponent>();
            if (globalStorage == null)
                return;

            // 再次检查是否有工厂设施的飞行器
            if (!HasFactoryFacilityFlyOver())
            {
                Messages.Message("WULA_NoFactoryFlyOver".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            // 获取空投参数
            var airdropExt = AirdropExtension;
            float maxRange = airdropExt?.maxRange ?? 50f;
            float randomRange = airdropExt?.randomRange ?? 15f;
            int minPods = airdropExt?.minPods ?? 1;
            int maxPods = airdropExt?.maxPods ?? 10;

            // 检查目标距离
            if (targetCell.DistanceTo(Position) > maxRange)
            {
                Messages.Message("WULA_AirdropTargetTooFar".Translate(maxRange), MessageTypeDefOf.RejectInput);
                return;
            }

            // 查找有效的落点
            List<IntVec3> validDropSpots = FindValidDropSpots(targetCell, randomRange, maxPods);
            
            if (validDropSpots.Count == 0)
            {
                Messages.Message("WULA_NoValidDropSpots".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            // 计算实际空投舱数量
            int actualPodCount = Mathf.Clamp(validDropSpots.Count, minPods, maxPods);
            
            // 分配物品到空投舱
            List<List<Thing>> podContents = DistributeItemsToPods(globalStorage, actualPodCount);
            
            if (podContents.Count == 0)
            {
                Messages.Message("WULA_FailedToDistributeItems".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            // 生成空投舱
            int successfulDrops = 0;
            for (int i = 0; i < Mathf.Min(actualPodCount, podContents.Count); i++)
            {
                if (CreateDropPod(validDropSpots[i], podContents[i]))
                {
                    successfulDrops++;
                }
            }

            Messages.Message("WULA_AirdropSuccessful".Translate(successfulDrops), MessageTypeDefOf.PositiveEvent);
        }

        // 新增：查找有效落点
        private List<IntVec3> FindValidDropSpots(IntVec3 center, float radius, int maxSpots)
        {
            List<IntVec3> validSpots = new List<IntVec3>();
            Map map = Map;

            // 在指定半径内搜索有效格子
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, radius, true))
            {
                if (!cell.IsValid || !cell.InBounds(map))
                    continue;

                // 检查是否为厚岩顶
                if (map.roofGrid.RoofAt(cell)?.isThickRoof ?? false)
                    continue;

                // 检查是否可以放置空投舱
                if (DropCellFinder.IsGoodDropSpot(cell, map, false, true))
                {
                    validSpots.Add(cell);
                    
                    if (validSpots.Count >= maxSpots)
                        break;
                }
            }

            // 如果有效格子太少，放宽条件（但仍然排除厚岩顶）
            if (validSpots.Count < 3)
            {
                foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, radius, true))
                {
                    if (!cell.IsValid || !cell.InBounds(map))
                        continue;

                    if (map.roofGrid.RoofAt(cell)?.isThickRoof ?? false)
                        continue;

                    if (!validSpots.Contains(cell) && cell.Standable(map))
                    {
                        validSpots.Add(cell);

                        if (validSpots.Count >= maxSpots)
                            break;
                    }
                }
            }

            return validSpots;
        }
        // 在 Building_GlobalWorkTable.cs 中修改 DistributeItemsToPods 方法
        private List<List<Thing>> DistributeItemsToPods(GlobalStorageWorldComponent storage, int podCount)
        {
            List<List<Thing>> podContents = new List<List<Thing>>();
            // 初始化空投舱内容列表
            for (int i = 0; i < podCount; i++)
            {
                podContents.Add(new List<Thing>());
            }
            // 获取所有输出物品并转换为Thing列表
            List<Thing> allItems = new List<Thing>();

            // 首先处理机械体，因为需要特殊处理
            foreach (var kvp in storage.outputStorage.ToList())
            {
                if (kvp.Value <= 0) continue;
                ThingDef thingDef = kvp.Key;
                int remainingCount = kvp.Value;
                // 如果是Pawn，需要特殊处理
                if (thingDef.race != null)
                {
                    // 对于Pawn，每个单独生成
                    for (int i = 0; i < remainingCount; i++)
                    {
                        PawnKindDef randomPawnKind = GetRandomPawnKindForType(thingDef);
                        if (randomPawnKind != null)
                        {
                            try
                            {
                                Pawn pawn = PawnGenerator.GeneratePawn(randomPawnKind, Faction.OfPlayer);
                                // 确保Pawn处于活跃状态
                                if (pawn != null)
                                {
                                    // 设置Pawn为可用的状态
                                    pawn.health.Reset();
                                    pawn.drafter = new Pawn_DraftController(pawn);

                                    allItems.Add(pawn);
                                }
                                else
                                {
                                    Log.Error("[Airdrop] Generated pawn is null");
                                }
                            }
                            catch (System.Exception ex)
                            {
                                Log.Error($"[Airdrop] Error generating pawn: {ex}");
                            }
                        }
                        else
                        {
                            Log.Error($"[Airdrop] Could not find suitable PawnKindDef for {thingDef.defName}");
                        }
                    }

                    // 立即从存储中移除已处理的机械体
                    storage.RemoveFromOutputStorage(thingDef, remainingCount);
                }
            }
            // 然后处理普通物品
            foreach (var kvp in storage.outputStorage.ToList())
            {
                if (kvp.Value <= 0) continue;
                ThingDef thingDef = kvp.Key;
                int remainingCount = kvp.Value;
                // 跳过已经处理的机械体
                if (thingDef.race != null) continue;
                Log.Message($"[Airdrop] Processing {remainingCount} items of type {thingDef.defName}");
                // 对于普通物品，按照堆叠限制分割
                while (remainingCount > 0)
                {
                    int stackSize = Mathf.Min(remainingCount, thingDef.stackLimit);
                    Thing thing = CreateThingWithMaterial(thingDef, stackSize);
                    if (thing != null)
                    {
                        allItems.Add(thing);
                        remainingCount -= stackSize;
                    }
                    else
                    {
                        Log.Error($"[Airdrop] Failed to create thing: {thingDef.defName}");
                        break;
                    }
                }

                // 从存储中移除已处理的物品
                storage.RemoveFromOutputStorage(thingDef, kvp.Value);
            }
            if (allItems.Count == 0)
            {
                return podContents;
            }
            // 平均分配物品到空投舱
            int currentPod = 0;
            foreach (Thing item in allItems)
            {
                if (item != null)
                {
                    podContents[currentPod].Add(item);
                    currentPod = (currentPod + 1) % podCount;
                }
            }
            // 记录分配结果
            for (int i = 0; i < podContents.Count; i++)
            {
                Log.Message($"[Airdrop] Pod {i} contains {podContents[i].Count} items");
            }
            return podContents;
        }

        // 新增：创建物品并应用材质规则
        private Thing CreateThingWithMaterial(ThingDef thingDef, int stackCount)
        {
            // 检查物品是否需要材质
            if (thingDef.MadeFromStuff)
            {
                // 获取物品可用的材质类别
                var stuffCategories = thingDef.stuffCategories;
                if (stuffCategories != null && stuffCategories.Count > 0)
                {
                    // 检查是否有金属类材质需求
                    var metallicCategory = stuffCategories.FirstOrDefault(sc => 
                        sc.defName == "Metallic" || sc.defName.Contains("Metallic"));
                    
                    // 检查是否有布革类材质需求  
                    var fabricCategory = stuffCategories.FirstOrDefault(sc =>
                        sc.defName == "Fabric" || sc.defName.Contains("Fabric") || 
                        sc.defName == "Leathery" || sc.defName.Contains("Leather"));

                    // 应用材质规则
                    ThingDef selectedStuff = null;
                    
                    if (metallicCategory != null)
                    {
                        // 金属类 -> 玻璃钢
                        selectedStuff = ThingDefOf.Plasteel;
                    }
                    else if (fabricCategory != null)
                    {
                        // 布革类 -> 超织物
                        selectedStuff = ThingDefOf_WULA.Hyperweave;
                    }

                    // 创建带有指定材质的物品
                    if (selectedStuff != null)
                    {
                        Thing thing = ThingMaker.MakeThing(thingDef, selectedStuff);
                        thing.stackCount = stackCount;
                        return thing;
                    }
                }
            }

            // 默认情况：创建无材质的物品
            Thing defaultThing = ThingMaker.MakeThing(thingDef);
            defaultThing.stackCount = stackCount;
            return defaultThing;
        }

        // 改进 GetRandomPawnKindForType 方法
        private PawnKindDef GetRandomPawnKindForType(ThingDef pawnType)
        {
            if (pawnType.race == null)
            {
                Log.Error($"[Airdrop] GetRandomPawnKindForType: {pawnType.defName} is not a pawn type");
                return null;
            }
            // 获取工作台的派系
            Faction workTableFaction = this.Faction;
            if (workTableFaction == null)
            {
                Log.Error($"[Airdrop] Work table has no faction");
                return null;
            }
            // 获取该种族的所有PawnKindDef
            var availableKinds = DefDatabase<PawnKindDef>.AllDefs
                .Where(kind => kind.race == pawnType)
                .ToList();
            if (availableKinds.Count == 0)
            {
                Log.Error($"[Airdrop] No PawnKindDef found for race: {pawnType.defName}");
                return null;
            }
            // 最高优先级：与工作台派系完全相同的PawnKind
            var matchingFactionKinds = availableKinds
                .Where(kind => kind.defaultFactionDef != null &&
                              kind.defaultFactionDef == workTableFaction.def)
                .ToList();
            if (matchingFactionKinds.Count > 0)
            {
                var selected = matchingFactionKinds.RandomElement();
                return selected;
            }
            // 次高优先级：玩家派系的PawnKind（如果工作台是玩家派系）
            if (workTableFaction.IsPlayer)
            {
                var playerFactionKinds = availableKinds
                    .Where(kind => kind.defaultFactionDef != null &&
                                  (kind.defaultFactionDef == FactionDefOf.PlayerColony ||
                                   kind.defaultFactionDef == FactionDefOf.PlayerTribe))
                    .ToList();
                if (playerFactionKinds.Count > 0)
                {
                    var selected = playerFactionKinds.RandomElement();
                    return selected;
                }
            }
            // 备选：没有特定派系的PawnKind
            var noFactionKinds = availableKinds
                .Where(kind => kind.defaultFactionDef == null)
                .ToList();
            if (noFactionKinds.Count > 0)
            {
                var selected = noFactionKinds.RandomElement();
                Log.Message($"[Airdrop] Selected no-faction PawnKind: {selected.defName}");
                return selected;
            }
            // 最后选择任何可用的PawnKind
            var selectedKind = availableKinds.RandomElement();
            Log.Message($"[Airdrop] Selected fallback PawnKind: {selectedKind.defName}");
            return selectedKind;
        }

        // IThingHolder 实现
        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
        }

        public ThingOwner GetDirectlyHeldThings()
        {
            return innerContainer;
        }

        // 修改 CreateDropPod 方法
        private bool CreateDropPod(IntVec3 dropCell, List<Thing> contents)
        {
            try
            {
                if (contents == null || contents.Count == 0)
                {
                    Log.Warning("[Airdrop] CreateDropPod: contents is null or empty");
                    return false;
                }
                Log.Message($"[Airdrop] Creating drop pod at {dropCell} with {contents.Count} items");
                // 检查目标单元格是否有效
                if (!dropCell.IsValid || !dropCell.InBounds(Map))
                {
                    Log.Error($"[Airdrop] Invalid drop cell: {dropCell}");
                    return false;
                }
                // 创建空投舱信息 - 使用 DropPodInfo 而不是 ActiveTransporterInfo
                ActiveTransporterInfo dropPodInfo = new ActiveTransporterInfo();
                dropPodInfo.openDelay = 180; // 3秒后打开
                dropPodInfo.leaveSlag = true;

                // 创建容器并添加物品
                ThingOwner container = new ThingOwner<Thing>();
                foreach (Thing thing in contents)
                {
                    if (thing != null)
                    {
                        if (!container.TryAdd(thing, true))
                        {
                            Log.Error($"[Airdrop] Failed to add {thing.Label} to drop pod");
                        }
                        else
                        {
                            Log.Message($"[Airdrop] Added {thing.Label} to drop pod");
                        }
                    }
                }
                if (container.Count == 0)
                {
                    Log.Warning("[Airdrop] No items were successfully added to drop pod");
                    return false;
                }
                dropPodInfo.innerContainer = container;
                // 生成空投舱
                DropPodUtility.MakeDropPodAt(dropCell, Map, dropPodInfo, Faction.OfPlayer);

                Log.Message($"[Airdrop] Successfully created drop pod at {dropCell}");
                return true;
            }
            catch (System.Exception ex)
            {
                Log.Error($"[Airdrop] Failed to create drop pod at {dropCell}: {ex}");
                return false;
            }
        }
    }
}
