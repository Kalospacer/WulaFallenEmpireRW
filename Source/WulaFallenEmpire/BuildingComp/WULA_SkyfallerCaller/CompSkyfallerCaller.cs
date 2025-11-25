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

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (var gizmo in base.CompGetGizmosExtra())
                yield return gizmo;

            if (CanCall)
            {
                Command_Action callCommand = new Command_Action
                {
                    defaultLabel = "WULA_CallSkyfaller".Translate(),
                    defaultDesc = GetCallDescription(),
                    icon = ContentFinder<Texture2D>.Get("Wula/UI/Commands/WULA_DropBuilding"),
                    action = () => CallSkyfaller(false),
                    disabledReason = GetDisabledReason()
                };
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
            string desc = "WULA_CallSkyfallerDesc".Translate();
            
            // 只在需要 FlyOver 时显示相关描述
            if (Props.requireFlyOver && !HasRequiredFlyOver)
            {
                desc += $"\n{"WULA_RequiresBuildingDropperFlyOver".Translate()}";
            }
            
            // 添加 null 检查
            if (parent?.Map != null)
            {
                RoofDef roof = parent.Position.GetRoof(parent.Map);
                if (roof != null)
                {
                    if (roof.isThickRoof && !Props.allowThickRoof)
                    {
                        desc += $"\n{"WULA_ThickRoofBlockingDesc".Translate()}";
                    }
                    else if (!roof.isThickRoof && !Props.allowThinRoof)
                    {
                        desc += $"\n{"WULA_RoofBlockingDesc".Translate()}";
                    }
                }
            }
            
            return desc;
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
            
            return null;
        }

        public override string CompInspectStringExtra()
        {
            // 添加 null 检查，防止在小型化建筑上出现异常
            if (parent?.Map == null)
                return base.CompInspectStringExtra();

            if (calling)
            {
                int ticksLeft = callTick - Find.TickManager.TicksGame;
                if (ticksLeft > 0)
                {
                    return "WULA_SkyfallerArrivingIn".Translate(ticksLeft.ToStringTicksToPeriod());
                }
            }
            else if (!used)
            {
                string status = "WULA_ReadyToCallSkyfaller".Translate();
                
                // 只在需要 FlyOver 时显示相关条件信息
                if (Props.requireFlyOver && !HasRequiredFlyOver)
                {
                    status += $"\n{"WULA_MissingBuildingDropperFlyOver".Translate()}";
                }
                
                // 添加 null 检查
                if (parent?.Map != null)
                {
                    RoofDef roof = parent.Position.GetRoof(parent.Map);
                    if (roof != null)
                    {
                        if (roof.isThickRoof && !Props.allowThickRoof)
                        {
                            status += $"\n{"WULA_BlockedByThickRoof".Translate()}";
                        }
                        else if (!roof.isThickRoof && !Props.allowThinRoof)
                        {
                            status += $"\n{"WULA_BlockedByRoof".Translate()}";
                        }
                    }
                }
                
                return status;
            }
            
            return base.CompInspectStringExtra();
        }
    }
}
