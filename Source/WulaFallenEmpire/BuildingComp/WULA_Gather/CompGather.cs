using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;
using RimWorld;
using UnityEngine;

namespace WulaFallenEmpire
{
    // 召集组件属性
    public class CompProperties_Gather : CompProperties
    {
        public float gatherRange = 100f; // 召集范围（单元格）
        public SoundDef gatherSound;    // 召集音效
        public int cooldownTicks = 1200; // 召集冷却时间（tick，默认20秒）
        
        // 转化相关
        public bool canTransformPawns = true; // 是否允许转化Pawn
        public IntVec3 spawnOffset = IntVec3.Zero; // 新Pawn生成位置偏移
        
        public CompProperties_Gather()
        {
            compClass = typeof(Comp_Gather);
        }
    }
    
    // 召集组件实现
    public class Comp_Gather : ThingComp
    {
        private int lastGatherTick = -1000; // 上一次召集的tick
        private bool gatheringActive = false; // 召集是否正在进行
        private int gatherDurationLeft = 0;   // 召集持续时间剩余
        
        // Gizmo缓存
        private Command_Action cachedGizmo;
        
        // 属性
        public CompProperties_Gather Props => (CompProperties_Gather)props;
        
        public bool CanGatherNow
        {
            get
            {
                if (!parent.Spawned || parent.Destroyed)
                    return false;
                    
                // 检查冷却时间
                int currentTick = Find.TickManager.TicksGame;
                if (currentTick - lastGatherTick < Props.cooldownTicks)
                    return false;
                    
                // 检查建筑是否可用
                if (parent is Building building)
                {
                    if (building.IsBrokenDown() || building.IsForbidden(Faction.OfPlayer))
                        return false;
                }
                
                return true;
            }
        }
        
        public bool IsGatheringActive => gatheringActive;
        
        // 公开方法：检查是否可以进行转化
        public bool CanTransformPawn(Pawn pawn)
        {
            if (!Props.canTransformPawns)
                return false;
                
            if (!parent.Spawned || parent.Destroyed)
                return false;
                
            // 检查Pawn是否拥有AutonomousCat组件
            if (pawn.TryGetComp<Comp_AutonomousCat>() == null)
                return false;
                
            // 检查建筑是否可用
            if (parent is Building building)
            {
                if (building.IsBrokenDown() || building.IsForbidden(Faction.OfPlayer))
                    return false;
            }
            
            return true;
        }
        
        // 获取生成位置
        public IntVec3 GetSpawnPosition()
        {
            if (parent.Spawned)
            {
                return parent.Position + Props.spawnOffset;
            }
            return parent.Position;
        }
        
