// CompMechFuel.cs
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;
using System.Linq;

namespace WulaFallenEmpire
{
    public class CompMechFuel : ThingComp
    {
        public CompProperties_MechFuel Props => (CompProperties_MechFuel)props;
        
        private float fuel;
        private bool isShutdown = false;
        private int lastFuelTick = -1;
        
        public float Fuel => fuel;
        public float FuelPercent => fuel / Props.fuelCapacity;
        public bool HasFuel => fuel > 0f;
        public bool IsFull => FuelPercent >= 0.999f;
        public bool NeedsRefueling => FuelPercent < Props.autoRefuelThreshold && Props.allowAutoRefuel;
        public bool IsShutdown => isShutdown;
        
        public ThingDef FuelType => Props.fuelType;
        
        // 停机状态 Hediff
        private HediffDef ShutdownHediffDef => HediffDef.Named("WULA_MechShutdown");
        
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            lastFuelTick = Find.TickManager.TicksGame;
            
            // 如果是新生成的机甲，自动加满燃料
            if (!respawningAfterLoad && fuel <= 0f)
            {
                Refuel(Props.fuelCapacity);
            }
            
            // 确保停机状态和 Hediff 同步
            SyncShutdownHediff();
        }
        
        public override void CompTick()
        {
            base.CompTick();
            
            // 每60ticks（1秒）消耗一次燃料
            if (Find.TickManager.TicksGame % 60 == 0)
            {
                ConsumeFuelOverTime();
            }
            
            // 检查是否需要关机
            CheckShutdown();
            
            // 确保停机状态和 Hediff 同步
            SyncShutdownHediff();
        }
        
        private void SyncShutdownHediff()
        {
            var mech = parent as Pawn;
            if (mech == null || mech.health == null || ShutdownHediffDef == null)
                return;
            
            bool hasShutdownHediff = mech.health.hediffSet.HasHediff(ShutdownHediffDef);
            
            // 如果处于停机状态但没有 Hediff，添加 Hediff
            if (isShutdown && !hasShutdownHediff)
            {
                mech.health.AddHediff(ShutdownHediffDef);
            }
            // 如果不处于停机状态但有 Hediff，移除 Hediff
            else if (!isShutdown && hasShutdownHediff)
            {
                var hediff = mech.health.hediffSet.GetFirstHediffOfDef(ShutdownHediffDef);
                if (hediff != null)
                {
                    mech.health.RemoveHediff(hediff);
                }
            }
        }
        
        private void ConsumeFuelOverTime()
        {
            if (fuel <= 0f || !parent.Spawned)
                return;
            
            // 检查是否有驾驶员 - 没有驾驶员时不消耗燃料
            var pilotComp = parent.TryGetComp<CompMechPilotHolder>();
            bool hasPilot = pilotComp != null && pilotComp.HasPilots;
            
            if (!hasPilot)
                return; // 没有驾驶员，不消耗燃料
            
            // 获取当前时间
            int currentTick = Find.TickManager.TicksGame;
            
            // 计算经过的游戏时间（以天为单位）
            float daysPassed = (currentTick - lastFuelTick) / 60000f; // 60000 ticks = 1天
            
            // 计算基础燃料消耗
            float consumption = Props.dailyFuelConsumption * daysPassed;
            
            // 如果机甲正在活动（移动、战斗等），消耗更多燃料
            var mech = parent as Pawn;
            if (mech != null)
            {
                // 增加活动消耗
                if (mech.pather.Moving)
                {
                    consumption *= 2f; // 移动时消耗加倍
                }
                
                // 战斗状态消耗更多
                if (mech.CurJob != null && mech.CurJob.def == JobDefOf.AttackStatic)
                {
                    consumption *= 1.5f;
                }
            }
            
            // 消耗燃料
            fuel = Mathf.Max(0f, fuel - consumption);
            
            // 更新最后消耗时间
            lastFuelTick = currentTick;
        }
        
        private void CheckShutdown()
        {
            if (!Props.shutdownWhenEmpty || isShutdown)
                return;
            
            // 燃料耗尽时关机
            if (fuel <= 0f && parent.Spawned)
            {
                Shutdown();
            }
        }
        
        private void Shutdown()
        {
            if (isShutdown)
                return;
            
            isShutdown = true;
            
            var mech = parent as Pawn;
            if (mech != null)
            {
                // 取消所有工作
                mech.jobs.StopAll();
                mech.drafter.Drafted = false;
                
                // 添加停机 Hediff
                if (ShutdownHediffDef != null)
                {
                    mech.health.AddHediff(ShutdownHediffDef);
                }
                
                // 播放关机效果
                MoteMaker.ThrowText(mech.DrawPos, mech.Map, "WULA_Shutdown".Translate(), Color.gray, 3.5f);
            }
        }
        
