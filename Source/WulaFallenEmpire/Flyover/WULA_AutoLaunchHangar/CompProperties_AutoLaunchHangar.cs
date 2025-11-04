using RimWorld;
using Verse;
using System.Collections.Generic;

namespace WulaFallenEmpire
{
    public class CompProperties_AutoLaunchHangar : CompProperties
    {
        public ThingDef aircraftDef; // 对应的战机定义
        public int aircraftCount = 1; // 起飞后提供的战机数量
        public ThingDef skyfallerLeaving; // 起飞时的天空坠落者效果
        public int launchDelayTicks = 60; // 延迟启动的ticks（默认1秒）
        public bool requirePower = true; // 是否需要电力才能启动
        
        public CompProperties_AutoLaunchHangar()
        {
            compClass = typeof(CompAutoLaunchHangar);
        }
    }

    public class CompAutoLaunchHangar : ThingComp
    {
        public CompProperties_AutoLaunchHangar Props => (CompProperties_AutoLaunchHangar)props;
        private bool hasLaunched = false;
        private int spawnTick = -1;
        
        private CompPowerTrader powerComp;
        private CompRefuelable refuelableComp;
        
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            
            if (!respawningAfterLoad)
            {
                // 记录生成时间
                spawnTick = Find.TickManager.TicksAbs;
                hasLaunched = false;
                
                // 缓存其他组件
                powerComp = parent.GetComp<CompPowerTrader>();
                refuelableComp = parent.GetComp<CompRefuelable>();
                
                Log.Message($"AutoLaunchHangar spawned at tick {spawnTick}, will launch in {Props.launchDelayTicks} ticks");
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref hasLaunched, "hasLaunched", false);
            Scribe_Values.Look(ref spawnTick, "spawnTick", -1);
        }

        public override void CompTick()
        {
            base.CompTick();
            
            if (hasLaunched || spawnTick == -1)
                return;
                
            int currentTick = Find.TickManager.TicksAbs;
            if (currentTick - spawnTick >= Props.launchDelayTicks)
            {
                if (CanAutoLaunch())
                {
                    AutoLaunchAircraft();
                }
            }
        }

        private bool CanAutoLaunch()
        {
            // 检查建筑是否完好
            if (parent.HitPoints <= 0)
            {
                Log.Message("AutoLaunch: Hangar is damaged, cannot launch");
                return false;
            }
            
            // 检查电力需求
            if (Props.requirePower && powerComp != null && !powerComp.PowerOn)
            {
                Log.Message("AutoLaunch: No power, cannot launch");
                return false;
            }
            
            // 检查燃料需求
            if (refuelableComp != null && !refuelableComp.HasFuel)
            {
                Log.Message("AutoLaunch: No fuel, cannot launch");
                return false;
            }
            
            // 检查地图是否有效
            if (parent.Map == null)
            {
                Log.Message("AutoLaunch: Map is null, cannot launch");
                return false;
            }
            
            return true;
        }

        private void AutoLaunchAircraft()
        {
            Log.Message($"AutoLaunch: Starting aircraft launch sequence for {parent.Label}");
            
            // 获取全局战机管理器
            WorldComponent_AircraftManager aircraftManager = Find.World.GetComponent<WorldComponent_AircraftManager>();
            
            if (aircraftManager == null)
            {
                Log.Error("AutoLaunch: AircraftManager not found");
                hasLaunched = true;
                return;
            }

            try
            {
                // 立即向全局管理器注册战机
                aircraftManager.AddAircraft(Props.aircraftDef, Props.aircraftCount, parent.Faction);
                
                // 显示消息
                Messages.Message("AircraftAutoLaunched".Translate(Props.aircraftCount, Props.aircraftDef.LabelCap), 
                    parent, MessageTypeDefOf.PositiveEvent);
                
                Log.Message($"AutoLaunch: Successfully added {Props.aircraftCount} {Props.aircraftDef.LabelCap} to global manager");
                
                // 创建起飞效果
                if (Props.skyfallerLeaving != null)
                {
                    CreateAutoTakeoffEffect();
                }
                else
                {
                    // 如果没有定义 Skyfaller，直接销毁建筑
                    parent.Destroy();
                }
                
                hasLaunched = true;
            }
            catch (System.Exception ex)
            {
                Log.Error($"AutoLaunch error: {ex}");
                hasLaunched = true; // 标记为已启动，避免重复尝试
            }
        }

        private void CreateAutoTakeoffEffect()
        {
            try
            {
                // 创建起飞效果
                Thing chemfuel = ThingMaker.MakeThing(ThingDefOf.Chemfuel);
                chemfuel.stackCount = 1;
                
                Skyfaller skyfaller = SkyfallerMaker.MakeSkyfaller(Props.skyfallerLeaving, chemfuel);
                
                IntVec3 takeoffPos = parent.Position;
                
                if (parent.Map == null)
                {
                    Log.Error("AutoLaunch: Map is null during takeoff effect creation");
                    parent.Destroy();
                    return;
                }
                
                // 生成 Skyfaller
                GenSpawn.Spawn(skyfaller, takeoffPos, parent.Map);
                
                Log.Message($"AutoLaunch: Takeoff effect created at {takeoffPos}");
                
                // 销毁原建筑
                parent.Destroy(DestroyMode.Vanish);
            }
            catch (System.Exception ex)
            {
                Log.Error($"AutoLaunch takeoff effect error: {ex}");
                // 如果Skyfaller创建失败，直接销毁建筑
                parent.Destroy(DestroyMode.Vanish);
            }
        }

        // 可选：提供手动触发的Gizmo（如果自动触发失败）
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (!hasLaunched)
            {
                Command_Action manualLaunch = new Command_Action
                {
                    defaultLabel = "ManualLaunchAircraft".Translate(),
                    defaultDesc = "ManualLaunchAircraftDesc".Translate(),
                    icon = TexCommand.Attack,
                    action = () =>
                    {
                        if (CanAutoLaunch())
                        {
                            AutoLaunchAircraft();
                        }
                        else
                        {
                            Messages.Message("CannotManualLaunch".Translate(), MessageTypeDefOf.RejectInput);
                        }
                    }
                };

                // 禁用条件检查
                if (parent.HitPoints <= 0)
                {
                    manualLaunch.Disable("HangarDamaged".Translate());
                }
                else if (Props.requirePower && powerComp != null && !powerComp.PowerOn)
                {
                    manualLaunch.Disable("NoPower".Translate());
                }
                else if (refuelableComp != null && !refuelableComp.HasFuel)
                {
                    manualLaunch.Disable("NoFuel".Translate());
                }

                yield return manualLaunch;
            }
        }

        public override string CompInspectStringExtra()
        {
            if (!hasLaunched)
            {
                int remainingTicks = Props.launchDelayTicks - (Find.TickManager.TicksAbs - spawnTick);
                if (remainingTicks > 0)
                {
                    return "AutoLaunchIn".Translate(remainingTicks.ToStringTicksToPeriod());
                }
                else
                {
                    return "AutoLaunchReady".Translate();
                }
            }
            return null;
        }
    }
}