        // 公开方法：执行转化
        public void TransformPawn(Pawn originalPawn, PawnKindDef targetPawnKind)
        {
            if (!CanTransformPawn(originalPawn))
                return;
                
            try
            {
                // 记录原始信息
                Map map = originalPawn.Map;
                IntVec3 position = GetSpawnPosition();
                Faction faction = originalPawn.Faction;
                
                // 检查位置是否可用
                if (!position.Walkable(map) || position.Fogged(map))
                {
                    // 尝试在周围寻找可用位置
                    if (!CellFinder.TryFindRandomCellNear(position, map, 3, 
                        (c) => c.Walkable(map) && !c.Fogged(map), out position))
                    {
                        Messages.Message("Wula_NoSpawnSpace".Translate(), MessageTypeDefOf.RejectInput);
                        return;
                    }
                }
                
                // 创建新的Pawn
                Pawn newPawn = PawnGenerator.GeneratePawn(targetPawnKind, faction);
                
                // 复制一些重要信息
                if (originalPawn.Name != null)
                {
                    newPawn.Name = originalPawn.Name;
                }
                newPawn.gender = originalPawn.gender;
                
                // 销毁原始Pawn
                originalPawn.Destroy(DestroyMode.Vanish);
                
                // 生成新的Pawn
                GenSpawn.Spawn(newPawn, position, map);
                
                // 为新Pawn添加AutonomousCat组件（如果还没有）
                EnsureAutonomousCatComponent(newPawn);
                
                // 显示消息
                Messages.Message(
                    "Wula_TransformComplete".Translate(originalPawn.LabelShort, newPawn.LabelShort),
                    MessageTypeDefOf.PositiveEvent
                );
                
                // 记录日志
                if (Prefs.DevMode)
                {
                    Log.Message($"[CompGather] Transformation complete: {originalPawn.LabelShort} -> {newPawn.LabelShort}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error completing transformation: {ex.Message}");
            }
        }
        
        // 确保新Pawn有AutonomousCat组件
        private void EnsureAutonomousCatComponent(Pawn newPawn)
        {
            // 检查是否已经有AutonomousCat组件
            if (newPawn.TryGetComp<Comp_AutonomousCat>() != null)
                return;
                
            // 检查Pawn的定义中是否有AutonomousCat组件
            var compProps = newPawn.def.comps?.Find(c => c.compClass == typeof(Comp_AutonomousCat)) as CompProperties_AutonomousCat;
            if (compProps == null)
            {
                // 如果没有，添加一个默认的AutonomousCat组件
                newPawn.AllComps.Add(new Comp_AutonomousCat()
                {
                    parent = newPawn,
                    props = new CompProperties_AutonomousCat()
                    {
                        autoDraftOnGather = true
                    }
                });
                
                if (Prefs.DevMode)
                {
                    Log.Message($"[CompGather] Added AutonomousCat component to {newPawn.LabelShort}");
                }
            }
        }
        
        // Gizmo显示
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }
            
            // 只对玩家派系显示
            if (parent.Faction == Faction.OfPlayer)
            {
                if (cachedGizmo == null)
                {
                    InitializeGizmo();
                }
                
                yield return cachedGizmo;
            }
        }
        
        private void InitializeGizmo()
        {
            cachedGizmo = new Command_Action();
            cachedGizmo.defaultLabel = "Wula_GatherCats".Translate();
            cachedGizmo.defaultDesc = "Wula_GatherCatsDesc".Translate();
            cachedGizmo.icon = ContentFinder<Texture2D>.Get("UI/Gizmos/GatherCats", false) ?? BaseContent.BadTex;
            cachedGizmo.action = StartGathering;
            
            // 添加冷却时间显示
            cachedGizmo.disabledReason = GetDisabledReason();
            
            // 热键
            cachedGizmo.hotKey = KeyBindingDefOf.Misc2;
        }
        
        private string GetDisabledReason()
        {
            if (!parent.Spawned || parent.Destroyed)
                return "Building destroyed or not spawned";
                
            int currentTick = Find.TickManager.TicksGame;
            int ticksSinceLastGather = currentTick - lastGatherTick;
            
            if (ticksSinceLastGather < Props.cooldownTicks)
            {
                int remainingTicks = Props.cooldownTicks - ticksSinceLastGather;
                return "Wula_GatherCooldown".Translate(remainingTicks.ToStringTicksToPeriod());
            }
            
            if (parent is Building building && building.IsBrokenDown())
                return "Wula_BuildingBroken".Translate();
                
            return null;
        }
        
        // 开始召集
        private void StartGathering()
        {
            if (!CanGatherNow)
                return;
            
            // 记录召集时间
            lastGatherTick = Find.TickManager.TicksGame;
            
            // 设置召集状态
            gatheringActive = true;
            gatherDurationLeft = 60; // 持续1秒
            
            // 查找并命令范围内的 Autonomous Cats
            GatherAutonomousCats();
            
            // 显示消息
            Messages.Message("Wula_GatheringStarted".Translate(), MessageTypeDefOf.PositiveEvent);
            
            // 刷新Gizmo状态
            cachedGizmo.disabledReason = GetDisabledReason();
        }
        
