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
        private CompProperties_SkyfallerCaller Props => (CompProperties_SkyfallerCaller)props;

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
        
        private bool used = false;
        private int callTick = -1;
        private bool calling = false;

        public bool CanCall => !used && !calling;

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
                    Log.Error($"[SkyfallerCaller] Error in HasRequiredFlyOver: {ex}");
                    return false;
                }
            }
        }

        // 检查屋顶条件
        public bool CheckRoofConditions
        {
            get
            {
                if (parent?.Map == null) return false;
                
                IntVec3 targetPos = parent.Position;
                RoofDef roof = targetPos.GetRoof(parent.Map);
                
                if (roof == null)
                {
                    Log.Message($"[SkyfallerCaller] No roof at target position, skyfaller allowed");
                    return true; // 没有屋顶，允许空投
                }
                
                if (roof.isThickRoof)
                {
                    Log.Message($"[SkyfallerCaller] Thick roof detected at target position: {roof.defName}");
                    return Props.allowThickRoof; // 厚岩顶，根据配置决定
                }
                else
                {
                    Log.Message($"[SkyfallerCaller] Thin roof detected at target position: {roof.defName}");
                    return Props.allowThinRoof; // 薄屋顶，根据配置决定
                }
            }
        }

        // 检查所有召唤条件
        public bool CanCallSkyfaller
        {
            get
            {
                if (!CanCall)
                {
                    Log.Message($"[SkyfallerCaller] Cannot call: already used or calling");
                    return false;
                }
                
                if (!HasRequiredFlyOver)
                {
                    Log.Message($"[SkyfallerCaller] Cannot call: missing required FlyOver with BuildingdropperFacility");
                    return false;
                }
                
                if (!CheckRoofConditions)
                {
                    Log.Message($"[SkyfallerCaller] Cannot call: roof conditions not met");
                    return false;
                }

                if (!HasEnoughMaterials())
                {
                    Log.Message($"[SkyfallerCaller] Cannot call: insufficient materials");
                    return false;
                }
                
                Log.Message($"[SkyfallerCaller] All conditions met for skyfaller call");
                return true;
            }
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (!respawningAfterLoad && Props.canAutoCall && WorldComp.AutoCallSkyfaller && CanCallSkyfaller)
            {
                CallSkyfaller(true);
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref used, "used", false);
            Scribe_Values.Look(ref callTick, "callTick", -1);
            Scribe_Values.Look(ref calling, "calling", false);
        }

        public override void CompTick()
        {
            base.CompTick();
            
            if (calling && callTick >= 0 && Find.TickManager.TicksGame >= callTick)
            {
                ExecuteSkyfallerCall();
            }
        }

        public void CallSkyfaller(bool isAutoCall = false)
        {
            if (!CanCallSkyfaller)
            {
                // 显示相应的错误消息
                if (!HasRequiredFlyOver && Props.requireFlyOver) // 只在需要 FlyOver 时才显示此消息
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
                Messages.Message("WULA_SkyfallerIncoming".Translate(delay.ToStringTicksToPeriod()), parent, MessageTypeDefOf.ThreatBig);
            }
        }

        private void ExecuteSkyfallerCall()
        {
            Log.Message($"[SkyfallerCaller] Executing skyfaller call at {parent.Position}");
            
            if (Props.skyfallerDef == null)
            {
                Log.Error("[SkyfallerCaller] Skyfaller def is null!");
                return;
            }

            if (!HasEnoughMaterials())
            {
                Log.Message($"[SkyfallerCaller] Aborting skyfaller call due to insufficient materials.");
                calling = false;
                used = false;
                callTick = -1;
                return;
            }

            ConsumeMaterials();
            
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

        private List<ThingDefCountClass> CostList
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

        private bool HasEnoughMaterials()
        {
            var costList = CostList;
            if (costList.NullOrEmpty())
            {
                return true;
            }

            var availableThings = new List<Thing>();
            if (parent.Map == null) return false;

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
                            availableThings.Add(thing);
                        }
                    }
                }
            }
    
            availableThings = availableThings.Distinct().ToList();

            foreach (var cost in costList)
            {
                int count = 0;
                foreach (var thing in availableThings)
                {
                    if (thing.def == cost.thingDef)
                    {
                        count += thing.stackCount;
                    }
                }
                if (count < cost.count)
                {
                    return false;
                }
            }

            return true;
        }

        private void ConsumeMaterials()
        {
            var costList = CostList;
            if (costList.NullOrEmpty())
            {
                return;
            }

            var tradeableThings = new List<Thing>();
            if (parent.Map == null) return;

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
    
            tradeableThings = tradeableThings.Distinct().ToList();

            foreach (var cost in costList)
            {
                int remaining = cost.count;
                for (int i = tradeableThings.Count - 1; i >= 0; i--)
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
                        }
                        if (remaining <= 0)
                        {
                            break;
                        }
                    }
                }
            }
        }

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
            Messages.Message("WULA_SkyfallerCallCancelled".Translate(), parent, MessageTypeDefOf.NeutralEvent);
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (var gizmo in base.CompGetGizmosExtra())
                yield return gizmo;

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

            if (Props.canAutoCall)
            {
                Command_Toggle toggleAutoCall = new Command_Toggle
                {
                    defaultLabel = "WULA_ToggleAutoCallSkyfaller".Translate(),
                    defaultDesc = "WULA_ToggleAutoCallSkyfallerDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("Wula/UI/Commands/WULA_DropBuilding"),
                    isActive = () => WorldComp.AutoCallSkyfaller,
                    toggleAction = () =>
                    {
                        WorldComp.AutoCallSkyfaller = !WorldComp.AutoCallSkyfaller;
                        if (WorldComp.AutoCallSkyfaller)
                        {
                            Messages.Message("WULA_AutoCallEnabled".Translate(), MessageTypeDefOf.PositiveEvent);
                        }
                        else
                        {
                            Messages.Message("WULA_AutoCallDisabled".Translate(), MessageTypeDefOf.NegativeEvent);
                        }
                    }
                };
                yield return toggleAutoCall;
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

            return sb.ToString();
        }

        private string GetDisabledReason()
        {
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

            if (calling)
            {
                int ticksLeft = callTick - Find.TickManager.TicksGame;
                if (ticksLeft > 0)
                {
                    sb.Append("WULA_SkyfallerArrivingIn".Translate(ticksLeft.ToStringTicksToPeriod()));
                }
            }
            else if (!used)
            {
                sb.Append("WULA_ReadyToCallSkyfaller".Translate());

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
