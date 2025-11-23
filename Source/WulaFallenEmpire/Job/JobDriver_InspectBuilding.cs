using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    public class JobDriver_InspectBuilding : JobDriver
    {
        private const TargetIndex BuildingIndex = TargetIndex.A;
        
        private int ticksLeft;

        // 定义考察效果 - 可以使用现有的效果或创建自定义效果
        private static readonly EffecterDef InspectEffect = EffecterDefOf.Research; // 使用研究效果作为临时替代
        private static readonly SoundDef InspectSound = SoundDefOf.Interact_CleanFilth; // 使用建造声音作为临时替代

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // 预订目标建筑
            return pawn.Reserve(job.targetA, job, 1, 1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            // 失败条件
            this.FailOnDestroyedOrNull(BuildingIndex);
            this.FailOnDespawnedOrNull(BuildingIndex);
            this.FailOn(() => !pawn.health.capacities.CapableOf(PawnCapacityDefOf.Moving));

            // 第一步：前往目标建筑
            yield return Toils_Goto.GotoCell(BuildingIndex, PathEndMode.Touch)
                .FailOnSomeonePhysicallyInteracting(BuildingIndex);

            // 第二步：进行考察（带动画效果）
            Toil inspectToil = CreateInspectionToil();
            yield return inspectToil;
        }

        /// <summary>
        /// 创建带动画效果的考察工作
        /// </summary>
        private Toil CreateInspectionToil()
        {
            Toil inspectToil = new Toil();
            
            inspectToil.initAction = () =>
            {
                ticksLeft = job.expiryInterval;
                
                // 记录日志
                if (Prefs.DevMode)
                {
                    Log.Message($"[JobDriver_InspectBuilding] {pawn.Name} started inspecting {TargetThingA.Label} for {ticksLeft} ticks");
                }
            };

            inspectToil.tickAction = () =>
            {
                ticksLeft--;
                
                // 每 tick 检查是否完成
                if (ticksLeft <= 0)
                {
                    ReadyForNextToil();
                }
            };

            inspectToil.defaultCompleteMode = ToilCompleteMode.Delay;
            inspectToil.defaultDuration = job.expiryInterval;
            
            // 添加动画效果
            inspectToil.WithEffect(InspectEffect, BuildingIndex);
            
            // 添加音效
            inspectToil.PlaySustainerOrSound(() => InspectSound);
            
            // 添加进度条（可选）
            inspectToil.WithProgressBar(BuildingIndex, 
                () => 1f - ((float)ticksLeft / job.expiryInterval), 
                interpolateBetweenActorAndTarget: true);
            
            inspectToil.AddFinishAction(() =>
            {
                // 考察完成后的处理
                OnInspectionComplete();
            });

            return inspectToil;
        }

        /// <summary>
        /// 考察完成时的处理
        /// </summary>
        private void OnInspectionComplete()
        {
            // 可以在这里添加考察完成后的效果
            // 例如：增加技能经验、触发事件等
            
            if (Prefs.DevMode)
            {
                Log.Message($"[JobDriver_InspectBuilding] {pawn.Name} completed inspection of {TargetThingA.Label}");
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref ticksLeft, "ticksLeft", 0);
        }
    }
}
