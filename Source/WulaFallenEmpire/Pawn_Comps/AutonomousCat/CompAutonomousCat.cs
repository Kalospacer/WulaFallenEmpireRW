using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;
using RimWorld;
using UnityEngine;

namespace WulaFallenEmpire
{
    // AutonomousCat组件属性
    public class CompProperties_AutonomousCat : CompProperties
    {
        public bool autoDraftOnGather = true;      // 被召集时自动征召
        
        // 转化功能
        public List<PawnKindDef> transformablePawnKinds; // 可转化的Pawn种类列表
        public float transformTime = 3f;           // 转化所需时间（秒）
        public SoundDef transformSound;            // 转化音效
        public EffecterDef transformEffect;        // 转化特效
        
        // 外观设置
        public Color gatherLineColor = new Color(0.2f, 0.8f, 0.2f, 0.8f); // 召集线颜色
        public float gatherLineWidth = 0.2f;        // 召集线宽度
        
        public CompProperties_AutonomousCat()
        {
            compClass = typeof(Comp_AutonomousCat);
        }
    }
    
    // AutonomousCat组件实现
    public class Comp_AutonomousCat : ThingComp
    {
        // 状态跟踪
        private bool isRespondingToGather = false;
        private Thing gatherTarget = null;
        private int gatherResponseEndTick = 0;
        
        // 转化目标
        private PawnKindDef pendingTransformTarget = null;
        
        // Gizmo缓存
        private Command_Action cachedTransformGizmo;
        private bool gizmoInitialized = false;
        
        // 属性
        public CompProperties_AutonomousCat Props => (CompProperties_AutonomousCat)props;
        
        // 公开属性
        public bool IsRespondingToGather => isRespondingToGather;
        public Thing GatherTarget => gatherTarget;
        public PawnKindDef PendingTransformTarget => pendingTransformTarget;
        
        public override void Initialize(CompProperties props)
        {
            base.Initialize(props);
        }
        
        // 响应召集命令
        public void RespondToGather(Thing target)
        {
            if (parent is Pawn pawn && !pawn.Dead && pawn.Spawned)
            {
                try
                {
                    // 如果有待处理的转化，不响应召集
                    if (pendingTransformTarget != null)
                        return;
                        
                    // 设置目标
                    gatherTarget = target;
                    isRespondingToGather = true;
                    gatherResponseEndTick = Find.TickManager.TicksGame + 6000; // 100秒后超时
                    
                    // 自动征召
                    if (Props.autoDraftOnGather && pawn.drafter != null)
                    {
                        pawn.drafter.Drafted = true;
                    }
                    
                    // 创建移动到目标的Job
                    if (target != null && target.Spawned)
                    {
                        CreateGatherJob(pawn, target);
                    }
                    
                    // 记录日志
                    if (Prefs.DevMode)
                    {
                        Log.Message($"[CompAutonomousCat] {pawn.LabelShort} responding to gather call from {target?.LabelShort ?? "unknown"}");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"Error in RespondToGather for {parent.LabelShort}: {ex.Message}");
                }
            }
        }
        
        // 创建召集任务
        private void CreateGatherJob(Pawn pawn, Thing target)
        {
            try
            {
                // 清除当前任务
                if (pawn.CurJob != null)
                {
                    pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
                }
                
                // 创建移动到目标的Job
                Job job = new Job(JobDefOf.Goto, target.Position);
                
                // 设置任务优先级和参数
                job.expiryInterval = 30000; // 长时间有效
                job.checkOverrideOnExpire = true;
                
                // 开始任务
                pawn.jobs.StartJob(job, JobCondition.InterruptForced);
            }
            catch (Exception ex)
            {
                Log.Error($"Error creating gather job for {pawn.LabelShort}: {ex.Message}");
            }
        }
        
        // 开始转化流程
        public void StartTransformation(PawnKindDef targetPawnKind)
        {
            if (parent is Pawn pawn && !pawn.Dead && pawn.Spawned)
            {
                try
                {
                    // 查找最近的Comp_Gather建筑
                    Thing closestGatherBuilding = FindClosestGatherBuilding();
                    
                    if (closestGatherBuilding == null)
                    {
                        Messages.Message(
                            "Wula_NoGatherBuilding".Translate(),
                            MessageTypeDefOf.RejectInput
                        );
                        return;
                    }
                    
                    // 存储转化目标
                    pendingTransformTarget = targetPawnKind;
                    
                    // 创建转化Job
                    Job job = JobMaker.MakeJob(Wula_JobDefOf.WULA_TransformPawn, closestGatherBuilding);
                    
                    // 清除当前任务并开始新任务
                    pawn.jobs.StopAll();
                    pawn.jobs.StartJob(job, JobCondition.InterruptForced);
                    
                    // 显示消息
                    Messages.Message(
                        "Wula_TransformStarted".Translate(pawn.LabelShort, targetPawnKind.label),
                        MessageTypeDefOf.NeutralEvent
                    );
                    
                    // 记录日志
                    if (Prefs.DevMode)
                    {
                        Log.Message($"[CompAutonomousCat] {pawn.LabelShort} starting transformation to {targetPawnKind.label}");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"Error starting transformation for {parent.LabelShort}: {ex.Message}");
                }
            }
        }
        
        // 清除转化目标（由JobDriver调用）
        public void ClearTransformTarget()
        {
            pendingTransformTarget = null;
        }
        