        public void Startup()
        {
            if (!isShutdown || fuel <= 0f)
                return;
            
            isShutdown = false;
            
            var mech = parent as Pawn;
            if (mech != null)
            {
                // 移除停机 Hediff
                if (ShutdownHediffDef != null)
                {
                    var hediff = mech.health.hediffSet.GetFirstHediffOfDef(ShutdownHediffDef);
                    if (hediff != null)
                    {
                        mech.health.RemoveHediff(hediff);
                    }
                }
                
                MoteMaker.ThrowText(mech.DrawPos, mech.Map, "WULA_Startup".Translate(), Color.green, 3.5f);
            }
        }
        
        public bool TryConsumeFuel(float amount)
        {
            if (fuel >= amount)
            {
                fuel -= amount;
                return true;
            }
            return false;
        }
        
        public bool Refuel(float amount)
        {
            float oldFuel = fuel;
            fuel = Mathf.Min(Props.fuelCapacity, fuel + amount);
            
            // 如果之前关机了且现在有燃料了，启动
            if (isShutdown && fuel > 0f)
            {
                Startup();
            }
            
            return fuel > oldFuel;
        }
        
        // 设置燃料到特定值（测试用）
        public void SetFuel(float amount)
        {
            float oldFuel = fuel;
            fuel = Mathf.Clamp(amount, 0f, Props.fuelCapacity);
            
            // 检查是否需要关机或启动
            if (fuel <= 0f)
            {
                if (!isShutdown && Props.shutdownWhenEmpty)
                {
                    Shutdown();
                }
            }
            else if (isShutdown && fuel > 0f)
            {
                Startup();
            }
            
            // 发送调试消息
            if (DebugSettings.godMode)
            {
                Messages.Message($"WULA_Debug_FuelSet".Translate(
                    parent.LabelShort, 
                    fuel.ToString("F1"), 
                    Props.fuelCapacity.ToString("F1"),
                    (fuel / Props.fuelCapacity * 100f).ToString("F0") + "%"
                ), parent, MessageTypeDefOf.PositiveEvent);
            }
        }
        
        public float FuelSpaceRemaining => Mathf.Max(0f, Props.fuelCapacity - fuel);
        
        public int GetFuelCountToFullyRefuel()
        {
            if (FuelType == null)
                return 0;
                
            float fuelNeeded = FuelSpaceRemaining;
            return Mathf.CeilToInt(fuelNeeded);
        }
        
        // 立刻加注 - 现在触发最近殖民者来加注
        public void RefuelNow()
        {
            if (IsFull || parent.Map == null || FuelType == null)
                return;
            
            // 寻找最近的可用殖民者
            Pawn bestColonist = FindBestColonistForRefuel();
            
            if (bestColonist == null)
            {
                Messages.Message("WULA_NoColonistAvailable".Translate(), parent, MessageTypeDefOf.RejectInput);
                return;
            }
            
            // 寻找燃料
            Thing fuel = FindFuelForRefuel(bestColonist);
            if (fuel == null)
            {
                Messages.Message("WULA_NoFuelAvailable".Translate(FuelType), parent, MessageTypeDefOf.RejectInput);
                return;
            }
            
            // 为殖民者创建强制加注工作
            Job job = JobMaker.MakeJob(Wula_JobDefOf.WULA_RefuelMech, parent, fuel);
            job.count = GetFuelCountToFullyRefuel();
            job.playerForced = true;
            
            bestColonist.jobs.StartJob(job, JobCondition.InterruptForced, null, resumeCurJobAfterwards: true);
            
            // 显示消息
            Messages.Message("WULA_OrderedRefuel".Translate(bestColonist.LabelShort, parent.LabelShort),
                parent, MessageTypeDefOf.PositiveEvent);
        }
        
        private Pawn FindBestColonistForRefuel()
        {
            Map map = parent.Map;
            if (map == null)
                return null;
            
            // 寻找所有可用的殖民者
            List<Pawn> colonists = map.mapPawns.FreeColonists.ToList();
            
            // 过滤掉无法工作或无法到达机甲的殖民者
            colonists = colonists.Where(colonist => 
                colonist.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation) &&
                colonist.health.capacities.CapableOf(PawnCapacityDefOf.Moving) &&
                !colonist.Downed &&
                !colonist.Dead &&
                colonist.CanReserveAndReach(parent, PathEndMode.Touch, Danger.Some)
            ).ToList();
            
