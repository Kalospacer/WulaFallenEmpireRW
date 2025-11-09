// Building_GlobalWorkTable.cs (修改版本)
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;
using UnityEngine;

namespace WulaFallenEmpire
{
    public class Building_GlobalWorkTable : Building_WorkTable
    {
        public GlobalProductionOrderStack globalOrderStack;
        
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
                    globalOrderStack.ProcessOrders();
                }
            }
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

        // 修改：添加空投命令到技能栏，添加工厂设施检查
        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo g in base.GetGizmos())
            {
                yield return g;
            }

            // 只有在有输出物品且有工厂设施的飞行器时才显示空投按钮
            var globalStorage = Find.World.GetComponent<GlobalStorageWorldComponent>();
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

        // 新增：检查是否有拥有FactoryFacility设施的飞行器
        private bool HasFactoryFacilityFlyOver()
        {
            Map map = Map;
            if (map == null) return false;

            try
            {
                // 检查所有FlyOver类型的物体
                var allFlyOvers = new List<Thing>();
                var dynamicObjects = map.dynamicDrawManager.DrawThings;
                foreach (var thing in dynamicObjects)
                {
                    if (thing is FlyOver)
                    {
                        allFlyOvers.Add(thing);
                    }
                }

                Log.Message($"[FactoryFacility Check] Found {allFlyOvers.Count} FlyOvers on map");

                foreach (var thing in allFlyOvers)
                {
                    if (thing is FlyOver flyOver && !flyOver.Destroyed)
                    {
                        // 检查设施
                        var facilitiesComp = flyOver.GetComp<CompFlyOverFacilities>();
                        if (facilitiesComp == null)
                        {
                            Log.Warning($"[FactoryFacility Check] FlyOver at {flyOver.Position} has no CompFlyOverFacilities");
                            continue;
                        }
                        
                        if (facilitiesComp.HasFacility("FactoryFacility"))
                        {
                            Log.Message($"[FactoryFacility Check] Found valid FlyOver at {flyOver.Position} with FactoryFacility");
                            return true;
                        }
                        else
                        {
                            Log.Message($"[FactoryFacility Check] FlyOver at {flyOver.Position} missing FactoryFacility. Has: {string.Join(", ", facilitiesComp.GetActiveFacilities())}");
                        }
                    }
                }

                Log.Message("[FactoryFacility Check] No FlyOver with FactoryFacility found");
                return false;
            }
            catch (System.Exception ex)
            {
                Log.Error($"[FactoryFacility Check] Error in HasFactoryFacilityFlyOver: {ex}");
                return false;
            }
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

        // 新增：分配物品到空投舱，包含材质处理
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
                            Pawn pawn = PawnGenerator.GeneratePawn(randomPawnKind, Faction.OfPlayer);
                            allItems.Add(pawn);
                        }
                    }
                }
                else
                {
                    // 对于普通物品，按照堆叠限制分割
                    while (remainingCount > 0)
                    {
                        int stackSize = Mathf.Min(remainingCount, thingDef.stackLimit);
                        Thing thing = CreateThingWithMaterial(thingDef, stackSize);
                        allItems.Add(thing);
                        remainingCount -= stackSize;
                    }
                }
            }

            if (allItems.Count == 0)
                return podContents;

            // 平均分配物品到空投舱
            int currentPod = 0;
            foreach (Thing item in allItems)
            {
                podContents[currentPod].Add(item);
                currentPod = (currentPod + 1) % podCount;
            }

            // 从存储中移除已分配的物品
            foreach (var kvp in storage.outputStorage.ToList())
            {
                storage.outputStorage[kvp.Key] = 0;
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
                        Log.Message($"[Material Rule] {thingDef.defName} requires metallic, using Plasteel");
                    }
                    else if (fabricCategory != null)
                    {
                        // 布革类 -> 超织物
                        selectedStuff = ThingDefOf_WULA.Hyperweave;
                        Log.Message($"[Material Rule] {thingDef.defName} requires fabric/leather, using Hyperweave");
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

        // 在 Building_GlobalWorkTable.cs 中修改 GetRandomPawnKindForType 方法
        private PawnKindDef GetRandomPawnKindForType(ThingDef pawnType)
        {
            if (pawnType.race == null) return null;

            // 获取建筑拥有者派系
            Faction buildingFaction = this.Faction;
            if (buildingFaction == null)
            {
                Log.Warning("Building has no faction, cannot select appropriate pawn kind");
                return null;
            }

            // 获取该种族的所有PawnKindDef
            var availableKinds = DefDatabase<PawnKindDef>.AllDefs
                .Where(kind => kind.race == pawnType)
                .ToList();

            if (availableKinds.Count == 0) return null;

            // 按优先级分组
            var matchingFactionKinds = availableKinds
                .Where(kind => kind.defaultFactionDef != null &&
                              kind.defaultFactionDef == buildingFaction.def)
                .ToList();

            var noFactionKinds = availableKinds
                .Where(kind => kind.defaultFactionDef == null)
                .ToList();

            // 排除与建筑派系不同的PawnKind
            var excludedKinds = availableKinds
                .Where(kind => kind.defaultFactionDef != null &&
                              kind.defaultFactionDef != buildingFaction.def)
                .ToList();

            // 优先级选择
            PawnKindDef selectedKind = null;

            // 1. 最高优先级：与建筑派系相同的PawnKind
            if (matchingFactionKinds.Count > 0)
            {
                selectedKind = matchingFactionKinds.RandomElement();
            }
            // 2. 备选：没有defaultFactionDef的PawnKind
            else if (noFactionKinds.Count > 0)
            {
                selectedKind = noFactionKinds.RandomElement();
            }
            // 3. 没有符合条件的PawnKind
            else
            {
                Log.Warning($"No suitable PawnKind found for {pawnType.defName} with building faction {buildingFaction.def.defName}");
                return null;
            }

            return selectedKind;
        }

        // 新增：创建空投舱
        private bool CreateDropPod(IntVec3 dropCell, List<Thing> contents)
        {
            try
            {
                if (contents == null || contents.Count == 0)
                    return false;

                // 创建空投舱信息
                ActiveTransporterInfo dropPodInfo = new ActiveTransporterInfo();
                
                // 添加所有物品到空投舱
                foreach (Thing thing in contents)
                {
                    dropPodInfo.innerContainer.TryAdd(thing, true);
                }

                // 设置空投舱参数
                dropPodInfo.openDelay = 180; // 3秒后打开
                dropPodInfo.leaveSlag = true;

                // 生成空投舱
                DropPodUtility.MakeDropPodAt(dropCell, Map, dropPodInfo);
                
                return true;
            }
            catch (System.Exception ex)
            {
                Log.Error($"Failed to create drop pod at {dropCell}: {ex}");
                return false;
            }
        }
    }
}