        // 查找最近的Comp_Gather建筑
        private Thing FindClosestGatherBuilding()
        {
            if (parent.Map == null)
                return null;
                
            Thing closestBuilding = null;
            float closestDistance = float.MaxValue;
            
            // 查找所有Comp_Gather建筑
            foreach (Thing thing in parent.Map.listerThings.AllThings)
            {
                if (thing.TryGetComp<Comp_Gather>() != null && 
                    thing.Spawned && 
                    !thing.Destroyed &&
                    thing.Faction == parent.Faction)
                {
                    float distance = thing.Position.DistanceTo(parent.Position);
                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        closestBuilding = thing;
                    }
                }
            }
            
            return closestBuilding;
        }
        
        // 每帧更新
        public override void CompTick()
        {
            base.CompTick();
            
            // 处理召集响应
            if (isRespondingToGather)
            {
                UpdateGatherResponse();
            }
        }
        
        // 更新召集响应
        private void UpdateGatherResponse()
        {
            // 检查是否超时
            if (Find.TickManager.TicksGame > gatherResponseEndTick)
            {
                EndGatherResponse();
                return;
            }
            
            // 检查目标是否仍然有效
            if (gatherTarget == null || !gatherTarget.Spawned || gatherTarget.Destroyed)
            {
                EndGatherResponse();
                return;
            }
            
            // 检查Pawn是否已经死亡或无法行动
            if (parent is Pawn pawn && (pawn.Dead || pawn.Downed || !pawn.Spawned))
            {
                EndGatherResponse();
                return;
            }
            
            // 检查是否已经到达目标附近
            if (parent is Pawn movingPawn && movingPawn.Position.DistanceTo(gatherTarget.Position) < 5f)
            {
                // 到达目标，结束召集响应
                EndGatherResponse();
                
                // 可选：到达后执行其他动作
                if (movingPawn.CurJobDef == JobDefOf.Goto)
                {
                    // 切换到等待或警戒状态
                    movingPawn.jobs.EndCurrentJob(JobCondition.Succeeded);
                }
            }
        }
        
        // 结束召集响应
        private void EndGatherResponse()
        {
            isRespondingToGather = false;
            gatherTarget = null;
        }
        
        // 绘制效果
        public override void PostDraw()
        {
            base.PostDraw();
            
            // 如果正在响应召集，绘制连接线
            if (isRespondingToGather && gatherTarget != null && parent.Spawned)
            {
                DrawGatherLine();
            }
        }
        
        // 绘制召集线
        private void DrawGatherLine()
        {
            Vector3 startPos = parent.DrawPos;
            Vector3 endPos = gatherTarget.DrawPos;
            
            // 提升到覆盖层高度
            startPos.y = AltitudeLayer.MetaOverlays.AltitudeFor();
            endPos.y = AltitudeLayer.MetaOverlays.AltitudeFor();
            
            // 绘制连接线
            GenDraw.DrawLineBetween(
                startPos, 
                endPos
            );
        }
        
        // Gizmo显示
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }
            
            // 只对玩家派系显示
            if (parent.Faction == Faction.OfPlayer && parent is Pawn pawn && !pawn.Dead)
            {
                // 延迟初始化Gizmo
                if (!gizmoInitialized)
                {
                    InitializeGizmo();
                    gizmoInitialized = true;
                }
                
                if (cachedTransformGizmo != null)
                {
                    // 如果已有待处理的转化，显示不同图标或状态
                    if (pendingTransformTarget != null)
                    {
                        cachedTransformGizmo.disabledReason = "Wula_TransformPending".Translate(pendingTransformTarget.label);
                    }
                    
                    yield return cachedTransformGizmo;
                }
            }
        }
        
        // 初始化Gizmo
        private void InitializeGizmo()
        {
            if (Props.transformablePawnKinds != null && Props.transformablePawnKinds.Count > 0)
            {
                cachedTransformGizmo = new Command_Action();
                cachedTransformGizmo.defaultLabel = "Wula_Transform".Translate();
                cachedTransformGizmo.defaultDesc = "Wula_TransformDesc".Translate();
                cachedTransformGizmo.icon = ContentFinder<Texture2D>.Get("UI/Gizmos/Transform", false) ?? BaseContent.BadTex;
                cachedTransformGizmo.action = () => ShowTransformMenu();
                
                // 设置热键
                cachedTransformGizmo.hotKey = KeyBindingDefOf.Misc3;
            }
        }
        
        // 显示转化菜单
        private void ShowTransformMenu()
        {
            if (Props.transformablePawnKinds == null || Props.transformablePawnKinds.Count == 0)
                return;
                
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            
            foreach (PawnKindDef pawnKind in Props.transformablePawnKinds)
            {
                options.Add(new FloatMenuOption(
                    pawnKind.LabelCap,
                    () => StartTransformation(pawnKind),
                    pawnKind.race.uiIcon,
                    Color.white
                ));
            }
            
            // 添加取消选项
            options.Add(new FloatMenuOption(
                "Cancel".Translate(),
                null,
                MenuOptionPriority.Default
            ));
            
            Find.WindowStack.Add(new FloatMenu(options));
        }
        
        // 保存/加载数据
        public override void PostExposeData()
        {
            base.PostExposeData();
            
            Scribe_Values.Look(ref isRespondingToGather, "isRespondingToGather", false);
            Scribe_References.Look(ref gatherTarget, "gatherTarget");
            Scribe_Values.Look(ref gatherResponseEndTick, "gatherResponseEndTick", 0);
            
            // 保存/加载转化目标
            Scribe_Defs.Look(ref pendingTransformTarget, "pendingTransformTarget");
            
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // 重置不正确的状态
                if (isRespondingToGather && (gatherTarget == null || !gatherTarget.Spawned))
                {
                    isRespondingToGather = false;
                }
                
                // 重置Gizmo缓存
                gizmoInitialized = false;
                cachedTransformGizmo = null;
            }
        }
    }
}
