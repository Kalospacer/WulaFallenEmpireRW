using System.Collections.Generic;
using System.Linq; // Added for FirstOrDefault
using UnityEngine;
using Verse;
using Verse.AI;
using RimWorld; // For ThingDefOf, StatDefOf, etc.

namespace WulaFallenEmpire
{
    public class JobDriver_IngestWulaEnergy : JobDriver
    {
        private const TargetIndex IngestibleSourceInd = TargetIndex.A;
        private bool eatingFromInventory; // 新增字段

        private Toil chewing; // 新增咀嚼Toil字段

        private Thing IngestibleSource => job.GetTarget(IngestibleSourceInd).Thing;

        // 新增咀嚼时间乘数属性
        private float ChewDurationMultiplier
        {
            get
            {
                Thing ingestibleSource = IngestibleSource;
                // 假设乌拉能量核心也有EatingSpeed属性影响咀嚼速度，或者固定为1f
                return 1f / pawn.GetStatValue(StatDefOf.EatingSpeed);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref eatingFromInventory, "eatingFromInventory", defaultValue: false);
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (pawn.Faction != null)
            {
                Thing ingestibleSource = IngestibleSource;
                // 使用FoodUtility.GetMaxAmountToPickup
                int maxAmountToPickup = FoodUtility.GetMaxAmountToPickup(ingestibleSource, pawn, job.count);
                if (!pawn.Reserve(ingestibleSource, job, 10, maxAmountToPickup, null, errorOnFailed))
                {
                    return false;
                }
                job.count = maxAmountToPickup;
            }
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            // 失败条件：如果能量核心被摧毁、为空或被禁止
            this.FailOn(() => IngestibleSource.DestroyedOrNull() || !IngestibleSource.IngestibleNow);

            // 初始化 eatingFromInventory
            eatingFromInventory = pawn.inventory != null && pawn.inventory.Contains(IngestibleSource);

            // 定义咀嚼Toil
            chewing = Toils_Ingest.ChewIngestible(pawn, ChewDurationMultiplier, IngestibleSourceInd, TargetIndex.None)
                .FailOn((Toil x) => !IngestibleSource.Spawned && (pawn.carryTracker == null || pawn.carryTracker.CarriedThing != IngestibleSource))
                .FailOnCannotTouch(IngestibleSourceInd, PathEndMode.Touch);

            // 根据是否从背包摄入，选择不同的Toil序列
            foreach (Toil item in PrepareToIngestToils(chewing))
            {
                yield return item;
            }

            yield return chewing;

            // 最终处理能量摄取
            Toil finalizeToil = ToilMaker.MakeToil("FinalizeWulaEnergyIngest");
            finalizeToil.initAction = delegate
            {
                Pawn actor = finalizeToil.actor;
                Thing thing = actor.carryTracker.CarriedThing; // 从carryTracker获取，因为Toils_Ingest.ChewIngestible会处理携带

                if (thing == null)
                {
                    actor.jobs.EndCurrentJob(JobCondition.Incompletable);
                    return;
                }

                Need_WulaEnergy energyNeed = actor.needs.TryGetNeed<Need_WulaEnergy>();
                if (energyNeed == null)
                {
                    actor.jobs.EndCurrentJob(JobCondition.Errored);
                    return;
                }

                ThingDefExtension_EnergySource ext = thing.def.GetModExtension<ThingDefExtension_EnergySource>();
                if (ext == null)
                {
                    actor.jobs.EndCurrentJob(JobCondition.Errored);
                    return;
                }

                energyNeed.CurLevel += ext.energyAmount;
                thing.Destroy(DestroyMode.Vanish);
            };
            finalizeToil.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return finalizeToil;
        }

        // 辅助方法，根据情况返回不同的Toil序列
        private IEnumerable<Toil> PrepareToIngestToils(Toil chewToil)
        {
            if (eatingFromInventory)
            {
                yield return Toils_Misc.TakeItemFromInventoryToCarrier(pawn, IngestibleSourceInd);
            }
            else
            {
                // 类似原版JobDriver_Ingest的ToolUser逻辑
                yield return Toils_Goto.GotoThing(IngestibleSourceInd, PathEndMode.ClosestTouch)
                    .FailOnDespawnedNullOrForbidden(IngestibleSourceInd);
                yield return Toils_Ingest.PickupIngestible(IngestibleSourceInd, pawn);
            }
            // 不处理FindAdjacentEatSurface，因为乌拉能量核心可能不需要“吃表面”
            // 也不处理takeExtraIngestibles，因为乌拉能量核心通常是单次消耗
            yield break; // 确保迭代器结束
        }
    }
}
