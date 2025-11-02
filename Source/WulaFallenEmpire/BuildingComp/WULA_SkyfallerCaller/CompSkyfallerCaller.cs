using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    public class CompProperties_SkyfallerCaller : CompProperties
    {
        public ThingDef skyfallerDef;
        public bool destroyBuilding = true;
        public int delayTicks = 0;
        public string requiredFlyOverType = "default"; // 需要的 FlyOver 类型
        public bool allowThinRoof = true; // 允许砸穿薄屋顶
        public bool allowThickRoof = false; // 是否允许在厚岩顶下空投
        public string requiredFlyOverLabel = "FlyOver"; // 显示给玩家的标签
        
        public CompProperties_SkyfallerCaller()
        {
            compClass = typeof(CompSkyfallerCaller);
        }
    }

    public class CompSkyfallerCaller : ThingComp
    {
        private CompProperties_SkyfallerCaller Props => (CompProperties_SkyfallerCaller)props;
        
        private bool used = false;
        private int callTick = -1;
        private bool calling = false;

        public bool CanCall => !used && !calling;

        // 获取所需的 FlyOver 显示标签
        public string RequiredFlyOverLabel
        {
            get
            {
                // 优先使用建筑配置的显示标签
                if (!Props.requiredFlyOverLabel.NullOrEmpty())
                    return Props.requiredFlyOverLabel;
                    
                // 如果没有配置，回退到类型名称
                return Props.requiredFlyOverType;
            }
        }

        // 检查是否有对应类型的 FlyOver
        public bool HasRequiredFlyOver
        {
            get
            {
                if (parent?.Map == null) return false;
                
                // 查找地图上所有具有 FlyOverType 组件的物体
                List<Thing> allThings = parent.Map.listerThings.AllThings;
                int flyOverCount = 0;
                int matchingTypeCount = 0;
                
                foreach (Thing thing in allThings)
                {
                    var typeComp = thing.TryGetComp<CompFlyOverType>();
                    if (typeComp != null)
                    {
                        flyOverCount++;
                        if (typeComp.FlyOverType == Props.requiredFlyOverType && typeComp.IsRequiredForDrop)
                        {
                            matchingTypeCount++;
                            Log.Message($"[SkyfallerCaller] Found required FlyOver of type: {Props.requiredFlyOverType} at {thing.Position}");
                        }
                    }
                }
                
                Log.Message($"[SkyfallerCaller] Searched {allThings.Count} things, found {flyOverCount} FlyOvers, {matchingTypeCount} matching type: {Props.requiredFlyOverType}");
                
                return matchingTypeCount > 0;
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
                    Log.Message($"[SkyfallerCaller] Cannot call: missing required FlyOver type: {Props.requiredFlyOverType}");
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

        public void CallSkyfaller()
        {
            if (!CanCallSkyfaller)
            {
                // 显示相应的错误消息
                if (!HasRequiredFlyOver)
                {
                    Messages.Message("WULA_NoRequiredFlyOver".Translate(RequiredFlyOverLabel), parent, MessageTypeDefOf.RejectInput);
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
            callTick = Find.TickManager.TicksGame + Props.delayTicks;

            if (Props.delayTicks <= 0)
            {
                ExecuteSkyfallerCall();
            }
            else
            {
                Messages.Message("WULA_SkyfallerIncoming".Translate(Props.delayTicks.ToStringTicksToPeriod()), parent, MessageTypeDefOf.ThreatBig);
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

            if (!CanCall)
                yield break;

            Command_Action callCommand = new Command_Action
            {
                defaultLabel = "WULA_CallSkyfaller".Translate(),
                defaultDesc = GetCallDescription(),
                icon = ContentFinder<Texture2D>.Get("Wula/UI/Commands/WULA_DropBuilding"),
                action = CallSkyfaller,
                disabledReason = GetDisabledReason()
            };

            yield return callCommand;
        }

        private string GetCallDescription()
        {
            string desc = "WULA_CallSkyfallerDesc".Translate();
            
            if (!HasRequiredFlyOver)
            {
                desc += $"\n{"WULA_RequiresFlyOver".Translate(RequiredFlyOverLabel)}";
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
            if (!HasRequiredFlyOver)
            {
                return "WULA_NoRequiredFlyOver".Translate(RequiredFlyOverLabel);
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
                
                // 添加条件信息
                if (!HasRequiredFlyOver)
                {
                    status += $"\n{"WULA_MissingFlyOver".Translate(RequiredFlyOverLabel)}";
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