        // 查找并命令 Autonomous Cats
        private void GatherAutonomousCats()
        {
            if (parent.Map == null)
                return;
                
            // 查找范围内的所有 Autonomous Cats
            List<Pawn> autonomousCats = new List<Pawn>();
            foreach (Pawn pawn in parent.Map.mapPawns.AllPawnsSpawned)
            {
                // 检查是否拥有 AutonomousCat 组件
                var comp = pawn.TryGetComp<Comp_AutonomousCat>();
                if (comp != null)
                {
                    // 检查是否有待处理的转化，如果有则不响应召集
                    if (comp.PendingTransformTarget != null)
                        continue;
                        
                    // 检查距离
                    float distance = pawn.Position.DistanceTo(parent.Position);
                    if (distance <= Props.gatherRange)
                    {
                        autonomousCats.Add(pawn);
                    }
                }
            }
            
            // 命令每个 Autonomous Cat
            foreach (Pawn cat in autonomousCats)
            {
                CommandCatToGather(cat);
            }
            
            // 报告召集数量
            if (autonomousCats.Count > 0)
            {
                Messages.Message(
                    "Wula_CatsGathered".Translate(autonomousCats.Count),
                    MessageTypeDefOf.NeutralEvent
                );
            }
        }
        
        // 命令单个 Autonomous Cat
        private void CommandCatToGather(Pawn cat)
        {
            if (cat == null || !cat.Spawned || cat.Dead)
                return;
                
            try
            {
                // 获取 AutonomousCat 组件
                var comp = cat.TryGetComp<Comp_AutonomousCat>();
                if (comp != null)
                {
                    // 使用组件的响应方法
                    comp.RespondToGather(parent);
                }
                else
                {
                    // 如果没有组件，使用默认行为
                    if (cat.drafter != null)
                    {
                        cat.drafter.Drafted = true;
                    }
                    
                    Job job = new Job(JobDefOf.Goto, parent.Position);
                    job.expiryInterval = 30000;
                    job.checkOverrideOnExpire = true;
                    
                    if (cat.CurJob != null)
                    {
                        cat.jobs.EndCurrentJob(JobCondition.InterruptForced);
                    }
                    
                    cat.jobs.StartJob(job, JobCondition.InterruptForced);
                }
                
                // 记录日志
                if (Prefs.DevMode)
                {
                    Log.Message($"[CompGather] Cat {cat.LabelShort} commanded to gather at {parent.Position}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error commanding cat to gather: {ex.Message}");
            }
        }
        
        // 每帧更新
        public override void CompTick()
        {
            base.CompTick();
            
            if (gatheringActive)
            {
                gatherDurationLeft--;
                if (gatherDurationLeft <= 0)
                {
                    gatheringActive = false;
                }
            }
            
            // 每30tick更新一次Gizmo状态
            if (parent.IsHashIntervalTick(30) && cachedGizmo != null)
            {
                cachedGizmo.disabledReason = GetDisabledReason();
            }
        }
        
        // 保存/加载数据
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref lastGatherTick, "lastGatherTick", -1000);
            Scribe_Values.Look(ref gatheringActive, "gatheringActive", false);
            Scribe_Values.Look(ref gatherDurationLeft, "gatherDurationLeft", 0);
        }
        
        // 绘制效果（可选）
        public override void PostDraw()
        {
            base.PostDraw();
            
            // 如果正在召集，绘制一个光环效果
            if (gatheringActive)
            {
                float pulse = Mathf.Sin(Find.TickManager.TicksGame * 0.1f) * 0.5f + 0.5f;
                GenDraw.DrawRadiusRing(parent.Position, Props.gatherRange, Color.Lerp(Color.cyan, Color.white, pulse), 
                    (c) => (c.x + c.y + Find.TickManager.TicksGame) % 10 < 5);
            }
        }
    }
}
