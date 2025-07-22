using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;
using RimWorld;

namespace WulaFallenEmpire
{
    public class JobDriver_FeedWulaPatient : JobDriver
    {
        private const TargetIndex FoodSourceInd = TargetIndex.A;
        private const TargetIndex PatientInd = TargetIndex.B;

        private Thing FoodSource => job.GetTarget(FoodSourceInd).Thing;
        private Pawn Patient => (Pawn)job.GetTarget(PatientInd).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // 预留食物来源和病患
            if (!pawn.Reserve(FoodSource, job, 1, -1, null, errorOnFailed))
            {
                return false;
            }
            if (!pawn.Reserve(Patient, job, 1, -1, null, errorOnFailed))
            {
                return false;
            }
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            // 失败条件：如果病患被摧毁、为空或不在床上
            this.FailOn(() => Patient.DestroyedOrNull());
            this.FailOn(() => !Patient.InBed());

            // Toil 0: 检查医生库存中是否有能量核心
            Toil checkInventoryToil = ToilMaker.MakeToil("CheckInventory");
            checkInventoryToil.initAction = delegate
            {
                Thing inventoryFood = null;
                foreach (Thing t in pawn.inventory.innerContainer)
                {
                    ThingDefExtension_EnergySource energySourceExt = t.def.GetModExtension<ThingDefExtension_EnergySource>();
                    if (energySourceExt != null && t.IngestibleNow)
                    {
                        inventoryFood = t;
                        break;
                    }
                }

                if (inventoryFood != null)
                {
                    // 如果库存中有食物，则将Job的目标设置为库存食物，并跳过拾取步骤，直接前往病患
                    job.SetTarget(FoodSourceInd, inventoryFood);
                    pawn.jobs.curDriver.JumpToToil(Toils_Goto.GotoThing(PatientInd, PathEndMode.Touch)); // 跳转到前往病患的Toil
                }
                // 如果库存中没有，则继续执行下一个Toil（前往地图上的食物来源）
            };
            yield return checkInventoryToil;

            // Toil 1: 前往食物来源 (如果库存中没有，则执行此Toil)
            yield return Toils_Goto.GotoThing(FoodSourceInd, PathEndMode.ClosestTouch)
                .FailOnDespawnedNullOrForbidden(FoodSourceInd)
                .FailOn(() => !pawn.CanReserve(FoodSource, 1, -1, null, false)); // 在这里预留食物来源

            // Toil 2: 拾取食物来源 (如果库存中没有，则执行此Toil)
            yield return Toils_Haul.StartCarryThing(FoodSourceInd); // 使用 StartCarryThing 拾取物品

            // Toil 3: 前往病患
            yield return Toils_Goto.GotoThing(PatientInd, PathEndMode.Touch)
                .FailOnDespawnedOrNull(PatientInd);

            // Toil 4: 喂食病患
            Toil feedToil = ToilMaker.MakeToil("FeedWulaPatient");
            feedToil.initAction = delegate
            {
                Pawn actor = feedToil.actor;
                Thing food = actor.carryTracker.CarriedThing; // 医生携带的食物 (从地图拾取)

                // 如果医生没有携带食物，检查是否在库存中 (从库存获取)
                if (food == null)
                {
                    food = job.GetTarget(FoodSourceInd).Thing; // 此时FoodSourceInd应该指向库存中的物品
                    if (food == null || !actor.inventory.innerContainer.Contains(food))
                    {
                        actor.jobs.EndCurrentJob(JobCondition.Incompletable);
                        return;
                    }
                }

                // 获取乌拉能量需求
                Need_WulaEnergy energyNeed = Patient.needs.TryGetNeed<Need_WulaEnergy>();
                if (energyNeed == null)
                {
                    actor.jobs.EndCurrentJob(JobCondition.Errored);
                    return;
                }

                // 检查食物来源是否有自定义能量扩展
                ThingDefExtension_EnergySource ext = food.def.GetModExtension<ThingDefExtension_EnergySource>();
                if (ext == null)
                {
                    actor.jobs.EndCurrentJob(JobCondition.Errored);
                    return;
                }

                // 补充乌拉的能量
                energyNeed.CurLevel += ext.energyAmount;

                // 消耗物品
                if (actor.carryTracker.CarriedThing == food) // 如果是携带的物品
                {
                    food.Destroy(DestroyMode.Vanish); // 销毁医生携带的物品
                    actor.carryTracker.innerContainer.ClearAndDestroyContents(); // 移除医生携带的物品
                }
                else if (actor.inventory.innerContainer.Contains(food)) // 如果是库存中的物品
                {
                    food.stackCount--; // 减少库存物品数量
                    if (food.stackCount <= 0)
                    {
                        food.Destroy(DestroyMode.Vanish); // 如果数量为0，销毁物品
                    }
                }
                else
                {
                    // 理论上不应该发生
                    actor.jobs.EndCurrentJob(JobCondition.Errored);
                    return;
                }

                // 记录能量摄入 (可选)
                // Patient.records.AddTo(RecordDefOf.NutritionEaten, ext.energyAmount);
            };
            feedToil.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return feedToil;
        }
    }
}
