// JobDriver_RefuelMech.cs
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using UnityEngine;

namespace WulaFallenEmpire
{
    public class JobDriver_RefuelMech : JobDriver
    {
        private const TargetIndex MechInd = TargetIndex.A;
        private const TargetIndex FuelInd = TargetIndex.B;
        private const int RefuelingDuration = 240; // 基础加注时间
        
        protected Pawn Mech => job.GetTarget(MechInd).Thing as Pawn;
        protected CompMechFuel FuelComp => Mech?.TryGetComp<CompMechFuel>();
        protected Thing Fuel => job.GetTarget(FuelInd).Thing;
        
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            Pawn pawn = this.pawn;
            Job job = this.job;
            LocalTargetInfo target = job.GetTarget(MechInd);
            
            if (!pawn.Reserve(target, job, 1, -1, null, errorOnFailed))
            {
                return false;
            }
            
            if (!pawn.Reserve(job.GetTarget(FuelInd), job, 1, -1, null, errorOnFailed))
            {
                return false;
            }
            
            return true;
        }
        
        protected override IEnumerable<Toil> MakeNewToils()
        {
            // 检查目标是否有效
            this.FailOnDespawnedNullOrForbidden(MechInd);
            this.FailOn(() => FuelComp == null);
            
            // 添加结束条件：燃料已满
            AddEndCondition(() => {
                if (FuelComp.IsFull)
                    return JobCondition.Succeeded;
                return JobCondition.Ongoing;
            });
            
            // 如果不是玩家强制命令，检查是否应该自动加注
            AddFailCondition(() => {
                if (job.playerForced)
                    return false;
                    
                // 获取驾驶员组件
                var pilotComp = Mech.TryGetComp<CompMechPilotHolder>();
                bool hasPilot = pilotComp != null && pilotComp.HasPilots;
                
                // 如果有驾驶员且不是玩家强制命令，不自动加注
                if (hasPilot && !job.playerForced)
                    return true;
                    
                // 检查燃料组件是否允许自动加注
                if (!FuelComp.Props.allowAutoRefuel && !job.playerForced)
                    return true;
                    
                return false;
            });
            
            // 第一步：计算需要多少燃料
            yield return Toils_General.DoAtomic(delegate
            {
                if (FuelComp != null)
                {
                    job.count = FuelComp.GetFuelCountToFullyRefuel();
                }
            });
            
            // 第二步：预留燃料
            Toil reserveFuel = Toils_Reserve.Reserve(FuelInd);
            yield return reserveFuel;
            
            // 第三步：前往燃料位置
            yield return Toils_Goto.GotoThing(FuelInd, PathEndMode.ClosestTouch)
                .FailOnDespawnedNullOrForbidden(FuelInd)
                .FailOnSomeonePhysicallyInteracting(FuelInd);
            
            // 第四步：拿起燃料
            yield return Toils_Haul.StartCarryThing(FuelInd, putRemainderInQueue: false, subtractNumTakenFromJobCount: true)
                .FailOnDestroyedNullOrForbidden(FuelInd);
            
            // 第五步：检查是否有机会拿更多燃料
            yield return Toils_Haul.CheckForGetOpportunityDuplicate(reserveFuel, FuelInd, TargetIndex.None, takeFromValidStorage: true);
            
            // 第六步：前往机甲位置
            yield return Toils_Goto.GotoThing(MechInd, PathEndMode.Touch);
            
            // 第七步：等待加注（有进度条）
            Toil refuelToil = Toils_General.Wait(RefuelingDuration)
                .FailOnDestroyedNullOrForbidden(FuelInd)
                .FailOnDestroyedNullOrForbidden(MechInd)
                .FailOnCannotTouch(MechInd, PathEndMode.Touch)
                .WithProgressBarToilDelay(MechInd);
            
            // 调整加注时间基于燃料组件的速度因子
            refuelToil.defaultDuration = Mathf.RoundToInt(RefuelingDuration / FuelComp.Props.refuelSpeedFactor);
            
            yield return refuelToil;
            
            // 第八步：完成加注 - 模仿 RimWorld 原版实现
            yield return FinalizeRefueling(MechInd, FuelInd);
        }
        
        // 模仿 RimWorld.Toils_Refuel.FinalizeRefueling 的实现
        private static Toil FinalizeRefueling(TargetIndex refuelableInd, TargetIndex fuelInd)
        {
            Toil toil = ToilMaker.MakeToil("FinalizeRefueling");
            toil.initAction = delegate
            {
                Pawn actor = toil.actor;
                Job curJob = actor.CurJob;
                Thing refuelable = curJob.GetTarget(refuelableInd).Thing;
                CompMechFuel fuelComp = refuelable.TryGetComp<CompMechFuel>();
                
                if (fuelComp != null)
                {
                    // 获取所有燃料物品
                    List<Thing> fuelThings;
                    if (actor.CurJob.placedThings.NullOrEmpty())
                    {
                        // 如果没有 placedThings，则使用燃料目标
                        Thing fuel = curJob.GetTarget(fuelInd).Thing;
                        if (fuel != null)
                        {
                            fuelThings = new List<Thing> { fuel };
                        }
                        else
                        {
                            fuelThings = null;
                        }
                    }
                    else
                    {
                        // 使用 placedThings 中的所有燃料物品
                        fuelThings = actor.CurJob.placedThings.Select((ThingCountClass p) => p.thing).ToList();
                    }
                    
                    if (fuelThings != null)
                    {
                        // 计算总燃料量并销毁燃料物品
                        float totalFuel = 0f;
                        foreach (Thing fuelThing in fuelThings)
                        {
                            if (fuelThing != null && fuelThing.def == fuelComp.FuelType)
                            {
                                totalFuel += fuelThing.stackCount;
                                fuelThing.Destroy(DestroyMode.Vanish);
                            }
                        }
                        
                        // 添加燃料到机甲
                        if (totalFuel > 0)
                        {
                            fuelComp.Refuel(totalFuel);
                        }
                    }
                }
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            return toil;
        }
    }
}
