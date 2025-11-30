using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    public class CompSkyfallerCaller : ThingComp
    {
        protected CompProperties_SkyfallerCaller Props => (CompProperties_SkyfallerCaller)props;

        private WulaSkyfallerWorldComponent _worldComponent;
        private WulaSkyfallerWorldComponent WorldComp
        {
            get
            {
                if (_worldComponent == null)
                {
                    _worldComponent = Find.World.GetComponent<WulaSkyfallerWorldComponent>();
                }
                return _worldComponent;
            }
        }

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
        
        private bool used = false;
        private int callTick = -1;
        private bool calling = false;
        private bool usedGlobalStorage = false;
        public bool autoCallScheduled = false; // 新增：标记是否已安排自动呼叫

        public bool CanCall => !used && !calling;

        // 新增：检查建筑是否属于非玩家派系
        public bool IsNonPlayerFaction => parent.Faction != null && parent.Faction != Faction.OfPlayer;

        // 新增：检查是否应该自动呼叫
        private bool ShouldAutoCall => IsNonPlayerFaction && Props.canAutoCall && !autoCallScheduled && !used;

        // 固定的显示标签
        public string RequiredFlyOverLabel => "建筑空投飞行器";

        // 检查是否有拥有 BuildingdropperFacility 设施的 FlyOver
        public bool HasRequiredFlyOver
        {
            get
            {
                // 如果不需要 FlyOver，直接返回 true
                if (!Props.requireFlyOver)
                    return true;

                if (parent?.Map == null) return false;
                
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

                    Log.Message($"[SkyfallerCaller] Found {allFlyOvers.Count} FlyOvers on map");

                    foreach (var thing in allFlyOvers)
                    {
                        if (thing is FlyOver flyOver && !flyOver.Destroyed)
                        {
                            // 检查设施
                            var facilitiesComp = flyOver.GetComp<CompFlyOverFacilities>();
                            if (facilitiesComp == null)
                            {
                                Log.Warning($"[SkyfallerCaller] FlyOver at {flyOver.Position} has no CompFlyOverFacilities");
                                continue;
                            }
                            
                            if (facilitiesComp.HasFacility("BuildingdropperFacility"))
                            {
                                Log.Message($"[SkyfallerCaller] Found valid FlyOver at {flyOver.Position} with BuildingdropperFacility");
                                return true;
                            }
                            else
                            {
                                Log.Message($"[SkyfallerCaller] FlyOver at {flyOver.Position} missing BuildingdropperFacility. Has: {string.Join(", ", facilitiesComp.GetActiveFacilities())}");
                            }
                        }
                    }

                    Log.Message("[SkyfallerCaller] No FlyOver with BuildingdropperFacility found");
                    return false;
                }
                catch (System.Exception ex)
                {
                    Log.Error($"[SkyfallerCaller] Exception while checking for FlyOver: {ex}");
                    return false;
                }
            }
        }

        private bool CheckRoofConditions
        {
            get
            {
                if (parent?.Map == null) return true;
                
                RoofDef roof = parent.Position.GetRoof(parent.Map);
                if (roof == null) return true;
                
                if (roof.isThickRoof && !Props.allowThickRoof) return false;
                if (!roof.isThickRoof && !Props.allowThinRoof) return false;
                
                return true;
            }
        }

        public bool CanCallSkyfaller => CanCall && HasRequiredFlyOver && CheckRoofConditions;

        // 新增：在建筑生成时检查是否需要自动呼叫
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            
            if (!respawningAfterLoad && ShouldAutoCall)
            {
                // 安排自动呼叫
                autoCallScheduled = true;
                callTick = Find.TickManager.TicksGame + Props.autoCallDelayTicks;
                calling = true;
                
                Log.Message($"[SkyfallerCaller] Scheduled auto-call for non-player building {parent.Label} at tick {callTick}");
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

        public override void CompTick()
        {
            base.CompTick();
            
            // 处理自动呼叫
            if (calling && callTick >= 0 && Find.TickManager.TicksGame >= callTick)
            {
                if (autoCallScheduled)
                {
                    // 执行自动呼叫
                    ExecuteAutoSkyfallerCall();
                }
                else
                {
                    // 执行手动呼叫
                    ExecuteSkyfallerCall();
                }
            }
        }

        // 新增：自动呼叫方法（非玩家派系专用）
        protected virtual void ExecuteAutoSkyfallerCall()
        {
            try
            {
                Log.Message($"[SkyfallerCaller] Executing auto skyfaller call for non-player building at {parent.Position}");
                
                if (Props.skyfallerDef == null)
                {
                    Log.Error("[SkyfallerCaller] Skyfaller def is null!");
                    ResetCall();
                    return;
                }

                // 非玩家派系自动呼叫不需要资源检查
                // 直接处理屋顶
                HandleRoofDestruction();

                // 创建Skyfaller
                Skyfaller skyfaller = SkyfallerMaker.MakeSkyfaller(Props.skyfallerDef);
                if (skyfaller == null)
                {
                    Log.Error("[SkyfallerCaller] Failed to create skyfaller!");
                    ResetCall();
                    return;
                }

                IntVec3 spawnPos = parent.Position;
                Log.Message($"[SkyfallerCaller] Spawning auto skyfaller at {spawnPos}");
                
                GenSpawn.Spawn(skyfaller, spawnPos, parent.Map);
                
                if (Props.destroyBuilding)
                {
                    Log.Message($"[SkyfallerCaller] Destroying non-player building {parent.Label}");
                    parent.Destroy(DestroyMode.Vanish);
                }
                
                // 重置状态
                ResetCall();
                autoCallScheduled = false;
                
                // 显示自动呼叫消息
                Messages.Message("WULA_AutoSkyfallerCalled".Translate(parent.Faction.Name), 
                    MessageTypeDefOf.NeutralEvent);
            }
            catch (System.Exception ex)
            {
                Log.Error($"[SkyfallerCaller] Error in ExecuteAutoSkyfallerCall: {ex}");
                ResetCall();
            }
        }

        public void CallSkyfaller(bool isAutoCall = false)
        {
            // 新增：非玩家派系不能手动呼叫
            if (IsNonPlayerFaction && !isAutoCall)
            {
                Messages.Message("WULA_NonPlayerCannotCall".Translate(), parent, MessageTypeDefOf.RejectInput);
                return;
            }

            if (!CanCallSkyfaller)
            {
                // 显示相应的错误消息
                if (!HasRequiredFlyOver && Props.requireFlyOver)
                {
                    Messages.Message("WULA_NoBuildingDropperFlyOver".Translate(), parent, MessageTypeDefOf.RejectInput);
                }
                else if (!CheckRoofConditions)
                {
                    if (parent.Position.GetRoof(parent.Map)?.isThickRoof == true)
                    {
                        Messages.Message("WULA_ThickRoofBlocking".Translate(), parent, MessageTypeDefOf.RejectInput);
                    }
                    else
                    {
                        Messages.Message("WULA_RoofBlocking".Translate(), parent, MessageTypeDefOf.RejectInput);
                    }
                }
                return;
            }

            Log.Message($"[SkyfallerCaller] Starting skyfaller call from {parent.Label} at {parent.Position}");
            
            calling = true;
            used = true;
            int delay = isAutoCall ? Props.autoCallDelayTicks : Props.delayTicks;
            callTick = Find.TickManager.TicksGame + delay;

            if (delay <= 0)
            {
                ExecuteSkyfallerCall();
            }
            else
            {
                // 修改：根据资源来源显示不同的消息
                string messageKey = usedGlobalStorage ? 
                    "WULA_SkyfallerIncomingFromGlobal" : 
                    "WULA_SkyfallerIncoming";
                Messages.Message(messageKey.Translate(delay.ToStringTicksToPeriod()), parent, MessageTypeDefOf.ThreatBig);
            }
        }

        protected void ResetCall()
        {
            calling = false;
            used = false;
            callTick = -1;
            usedGlobalStorage = false;
            autoCallScheduled = false;
        }

        protected virtual void ExecuteSkyfallerCall()
        {
            Log.Message($"[SkyfallerCaller] Executing skyfaller call at {parent.Position}");
            
            if (Props.skyfallerDef == null)
            {
                Log.Error("[SkyfallerCaller] Skyfaller def is null!");
                return;
            }

            // 修改：使用新的资源检查方法
            var resourceCheck = CheckAndConsumeMaterials();
            if (!resourceCheck.HasEnoughMaterials)
            {
                Log.Message($"[SkyfallerCaller] Aborting skyfaller call due to insufficient materials.");
                ResetCall();
                return;
            }

            // 记录是否使用了全局储存器
            usedGlobalStorage = resourceCheck.UsedGlobalStorage;
            
            // 检查屋顶并处理
            HandleRoofDestruction();

            // 创建Skyfaller
            Skyfaller skyfaller = SkyfallerMaker.MakeSkyfaller(Props.skyfallerDef);
            if (skyfaller == null)
            {
                Log.Error("[SkyfallerCaller] Failed to create skyfaller!");
                return;
            }

            IntVec3 spawnPos = parent.Position;
            Log.Message($"[SkyfallerCaller] Spawning skyfaller at {spawnPos}");
            
            GenSpawn.Spawn(skyfaller, spawnPos, parent.Map);
            
            if (Props.destroyBuilding)
            {
                Log.Message($"[SkyfallerCaller] Destroying building {parent.Label}");
                parent.Destroy(DestroyMode.Vanish);
            }
            
            calling = false;
            callTick = -1;
        }

        private void HandleRoofDestruction()
        {
            if (parent?.Map == null) return;
            
            IntVec3 targetPos = parent.Position;
            RoofDef roof = targetPos.GetRoof(parent.Map);
            
            if (roof != null && !roof.isThickRoof && Props.allowThinRoof)
            {
                Log.Message($"[SkyfallerCaller] Destroying thin roof at {targetPos}");
                parent.Map.roofGrid.SetRoof(targetPos, null);
                
                // 生成屋顶破坏效果
                FleckMaker.ThrowDustPuffThick(targetPos.ToVector3Shifted(), parent.Map, 2f, new Color(1f, 1f, 1f, 2f));
            }
        }

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

        // 新增：资源检查结果结构
        protected struct ResourceCheckResult
        {
            public bool HasEnoughMaterials;
            public bool UsedGlobalStorage;
            public Dictionary<ThingDef, int> BeaconMaterials;
            public Dictionary<ThingDef, int> GlobalMaterials;
        }

        // 修改：新的资源检查方法，优先检查信标附近，然后检查全局储存器
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

            // 第一步：收集信标附近的可用物资
            var beaconMaterials = CollectBeaconMaterials();
            result.BeaconMaterials = beaconMaterials;

            // 第二步：检查信标附近物资是否足够
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

            // 第三步：如果信标附近物资足够，只消耗信标附近的
            if (beaconHasEnough)
            {
                ConsumeBeaconMaterials(beaconMaterials, costList);
                result.HasEnoughMaterials = true;
                result.UsedGlobalStorage = false;
                return result;
            }

            // 第四步：如果信标附近物资不足，检查全局储存器
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
                // 先消耗信标附近的，不足部分从全局储存器扣除
                ConsumeMixedMaterials(beaconMaterials, globalMaterials, costList);
                result.HasEnoughMaterials = true;
                result.UsedGlobalStorage = true;
                return result;
            }

            // 两种来源加起来都不够
            result.HasEnoughMaterials = false;
            return result;
        }

        // 新增：收集信标附近的物资
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
        protected bool HasEnoughMaterials()
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

        // 原有的 ConsumeMaterials 方法现在只用于 God Mode 情况
        protected void ConsumeMaterials()
        {
            if (DebugSettings.godMode) return;

            // 在非 God Mode 下，这个方法不应该被调用
            // 实际的消耗在 CheckAndConsumeMaterials 中处理
            Log.Warning("[SkyfallerCaller] ConsumeMaterials called in non-God mode, this shouldn't happen");
        }

        // 其余方法保持不变...
        private string GetCostString()
        {
            var costList = CostList;
            if (costList.NullOrEmpty())
            {
                return "";
            }
            var sb = new System.Text.StringBuilder();
            foreach (var cost in costList)
            {
                sb.AppendLine($"  - {cost.thingDef.LabelCap}: {cost.count}");
            }
            return sb.ToString();
        }

        private void CancelCall()
        {
            calling = false;
            used = false;
            callTick = -1;
            usedGlobalStorage = false;
            autoCallScheduled = false;
            Messages.Message("WULA_SkyfallerCallCancelled".Translate(), parent, MessageTypeDefOf.NeutralEvent);
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (var gizmo in base.CompGetGizmosExtra())
                yield return gizmo;

            // 新增：非玩家派系不显示呼叫按钮
            if (IsNonPlayerFaction)
                yield break;

            if (calling)
            {
                Command_Action cancelCommand = new Command_Action
                {
                    defaultLabel = "WULA_CancelSkyfaller".Translate(),
                    defaultDesc = "WULA_CancelSkyfallerDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("UI/Designators/Cancel"),
                    action = CancelCall
                };
                yield return cancelCommand;
            }
            
            if (CanCall)
            {
                string reason = GetDisabledReason();
                Command_Action callCommand = new Command_Action
                {
                    defaultLabel = "WULA_CallSkyfaller".Translate(),
                    defaultDesc = GetCallDescription(),
                    icon = ContentFinder<Texture2D>.Get("Wula/UI/Commands/WULA_DropBuilding"),
                    action = () => CallSkyfaller(false),
                    disabledReason = reason
                };
                if (!string.IsNullOrEmpty(reason))
                {
                    callCommand.Disable(reason);
                }
                yield return callCommand;
            }
        }

        private string GetCallDescription()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("WULA_CallSkyfallerDesc".Translate());

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

            // 新增：显示资源来源信息
            sb.AppendLine().AppendLine().Append("WULA_ResourcePriorityInfo".Translate());

            return sb.ToString();
        }

        private string GetDisabledReason()
        {
            // 新增：非玩家派系不能手动呼叫
            if (IsNonPlayerFaction)
            {
                return "WULA_NonPlayerCannotCall".Translate();
            }

            // 只在需要 FlyOver 时检查并显示相关原因
            if (Props.requireFlyOver && !HasRequiredFlyOver)
            {
                return "WULA_NoBuildingDropperFlyOver".Translate();
            }
            
            if (!CheckRoofConditions)
            {
                // 添加 null 检查
                if (parent?.Map != null)
                {
                    RoofDef roof = parent.Position.GetRoof(parent.Map);
                    if (roof?.isThickRoof == true)
                    {
                        return "WULA_ThickRoofBlocking".Translate();
                    }
                    else
                    {
                        return "WULA_RoofBlocking".Translate();
                    }
                }
            }

            if (!HasEnoughMaterials())
            {
                return "WULA_InsufficientMaterials".Translate();
            }
            
            return null;
        }

        public override string CompInspectStringExtra()
        {
            if (parent?.Map == null)
            {
                return base.CompInspectStringExtra();
            }

            var sb = new System.Text.StringBuilder();

            // 新增：显示自动呼叫状态
            if (autoCallScheduled && calling)
            {
                int ticksLeft = callTick - Find.TickManager.TicksGame;
            }
            else if (calling)
            {
                int ticksLeft = callTick - Find.TickManager.TicksGame;
                if (ticksLeft > 0)
                {
                    string messageKey = usedGlobalStorage ? 
                        "WULA_SkyfallerArrivingInFromGlobal" : 
                        "WULA_SkyfallerArrivingIn";
                    sb.Append(messageKey.Translate(ticksLeft.ToStringTicksToPeriod()));
                }
            }
            else if (!used)
            {
                // 新增：显示非玩家派系自动呼叫信息
                if (IsNonPlayerFaction && Props.canAutoCall)
                {
                    sb.Append("WULA_AutoSkyfallerReady".Translate());
                }
                else
                {
                    sb.Append("WULA_ReadyToCallSkyfaller".Translate());
                }

                if (Props.requireFlyOver && !HasRequiredFlyOver)
                {
                    sb.AppendLine().Append("WULA_MissingBuildingDropperFlyOver".Translate());
                }

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
