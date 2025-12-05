using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace WulaFallenEmpire
{
    public class CompBuildingSpawner : ThingComp
    {
        public CompProperties_BuildingSpawner Props => (CompProperties_BuildingSpawner)props;
        
        private GlobalStorageWorldComponent _globalStorage;
        private GlobalStorageWorldComponent GlobalStorage
        {
            get
            {
                if (_globalStorage == null)
                {
                    _globalStorage = Find.World.GetComponent<GlobalStorageWorldComponent>();
                }
                return _globalStorage;
            }
        }
        
        // 状态变量
        public bool used = false;
        private int callTick = -1;
        public bool calling = false;
        private bool usedGlobalStorage = false;
        public bool autoCallScheduled = false;
        
        // 非玩家派系检查
        public bool IsNonPlayerFaction => parent.Faction != null && parent.Faction != Faction.OfPlayer;
        
        // 自动呼叫条件
        private bool ShouldAutoCall => IsNonPlayerFaction && Props.canAutoCall && !autoCallScheduled && !used;
        
        // 科技检查
        public bool HasRequiredResearch
        {
            get
            {
                if (Props.requiredResearch == null)
                    return true;
                    
                if (IsNonPlayerFaction)
                    return true; // 非玩家派系不需要科技
                    
                return Props.requiredResearch.IsFinished;
            }
        }
        
        // FlyOver 检查
        public bool HasRequiredFlyOver
        {
            get
            {
                if (!Props.requireFlyOver)
                    return true;

                if (parent?.Map == null) 
                    return false;
                
                try
                {
                    // 检查所有FlyOver类型的物体
                    var allFlyOvers = new List<Thing>();
                    var dynamicObjects = parent.Map.dynamicDrawManager.DrawThings;
                    foreach (var thing in dynamicObjects)
                    {
                        if (thing is FlyOver)
                        {
                            allFlyOvers.Add(thing);
                        }
                    }

                    Log.Message($"[BuildingSpawner] Found {allFlyOvers.Count} FlyOvers on map");

                    foreach (var thing in allFlyOvers)
                    {
                        if (thing is FlyOver flyOver && !flyOver.Destroyed)
                        {
                            var facilitiesComp = flyOver.GetComp<CompFlyOverFacilities>();
                            if (facilitiesComp == null)
                            {
                                Log.Warning($"[BuildingSpawner] FlyOver at {flyOver.Position} has no CompFlyOverFacilities");
                                continue;
                            }
                            
                            if (facilitiesComp.HasFacility("BuildingdropperFacility"))
                            {
                                Log.Message($"[BuildingSpawner] Found valid FlyOver at {flyOver.Position} with BuildingdropperFacility");
                                return true;
                            }
                        }
                    }

                    Log.Message("[BuildingSpawner] No FlyOver with BuildingdropperFacility found");
                    return false;
                }
                catch (System.Exception ex)
                {
                    Log.Error($"[BuildingSpawner] Exception while checking for FlyOver: {ex}");
                    return false;
                }
            }
        }
        
        // 屋顶检查
        public bool CheckRoofConditions
        {
            get
            {
                if (parent?.Map == null) 
                    return true;
                
                RoofDef roof = parent.Position.GetRoof(parent.Map);
                if (roof == null) 
                    return true;
                
                if (roof.isThickRoof && !Props.allowThickRoof) 
                    return false;
                if (!roof.isThickRoof && !Props.allowThinRoof) 
                    return false;
                
                return true;
            }
        }
        
        // 总体可调用检查
        public bool CanCallBuilding => !used && !calling && HasRequiredResearch && 
            HasRequiredFlyOver && CheckRoofConditions;
        
        // 初始化和保存
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            
            if (!respawningAfterLoad && ShouldAutoCall)
            {
                autoCallScheduled = true;
                callTick = Find.TickManager.TicksGame + Props.autoCallDelayTicks;
                calling = true;
                
                Log.Message($"[BuildingSpawner] Scheduled auto-call for non-player building {parent.Label} at tick {callTick}");
            }
        }
        
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref used, "used", false);
            Scribe_Values.Look(ref callTick, "callTick", -1);
            Scribe_Values.Look(ref calling, "calling", false);
            Scribe_Values.Look(ref usedGlobalStorage, "usedGlobalStorage", false);
            Scribe_Values.Look(ref autoCallScheduled, "autoCallScheduled", false);
        }
        
        // 定时更新
        public override void CompTick()
        {
            base.CompTick();
            
            if (calling && callTick >= 0 && Find.TickManager.TicksGame >= callTick)
            {
                if (autoCallScheduled)
                {
                    ExecuteAutoBuildingSpawn();
                }
                else
                {
                    ExecuteBuildingSpawn();
                }
            }
        }
        
        // 计算生成位置
        private IntVec3 CalculateSpawnPosition()
        {
            IntVec3 basePos = parent.Position;
            
            // 应用偏移
            IntVec3 offsetPos = basePos + new IntVec3(Props.spawnOffset.x, 0, Props.spawnOffset.z);
            
            return offsetPos;
        }
        
        // 自动生成建筑（非玩家派系）
        protected virtual void ExecuteAutoBuildingSpawn()
        {
            try
            {
                Log.Message($"[BuildingSpawner] Executing auto building spawn for non-player building at {parent.Position}");
                
                if (Props.buildingToSpawn == null)
                {
                    Log.Error("[BuildingSpawner] Building def is null!");
                    ResetCall();
                    return;
                }
                
                // 处理屋顶
                HandleRoofDestruction();
                
                // 获取生成位置
                IntVec3 spawnPos = CalculateSpawnPosition();
                
                // 检查位置是否可用
                if (!CanSpawnAtPosition(spawnPos))
                {
                    Log.Error($"[BuildingSpawner] Cannot spawn building at {spawnPos}");
                    ResetCall();
                    return;
                }
                
                // 播放效果器
                PlaySpawnEffects(spawnPos);
                
                // 创建建筑
                Thing newBuilding = CreateBuilding(spawnPos);
                
                if (newBuilding == null)
                {
                    Log.Error("[BuildingSpawner] Failed to create building!");
                    ResetCall();
                    return;
                }
                
                // 生成建筑
                GenSpawn.Spawn(newBuilding, spawnPos, parent.Map, Props.buildingRotation);
                
                // 后处理
                PostSpawnProcessing(newBuilding);
                
                // 销毁原建筑
                if (Props.destroyBuilding)
                {
                    parent.Destroy(DestroyMode.Vanish);
                }
                
                // 重置状态
                ResetCall();
                autoCallScheduled = false;
                
                // 显示消息
                Messages.Message("WULA_AutoBuildingSpawned".Translate(Props.buildingToSpawn.label, parent.Faction.Name), 
                    MessageTypeDefOf.NeutralEvent);
            }
            catch (System.Exception ex)
            {
                Log.Error($"[BuildingSpawner] Error in ExecuteAutoBuildingSpawn: {ex}");
                ResetCall();
            }
        }
        
        // 手动生成建筑
        public void CallBuilding(bool isAutoCall = false)
        {
            // 非玩家派系不能手动呼叫
            if (IsNonPlayerFaction && !isAutoCall)
            {
                Messages.Message("WULA_NonPlayerCannotCall".Translate(), parent, MessageTypeDefOf.RejectInput);
                return;
            }
            
            if (!CanCallBuilding)
            {
                ShowDisabledReason();
                return;
            }
            
            Log.Message($"[BuildingSpawner] Starting building spawn from {parent.Label} at {parent.Position}");
            
            calling = true;
            used = true;
            int delay = isAutoCall ? Props.autoCallDelayTicks : Props.delayTicks;
            callTick = Find.TickManager.TicksGame + delay;
            
            if (delay <= 0)
            {
                ExecuteBuildingSpawn();
            }
            else
            {
                string messageKey = usedGlobalStorage ? 
                    "WULA_BuildingIncomingFromGlobal" : 
                    "WULA_BuildingIncoming";
                Messages.Message(messageKey.Translate(delay.ToStringTicksToPeriod()), parent, MessageTypeDefOf.ThreatBig);
            }
        }
        
        // 显示禁用原因
        private void ShowDisabledReason()
        {
            if (!HasRequiredResearch)
            {
                Messages.Message("WULA_MissingResearch".Translate(Props.requiredResearch.label), 
                    parent, MessageTypeDefOf.RejectInput);
            }
            else if (!HasRequiredFlyOver)
            {
                Messages.Message("WULA_NoBuildingDropperFlyOver".Translate(), 
                    parent, MessageTypeDefOf.RejectInput);
            }
            else if (!CheckRoofConditions)
            {
                string roofType = parent.Position.GetRoof(parent.Map)?.isThickRoof == true ? 
                    "thick" : "thin";
                Messages.Message($"WULA_RoofBlocking_{roofType}".Translate(), 
                    parent, MessageTypeDefOf.RejectInput);
            }
        }
        
        // 重置状态
        protected void ResetCall()
        {
            calling = false;
            used = false;
            callTick = -1;
            usedGlobalStorage = false;
            autoCallScheduled = false;
        }
        
        // 执行建筑生成
        protected virtual void ExecuteBuildingSpawn()
        {
            Log.Message($"[BuildingSpawner] Executing building spawn at {parent.Position}");
            
            if (Props.buildingToSpawn == null)
            {
                Log.Error("[BuildingSpawner] Building def is null!");
                return;
            }
            
            // 检查资源
            var resourceCheck = CheckAndConsumeMaterials();
            if (!resourceCheck.HasEnoughMaterials)
            {
                Log.Message($"[BuildingSpawner] Aborting building spawn due to insufficient materials.");
                ResetCall();
                return;
            }
            
            usedGlobalStorage = resourceCheck.UsedGlobalStorage;
            
            // 处理屋顶
            HandleRoofDestruction();
            
            // 获取生成位置
            IntVec3 spawnPos = CalculateSpawnPosition();
            
            // 检查位置是否可用
            if (!CanSpawnAtPosition(spawnPos))
            {
                Log.Error($"[BuildingSpawner] Cannot spawn building at {spawnPos}");
                ResetCall();
                return;
            }
            
            // 播放效果器
            PlaySpawnEffects(spawnPos);
            
            // 创建建筑
            Thing newBuilding = CreateBuilding(spawnPos);
            
            if (newBuilding == null)
            {
                Log.Error("[BuildingSpawner] Failed to create building!");
                return;
            }
            
            // 生成建筑
            GenSpawn.Spawn(newBuilding, spawnPos, parent.Map, Props.buildingRotation);
            
            // 后处理
            PostSpawnProcessing(newBuilding);
            
            // 销毁原建筑
            if (Props.destroyBuilding)
            {
                parent.Destroy(DestroyMode.Vanish);
            }
            
            calling = false;
            callTick = -1;
        }
        
        // 检查位置是否可用
        private bool CanSpawnAtPosition(IntVec3 spawnPos)
        {
            if (parent?.Map == null)
                return false;
                
            // 检查是否在地图范围内
            if (!spawnPos.InBounds(parent.Map))
                return false;
                
            // 检查是否有阻挡物
            if (!Props.canReplaceExisting)
            {
                List<Thing> thingsAtPos = spawnPos.GetThingList(parent.Map);
                foreach (var thing in thingsAtPos)
                {
                    // 跳过不可穿透的建筑和植物
                    if (thing.def.passability == Traversability.Impassable)
                        return false;
                }
            }
            
            return true;
        }
        
        // 处理屋顶破坏
        private void HandleRoofDestruction()
        {
            if (parent?.Map == null) 
                return;
            
            IntVec3 targetPos = parent.Position;
            RoofDef roof = targetPos.GetRoof(parent.Map);
            
            if (roof != null && !roof.isThickRoof && Props.allowThinRoof)
            {
                Log.Message($"[BuildingSpawner] Destroying thin roof at {targetPos}");
                parent.Map.roofGrid.SetRoof(targetPos, null);
                
                // 生成屋顶破坏效果
                FleckMaker.ThrowDustPuffThick(targetPos.ToVector3Shifted(), parent.Map, 2f, 
                    new Color(1f, 1f, 1f, 2f));
            }
        }
        
        // 播放生成效果
        private void PlaySpawnEffects(IntVec3 spawnPos)
        {
            if (parent?.Map == null)
                return;
                
            // 播放效果器
            if (Props.spawnEffecter != null)
            {
                Effecter effecter = Props.spawnEffecter.Spawn();
                effecter.Trigger(new TargetInfo(spawnPos, parent.Map), 
                    new TargetInfo(spawnPos, parent.Map));
                effecter.Cleanup();
            }
            
            // 播放音效
            if (Props.spawnSound != null)
            {
                Props.spawnSound.PlayOneShot(new TargetInfo(spawnPos, parent.Map));
            }
            
            // 播放粒子效果
            FleckMaker.ThrowSmoke(spawnPos.ToVector3Shifted(), parent.Map, 1.5f);
            FleckMaker.ThrowLightningGlow(spawnPos.ToVector3Shifted(), parent.Map, 2f);
        }
        
        // 创建建筑实例
        private Thing CreateBuilding(IntVec3 spawnPos)
        {
            if (Props.buildingToSpawn == null)
                return null;
                
            // 构建参数
            ThingDef stuff = null;
            if (Props.buildingToSpawn.MadeFromStuff)
            {
                // 如果需要材料，可以从配置中获取或使用默认材料
                stuff = ThingDefOf.Steel;
            }
            
            // 创建建筑
            Thing newBuilding = ThingMaker.MakeThing(Props.buildingToSpawn, stuff);
            
            // 设置派系
            if (Props.inheritFaction && parent.Faction != null)
            {
                newBuilding.SetFaction(parent.Faction);
            }
            
            // 设置燃料（如果有）
            CompRefuelable refuelable = newBuilding.TryGetComp<CompRefuelable>();
            if (refuelable != null)
            {
                float fuelPercent = Props.fuelRange.RandomInRange;
                refuelable.Refuel(refuelable.Props.fuelCapacity * fuelPercent);
            }
            
            return newBuilding;
        }
        
        // 生成后处理
        private void PostSpawnProcessing(Thing newBuilding)
        {
            // 可以添加额外的后处理逻辑
            // 例如：启动炮塔、设置工作模式等
            
            // 如果生成的是炮塔，自动启动
            if (newBuilding is Building_Turret turret)
            {
                CompPowerTrader powerComp = turret.GetComp<CompPowerTrader>();
                if (powerComp != null)
                {
                    powerComp.PowerOn = true;
                }
            }
            
            // 记录日志
            Log.Message($"[BuildingSpawner] Successfully spawned {Props.buildingToSpawn.label} at {newBuilding.Position}");
        }
        
        // 资源管理（与SkyfallerCaller类似）
        protected virtual List<ThingDefCountClass> CostList
        {
            get
            {
                if (parent.def?.costList.NullOrEmpty() ?? true)
                {
                    return null;
                }
                return parent.def.costList;
            }
        }
        
        protected struct ResourceCheckResult
        {
            public bool HasEnoughMaterials;
            public bool UsedGlobalStorage;
            public Dictionary<ThingDef, int> BeaconMaterials;
            public Dictionary<ThingDef, int> GlobalMaterials;
        }
        
        protected ResourceCheckResult CheckAndConsumeMaterials()
        {
            var result = new ResourceCheckResult
            {
                HasEnoughMaterials = false,
                UsedGlobalStorage = false,
                BeaconMaterials = new Dictionary<ThingDef, int>(),
                GlobalMaterials = new Dictionary<ThingDef, int>()
            };

            if (DebugSettings.godMode)
            {
                result.HasEnoughMaterials = true;
                return result;
            }

            var costList = CostList;
            if (costList.NullOrEmpty())
            {
                result.HasEnoughMaterials = true;
                return result;
            }

            if (parent.Map == null)
            {
                return result;
            }

            // 收集信标附近的物资
            var beaconMaterials = CollectBeaconMaterials();
            result.BeaconMaterials = beaconMaterials;

            // 检查信标附近物资是否足够
            bool beaconHasEnough = true;
            foreach (var cost in costList)
            {
                int availableInBeacon = beaconMaterials.ContainsKey(cost.thingDef) ? beaconMaterials[cost.thingDef] : 0;
                if (availableInBeacon < cost.count)
                {
                    beaconHasEnough = false;
                    break;
                }
            }

            if (beaconHasEnough)
            {
                ConsumeBeaconMaterials(beaconMaterials, costList);
                result.HasEnoughMaterials = true;
                result.UsedGlobalStorage = false;
                return result;
            }

            // 检查全局储存器
            var globalMaterials = CheckGlobalStorageMaterials();
            result.GlobalMaterials = globalMaterials;

            bool globalHasEnough = true;
            foreach (var cost in costList)
            {
                int availableInBeacon = beaconMaterials.ContainsKey(cost.thingDef) ? beaconMaterials[cost.thingDef] : 0;
                int availableInGlobal = globalMaterials.ContainsKey(cost.thingDef) ? globalMaterials[cost.thingDef] : 0;
                
                if (availableInBeacon + availableInGlobal < cost.count)
                {
                    globalHasEnough = false;
                    break;
                }
            }

            if (globalHasEnough)
            {
                ConsumeMixedMaterials(beaconMaterials, globalMaterials, costList);
                result.HasEnoughMaterials = true;
                result.UsedGlobalStorage = true;
                return result;
            }

            result.HasEnoughMaterials = false;
            return result;
        }

        // 资源收集和消耗方法（与SkyfallerCaller相同）
        private Dictionary<ThingDef, int> CollectBeaconMaterials()
        {
            var materials = new Dictionary<ThingDef, int>();

            if (parent.Map == null) return materials;

            foreach (Building_OrbitalTradeBeacon beacon in Building_OrbitalTradeBeacon.AllPowered(parent.Map))
            {
                foreach (IntVec3 cell in beacon.TradeableCells)
                {
                    List<Thing> thingList = parent.Map.thingGrid.ThingsListAt(cell);
                    for (int i = 0; i < thingList.Count; i++)
                    {
                        Thing thing = thingList[i];
                        if (thing.def.EverHaulable)
                        {
                            if (materials.ContainsKey(thing.def))
                            {
                                materials[thing.def] += thing.stackCount;
                            }
                            else
                            {
                                materials[thing.def] = thing.stackCount;
                            }
                        }
                    }
                }
            }

            return materials;
        }

        // 新增：检查全局储存器中的物资
        private Dictionary<ThingDef, int> CheckGlobalStorageMaterials()
        {
            var materials = new Dictionary<ThingDef, int>();

            if (GlobalStorage == null) return materials;

            var costList = CostList;
            if (costList.NullOrEmpty()) return materials;

            foreach (var cost in costList)
            {
                int globalCount = GlobalStorage.GetInputStorageCount(cost.thingDef);
                if (globalCount > 0)
                {
                    materials[cost.thingDef] = globalCount;
                }
            }

            return materials;
        }

        // 新增：只消耗信标附近的物资
        private void ConsumeBeaconMaterials(Dictionary<ThingDef, int> beaconMaterials, List<ThingDefCountClass> costList)
        {
            var tradeableThings = new List<Thing>();

            foreach (Building_OrbitalTradeBeacon beacon in Building_OrbitalTradeBeacon.AllPowered(parent.Map))
            {
                foreach (IntVec3 cell in beacon.TradeableCells)
                {
                    List<Thing> thingList = parent.Map.thingGrid.ThingsListAt(cell);
                    for (int i = 0; i < thingList.Count; i++)
                    {
                        Thing thing = thingList[i];
                        if (thing.def.EverHaulable)
                        {
                            tradeableThings.Add(thing);
                        }
                    }
                }
            }

            foreach (var cost in costList)
            {
                int remaining = cost.count;
                for (int i = tradeableThings.Count - 1; i >= 0 && remaining > 0; i--)
                {
                    var thing = tradeableThings[i];
                    if (thing.def == cost.thingDef)
                    {
                        if (thing.stackCount > remaining)
                        {
                            thing.SplitOff(remaining);
                            remaining = 0;
                        }
                        else
                        {
                            remaining -= thing.stackCount;
                            thing.Destroy();
                            tradeableThings.RemoveAt(i);
                        }
                    }
                }
            }
        }

        // 新增：混合消耗信标和全局储存器的物资
        private void ConsumeMixedMaterials(Dictionary<ThingDef, int> beaconMaterials, Dictionary<ThingDef, int> globalMaterials, List<ThingDefCountClass> costList)
        {
            // 先消耗信标附近的物资
            var tradeableThings = new List<Thing>();

            foreach (Building_OrbitalTradeBeacon beacon in Building_OrbitalTradeBeacon.AllPowered(parent.Map))
            {
                foreach (IntVec3 cell in beacon.TradeableCells)
                {
                    List<Thing> thingList = parent.Map.thingGrid.ThingsListAt(cell);
                    for (int i = 0; i < thingList.Count; i++)
                    {
                        Thing thing = thingList[i];
                        if (thing.def.EverHaulable)
                        {
                            tradeableThings.Add(thing);
                        }
                    }
                }
            }

            // 对每种所需材料进行处理
            foreach (var cost in costList)
            {
                int remaining = cost.count;

                // 第一步：消耗信标附近的物资
                for (int i = tradeableThings.Count - 1; i >= 0 && remaining > 0; i--)
                {
                    var thing = tradeableThings[i];
                    if (thing.def == cost.thingDef)
                    {
                        if (thing.stackCount > remaining)
                        {
                            thing.SplitOff(remaining);
                            remaining = 0;
                        }
                        else
                        {
                            remaining -= thing.stackCount;
                            thing.Destroy();
                            tradeableThings.RemoveAt(i);
                        }
                    }
                }

                // 第二步：如果还有剩余，从全局储存器扣除
                if (remaining > 0 && GlobalStorage != null)
                {
                    GlobalStorage.RemoveFromInputStorage(cost.thingDef, remaining);
                }
            }
        }

        // 保留原有的 HasEnoughMaterials 方法用于 Gizmo 显示
        public bool HasEnoughMaterials()
        {
            if (DebugSettings.godMode) return true;

            var costList = CostList;
            if (costList.NullOrEmpty())
            {
                return true;
            }

            // 第一步：检查信标附近物资
            var beaconMaterials = CollectBeaconMaterials();
            bool beaconHasEnough = true;

            foreach (var cost in costList)
            {
                int availableInBeacon = beaconMaterials.ContainsKey(cost.thingDef) ? beaconMaterials[cost.thingDef] : 0;
                if (availableInBeacon < cost.count)
                {
                    beaconHasEnough = false;
                    break;
                }
            }

            if (beaconHasEnough) return true;

            // 第二步：检查全局储存器（如果信标附近不够）
            if (GlobalStorage == null) return false;

            foreach (var cost in costList)
            {
                int availableInBeacon = beaconMaterials.ContainsKey(cost.thingDef) ? beaconMaterials[cost.thingDef] : 0;
                int availableInGlobal = GlobalStorage.GetInputStorageCount(cost.thingDef);

                if (availableInBeacon + availableInGlobal < cost.count)
                {
                    return false;
                }
            }

            return true;
        }

        // Gizmos
        private void CancelCall()
        {
            calling = false;
            used = false;
            callTick = -1;
            usedGlobalStorage = false;
            autoCallScheduled = false;
            Messages.Message("WULA_BuildingCallCancelled".Translate(), parent, MessageTypeDefOf.NeutralEvent);
        }
        
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (var gizmo in base.CompGetGizmosExtra())
                yield return gizmo;

            // 非玩家派系不显示呼叫按钮
            if (IsNonPlayerFaction)
                yield break;

            if (calling)
            {
                Command_Action cancelCommand = new Command_Action
                {
                    defaultLabel = "WULA_CancelBuilding".Translate(),
                    defaultDesc = "WULA_CancelBuildingDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("UI/Designators/Cancel"),
                    action = CancelCall
                };
                yield return cancelCommand;
            }
            
            if (CanCallBuilding)
            {
                string reason = GetDisabledReason();
                Command_Action callCommand = new Command_Action
                {
                    defaultLabel = "WULA_CallBuilding".Translate(),
                    defaultDesc = GetCallDescription(),
                    icon = ContentFinder<Texture2D>.Get("Wula/UI/Commands/WULA_SpawnBuilding"),
                    action = () => CallBuilding(false),
                    disabledReason = reason
                };
                if (!string.IsNullOrEmpty(reason))
                {
                    callCommand.Disable(reason);
                }
                yield return callCommand;
            }
        }
        
        // 获取禁用原因
        private string GetDisabledReason()
        {
            if (IsNonPlayerFaction)
            {
                return "WULA_NonPlayerCannotCall".Translate();
            }
            
            if (!HasRequiredResearch)
            {
                return "WULA_MissingResearch".Translate(Props.requiredResearch.label);
            }
            
            if (Props.requireFlyOver && !HasRequiredFlyOver)
            {
                return "WULA_NoBuildingDropperFlyOver".Translate();
            }
            
            if (!CheckRoofConditions)
            {
                string roofType = parent.Position.GetRoof(parent.Map)?.isThickRoof == true ? 
                    "thick" : "thin";
                return $"WULA_RoofBlocking_{roofType}".Translate();
            }
            
            if (!HasEnoughMaterials())
            {
                return "WULA_InsufficientMaterials".Translate();
            }
            
            return null;
        }
        
        // 获取呼叫描述
        private string GetCallDescription()
        {
            var sb = new StringBuilder();
            sb.Append("WULA_CallBuildingDesc".Translate(Props.buildingToSpawn.label));
            
            if (Props.requiredResearch != null)
            {
                sb.AppendLine().Append("WULA_RequiresResearch".Translate(Props.requiredResearch.label));
            }
            
            if (Props.requireFlyOver && !HasRequiredFlyOver)
            {
                sb.AppendLine().Append("WULA_RequiresBuildingDropperFlyOver".Translate());
            }
            
            if (parent?.Map != null)
            {
                RoofDef roof = parent.Position.GetRoof(parent.Map);
                if (roof != null)
                {
                    if (roof.isThickRoof && !Props.allowThickRoof)
                    {
                        sb.AppendLine().Append("WULA_ThickRoofBlockingDesc".Translate());
                    }
                    else if (!roof.isThickRoof && !Props.allowThinRoof)
                    {
                        sb.AppendLine().Append("WULA_RoofBlockingDesc".Translate());
                    }
                }
            }
            
            string costString = GetCostString();
            if (!string.IsNullOrEmpty(costString))
            {
                sb.AppendLine().AppendLine().Append("WULA_RequiredMaterials".Translate());
                sb.Append(costString);
            }
            
            return sb.ToString();
        }
        
        // 获取成本字符串
        private string GetCostString()
        {
            var costList = CostList;
            if (costList.NullOrEmpty())
            {
                return "";
            }
            
            var sb = new StringBuilder();
            foreach (var cost in costList)
            {
                sb.AppendLine($"  - {cost.thingDef.LabelCap}: {cost.count}");
            }
            return sb.ToString();
        }
        
        // 检查字符串
        public override string CompInspectStringExtra()
        {
            if (parent?.Map == null)
            {
                return base.CompInspectStringExtra();
            }
            
            var sb = new StringBuilder();
            
            // 显示自动呼叫状态
            if (autoCallScheduled && calling)
            {
                int ticksLeft = callTick - Find.TickManager.TicksGame;
                sb.Append("WULA_AutoBuildingArrivingIn".Translate(ticksLeft.ToStringTicksToPeriod()));
            }
            else if (calling)
            {
                int ticksLeft = callTick - Find.TickManager.TicksGame;
                if (ticksLeft > 0)
                {
                    string messageKey = usedGlobalStorage ? 
                        "WULA_BuildingArrivingInFromGlobal" : 
                        "WULA_BuildingArrivingIn";
                    sb.Append(messageKey.Translate(ticksLeft.ToStringTicksToPeriod()));
                }
            }
            else if (!used)
            {
                if (IsNonPlayerFaction && Props.canAutoCall)
                {
                    sb.Append("WULA_AutoBuildingReady".Translate());
                }
                else
                {
                    sb.Append("WULA_ReadyToCallBuilding".Translate(Props.buildingToSpawn.label));
                }
                
                // 显示科技需求
                if (Props.requiredResearch != null && !HasRequiredResearch)
                {
                    sb.AppendLine().Append("WULA_MissingResearch".Translate(Props.requiredResearch.label));
                }
                
                // 显示FlyOver需求
                if (Props.requireFlyOver && !HasRequiredFlyOver)
                {
                    sb.AppendLine().Append("WULA_MissingBuildingDropperFlyOver".Translate());
                }
                
                // 显示屋顶状态
                RoofDef roof = parent.Position.GetRoof(parent.Map);
                if (roof != null)
                {
                    if (roof.isThickRoof && !Props.allowThickRoof)
                    {
                        sb.AppendLine().Append("WULA_BlockedByThickRoof".Translate());
                    }
                    else if (!roof.isThickRoof && !Props.allowThinRoof)
                    {
                        sb.AppendLine().Append("WULA_BlockedByRoof".Translate());
                    }
                }
                
                // 显示成本
                string costString = GetCostString();
                if (!string.IsNullOrEmpty(costString))
                {
                    sb.AppendLine().AppendLine("WULA_RequiredMaterials".Translate());
                    sb.Append(costString);
                }
            }
            
            string baseInspectString = base.CompInspectStringExtra();
            if (!string.IsNullOrEmpty(baseInspectString))
            {
                if (sb.Length > 0)
                {
                    sb.AppendLine();
                }
                sb.Append(baseInspectString);
            }
            
            return sb.Length > 0 ? sb.ToString().TrimEnd() : null;
        }
    }
}
