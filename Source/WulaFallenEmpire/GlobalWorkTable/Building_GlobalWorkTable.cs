// Building_GlobalWorkTable.cs (调整为每秒处理)
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
        private const int ProcessInterval = 1; // 改为每tick处理，以实现每秒1工作量

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
            
            // 改为每tick处理，以实现精确的工作量控制
            if (Find.TickManager.TicksGame % ProcessInterval == 0 && 
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

        // 新增：添加空投命令到技能栏
        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo g in base.GetGizmos())
            {
                yield return g;
            }

            // 只有在有输出物品时才显示空投按钮
            var globalStorage = Find.World.GetComponent<GlobalStorageWorldComponent>();
            if (globalStorage != null && globalStorage.outputStorage.Any(kvp => kvp.Value > 0))
            {
                yield return new Command_Action
                {
                    action = StartAirdropTargeting,
                    defaultLabel = "WULA_AirdropProducts".Translate(),
                    defaultDesc = "WULA_AirdropProductsDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/Airdrop"),
                    disabledReason = "WULA_CannotAirdrop".Translate()
                };
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

        // 新增：分配物品到空投舱
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

                // 按照堆叠限制分割物品
                while (remainingCount > 0)
                {
                    int stackSize = Mathf.Min(remainingCount, thingDef.stackLimit);
                    Thing thing = ThingMaker.MakeThing(thingDef);
                    thing.stackCount = stackSize;
                    allItems.Add(thing);
                    remainingCount -= stackSize;
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