            if (!colonists.Any())
                return null;
            
            // 按照距离排序，选择最近的殖民者
            return colonists.OrderBy(colonist => colonist.Position.DistanceTo(parent.Position)).FirstOrDefault();
        }
        
        private Thing FindFuelForRefuel(Pawn colonist)
        {
            if (FuelType == null)
                return null;
            
            // 先在殖民者库存中寻找
            if (colonist.inventory != null)
            {
                Thing fuelInInventory = colonist.inventory.innerContainer.FirstOrDefault(t => t.def == FuelType);
                if (fuelInInventory != null)
                    return fuelInInventory;
            }
            
            // 在地图上寻找可用的燃料
            return GenClosest.ClosestThingReachable(
                colonist.Position,
                colonist.Map,
                ThingRequest.ForDef(FuelType),
                PathEndMode.ClosestTouch,
                TraverseParms.For(colonist),
                9999f,
                validator: thing => !thing.IsForbidden(colonist) && colonist.CanReserve(thing)
            );
        }
        
        public bool CanRefuelNow()
        {
            if (IsFull || parent.Map == null || FuelType == null)
                return false;
            
            // 检查是否有可用殖民者
            if (FindBestColonistForRefuel() == null)
                return false;
            
            return true;
        }
        
        // 检查机甲是否有驾驶员
        public bool HasPilot()
        {
            var pilotComp = parent.TryGetComp<CompMechPilotHolder>();
            return pilotComp != null && pilotComp.HasPilots;
        }
        
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            // 添加燃料状态Gizmo
            yield return new Gizmo_MechFuelStatus(this);
            
            // 添加立刻加注按钮
            if (!IsFull && parent.Faction == Faction.OfPlayer)
            {
                Command_Action refuelNow = new Command_Action
                {
                    defaultLabel = "WULA_RefuelNow".Translate(),
                    defaultDesc = "WULA_RefuelNowDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("Wula/UI/Commands/WULA_Refuel_Mech"),
                    action = () => RefuelNow()
                };
                
                // 检查是否可以立刻加注
                if (!CanRefuelNow())
                {
                    refuelNow.Disable("WULA_CannotRefuelNow".Translate());
                }
                
                yield return refuelNow;
            }
            
            // 在 God Mode 下显示测试按钮
            if (DebugSettings.godMode && parent.Faction == Faction.OfPlayer)
            {
                // 设置燃料为空
                Command_Action setEmpty = new Command_Action
                {
                    defaultLabel = "WULA_Debug_SetEmpty".Translate(),
                    defaultDesc = "WULA_Debug_SetEmptyDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/SetEmpty", false) ?? BaseContent.BadTex,
                    action = () => SetFuel(0f)
                };
                yield return setEmpty;
                
                // 设置燃料为50%
                Command_Action setHalf = new Command_Action
                {
                    defaultLabel = "WULA_Debug_SetHalf".Translate(),
                    defaultDesc = "WULA_Debug_SetHalfDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/SetHalf", false) ?? BaseContent.BadTex,
                    action = () => SetFuel(Props.fuelCapacity * 0.5f)
                };
                yield return setHalf;
                
                // 设置燃料为满
                Command_Action setFull = new Command_Action
                {
                    defaultLabel = "WULA_Debug_SetFull".Translate(),
                    defaultDesc = "WULA_Debug_SetFullDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/SetFull", false) ?? BaseContent.BadTex,
                    action = () => SetFuel(Props.fuelCapacity)
                };
                yield return setFull;
            }
        }
        
        public override void PostExposeData()
        {
            base.PostExposeData();
            
            Scribe_Values.Look(ref fuel, "fuel", Props.fuelCapacity);
            Scribe_Values.Look(ref isShutdown, "isShutdown", false);
            Scribe_Values.Look(ref lastFuelTick, "lastFuelTick", -1);
        }
        
        public override string CompInspectStringExtra()
        {
            string baseString = base.CompInspectStringExtra();
            
            string fuelString = "WULA_Fuel".Translate(FuelType) + ": " + 
                fuel.ToString("F1") + " / " + Props.fuelCapacity.ToString("F1") + 
                " (" + (FuelPercent * 100f).ToString("F0") + "%)";

            if (!baseString.NullOrEmpty())
                return baseString + "\n" + fuelString;
            else
                return fuelString;
        }
    }
}
