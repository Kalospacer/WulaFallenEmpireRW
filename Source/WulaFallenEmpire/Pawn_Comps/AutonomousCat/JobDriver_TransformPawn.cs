using System.Collections.Generic;
using Verse;
using Verse.AI;
using RimWorld;
using UnityEngine;

namespace WulaFallenEmpire
{
    public class JobDriver_TransformPawn : JobDriver
    {
        private const TargetIndex GatherBuildingIndex = TargetIndex.A;
        
        private Comp_Gather GatherComp => job.targetA.Thing?.TryGetComp<Comp_Gather>();
        private Comp_AutonomousCat PawnComp => pawn.TryGetComp<Comp_AutonomousCat>();
        
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // 预留目标建筑
            //if (!pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed))
            //{
            //    return false;
            //}
            return true;
        }
        
        protected override IEnumerable<Toil> MakeNewToils()
        {
            // 第1步：移动到目标建筑
            yield return Toils_Goto.GotoCell(GatherBuildingIndex, PathEndMode.InteractionCell);
            
            // 第2步：进行转化工作
            Toil transformToil = new Toil();
            transformToil.initAction = () =>
            {
                // 获取目标建筑
                Thing gatherBuilding = job.targetA.Thing;
                if (gatherBuilding == null || gatherBuilding.Destroyed)
                {
                    ReadyForNextToil();
                    return;
                }
                
                // 获取Comp_Gather
                var gatherComp = gatherBuilding.TryGetComp<Comp_Gather>();
                if (gatherComp == null)
                {
                    ReadyForNextToil();
                    return;
                }
                
                // 确保可以转化
                if (!gatherComp.CanTransformPawn(pawn))
                {
                    ReadyForNextToil();
                    return;
                }
            };
            
            transformToil.tickAction = () =>
            {
                // 确保目标建筑仍然有效
                Thing gatherBuilding = job.targetA.Thing;
                if (gatherBuilding == null || gatherBuilding.Destroyed || 
                    gatherBuilding.Map != pawn.Map || 
                    pawn.Position.DistanceTo(gatherBuilding.Position) > 3f)
                {
                    // 中断转化，清除转化目标
                    PawnComp?.ClearTransformTarget();
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                
                // 面向建筑
                pawn.rotationTracker.FaceCell(gatherBuilding.Position);
                
                // 播放转化效果
                if (Find.TickManager.TicksGame % 20 == 0)
                {
                    PlayTransformEffects();
                }
            };
            
            // 从Comp_AutonomousCat获取转化时间
            var compProps = PawnComp?.Props as CompProperties_AutonomousCat;
            int transformDuration = compProps != null ? 
                Mathf.RoundToInt(compProps.transformTime * 60f) : 180; // 默认3秒
            
            transformToil.defaultCompleteMode = ToilCompleteMode.Delay;
            transformToil.defaultDuration = transformDuration;
            yield return transformToil;
            
            // 第3步：完成转化
            yield return new Toil
            {
                initAction = () =>
                {
                    // 获取目标建筑
                    Thing gatherBuilding = job.targetA.Thing;
                    if (gatherBuilding == null || gatherBuilding.Destroyed)
                    {
                        return;
                    }
                    
                    // 获取Comp_Gather
                    var gatherComp = gatherBuilding.TryGetComp<Comp_Gather>();
                    if (gatherComp == null)
                    {
                        return;
                    }
                    
                    // 获取要转化的PawnKindDef（从Comp_AutonomousCat中）
                    var targetPawnKind = PawnComp?.PendingTransformTarget;
                    if (targetPawnKind == null)
                    {
                        return;
                    }
                    
                    // 调用Comp_Gather的转化方法
                    gatherComp.TransformPawn(pawn, targetPawnKind);
                    
                    // 清除转化目标
                    PawnComp?.ClearTransformTarget();
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }
        
        // 播放转化效果
        private void PlayTransformEffects()
        {
            // 播放音效
            var compProps = PawnComp?.Props as CompProperties_AutonomousCat;

            // 播放特效
            if (compProps?.transformEffect != null)
            {
                compProps.transformEffect.Spawn(pawn.Position, pawn.Map).Cleanup();
            }
        }
    }
}
